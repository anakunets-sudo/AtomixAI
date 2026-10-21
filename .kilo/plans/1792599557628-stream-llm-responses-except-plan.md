# Перевод ответов LLM на streaming (кроме `plan_payload`)

## Цель

Показывать пользователю текст финального отчёта и сообщения вытеснения по мере генерации,
вместо ожидания всего ответа. Запрос плана (`plan_payload` + `execute_bim_sequence`) остаётся
блокирующим: функция не стримится, tool-call должен быть целым.

В `src/AtomixAI.Main/Orchestrator/orchestrator.py` ровно три HTTP-запроса:

| Строка | Что | Решение |
|---|---|---|
| 710 | `plan_payload` (tool_choice) | **оставить как есть** |
| 526 | `eviction_payload` (`execute_eviction_protocol`) | перевести на stream |
| 950 | `human_payload` (гуманизация отчёта) | перевести на stream |

C# и pipe-протокол менять не нужно: `ui_log` из Python попадает в `McpHost.ProcessRequest`
→ `OnMessageReceived` → `PostWebMessageAsJson` как произвольный JSON, поля `phase`/`content`
доходят до JS без изменений. Правки — только Python + `wwwroot/js/main.js` + `style.css`.

## Принятые решения

1. **Синхронное чтение SSE, без фонового потока.** `requests.post(..., stream=True)` +
   `resp.iter_lines()`. Отмена внутри стрима не отслеживается.
2. **Cancel после начала ответа не останавливает ответ.** JS скрывает ссылку Cancel, как только
   пришёл первый непустой `phase: "stream"`. Python не опрашивает abort в цикле стрима, а abort,
   пришедший уже после появления текста, считается нефатальным (лог + доставка финального сообщения).
   Cancel до первого токена (в фазе плана / выполнения батча) работает как сейчас.
3. **Протокол UI — снапшоты, а не дельты.** Python шлёт `ui_log` с `role: "ai"`, `phase: "stream"`,
   `content` = весь накопленный текст, `turn` = generation. JS просто перезаписывает текст
   в текущем «thinking»-пузыре. Идемпотентно при любых сбоях доставки.
4. **Проверка утечки промпта (`PROMPT_LEAK_TRIGGERS`) остаётся только в конце.** Текст уже
   показан; при срабатывании финальный `ui_log` заменяет пузырь обычной ошибкой.
5. **Fallback без SSE.** Если провайдер проигнорировал `stream: true` и вернул JSON — разобрать
   `choices[0].message.content` как раньше. Если стрим оборвался, но накопленный текст непустой —
   использовать частичный текст (лог с ошибкой), а не выбрасывать его.
6. **Роутинг провайдеров не трогаем.** Мёртвый блок строк 943–947 пишет `provider`/`service_tier`
   в уже отправленный `plan_payload` (no-op). Оставить как есть в этой задаче (см. «Риски»).

## Задачи

### 1. Python: общий helper стрима (в `orchestrator.py`)

Рядом с `execute_eviction_protocol` добавить:

- `_push_stream_snapshot(client, text, generation)` — отправляет
  `client.send_receive({"action": "ui_log", "role": "ai", "phase": "stream", "content": text, "turn": generation})`;
  входящий `ui_event` не поднимает `RequestAborted`, а логируется (`[STREAM] abort ignored after text`).
- `stream_completion(payload, headers, client, generation, timeout, log_tag)`:
  1. `payload = dict(payload); payload["stream"] = True` (исходный dict не мутировать).
  2. `with requests.post(FULL_URL, json=payload, headers=headers, stream=True, timeout=timeout) as resp:`
  3. `resp.status_code != 200` → прочитать `resp.text`, залогировать, вернуть `("", False, "http")`
     (вызывающий код формирует те же сообщения об ошибке, что и сейчас).
  4. Не-`text/event-stream` (`Content-Type` содержит `application/json`) → `resp.json()`,
     взять `choices[0].message.content`, вернуть `(text, False, None)`.
  5. Итерация `for raw in resp.iter_lines(decode_unicode=False)`:
     - `raw` пустой или начинается с `:` (`: OPENROUTER PROCESSING`) → пропустить;
     - декодировать `utf-8`, `errors="replace"`; снять префикс `data:`; `[DONE]` → break;
     - `json.loads` в try/except (битую строку — в лог и продолжить);
     - если в объекте есть `error` → лог, break;
     - `delta = choices[0].delta`; копить только `delta.get("content")`
       (`reasoning`/`reasoning_content` игнорировать).
     - throttle снапшотов: первый непустой текст сразу, далее не чаще `STREAM_FLUSH_MS = 100`
       и/или при приросте ≥ 12 символов; в конце — финальный снапшот.
  6. Вернуть `(text, streamed_any, error_kind)`; частичный текст сохраняется при обрыве.
- Константы рядом с `MAX_FLOOD_ATTEMPTS`: `STREAM_FLUSH_MS = 100`, `STREAM_FLUSH_CHARS = 12`.

### 2. Гуманизация (`process_ai_logic`, строки 917–1007)

- `human_payload` собирать как сейчас (без `stream`), затем
  `final_report_text, streamed, err = stream_completion(human_payload, headers, client, generation, timeout=60, log_tag="REPORT")`.
- Удалить `raise_for_status()` + `final_response.json()` и разбор `final_data`.
- Если текст пустой → сохранить текущий путь `ValueError("Final report response was empty or invalid.")`
  → фолбэк `Execution context: ...`, как сейчас.
- Проверку `PROMPT_LEAK_TRIGGERS` оставить после получения полного текста (без изменений).
- `exchange(client, {"action": "ping"}, generation)` перед проверкой утечки заменить:
  если `streamed` и текст непустой — использовать `client.send_receive({"action": "ping"})` и только
  логировать abort; если ничего не стримилось — оставить `exchange(...)`, чтобы прежняя семантика
  отмены при пустом ответе сохранилась.
- `session_state.clear()`, сохранение истории, `final_report_created` — без изменений.

### 3. Вытеснение (`execute_eviction_protocol`)

- Сигнатура: `execute_eviction_protocol(user_text, client, generation, detected_lang, headers, chip_context="[]")`.
- `eviction_payload` собирать как сейчас, затем `stream_completion(..., timeout=20, log_tag="EVICTION")`.
- При пустом тексте вернуть прежние сообщения (`API returned HTTP ...` / `Network connection failed ...`),
  при частичном — вернуть частичный текст.
- Обновить оба вызова: строки 783 и 797.

### 4. UI (`src/AtomixAI.UI/wwwroot/js/main.js`, `style.css`)

- В `handleAIResponse` добавить ветку **до** `setBusy(false)`:
  ```js
  if (data.phase === 'stream' && thinkingMsgId) {
      // устаревший/отменённый ход — молча игнорируем (isStaleOrCancelledReply уже отработал выше)
      const msgEl = document.getElementById(thinkingMsgId);
      if (!msgEl) return;
      if (!data.content) return;
      msgEl.classList.add('streaming-state');
      let label = msgEl.querySelector('.streaming-label');
      if (!label) {
          msgEl.replaceChildren();               // убрать shimmer-«thinking»
          label = document.createElement('span');
          label.className = 'streaming-label';
          msgEl.appendChild(label);
      }
      // Cancel после начала ответа не нужен
      const cancel = msgEl.querySelector('.cancel-btn-link');
      if (cancel) cancel.remove();
      label.textContent = data.content;           // без markdown: перерисовка на каждый снапшот
      scrollChatToBottom();
      return;
  }
  ```
- `phase === 'plan'` оставить как есть (превью плана до выполнения).
- Финальный `ui_log` (без `phase`) уже делает `classList.remove('thinking-state')`,
  `renderMessageContent(...)` (markdown), `addRating` при `final_report_created`. Дополнительно
  снять `streaming-state` вместе с `thinking-state`, чтобы не осталось `white-space: pre-wrap`.
- `style.css`: рядом с `.thinking-state` добавить
  ```css
  .msg.ai.streaming-state { display: block; }
  .streaming-label { font-style: normal; white-space: pre-wrap; overflow-wrap: anywhere; }
  ```
  (shimmer/`background-clip: text` не наследуется — новый элемент не анимируется).
- Новых i18n-строк не добавлять.

## Проверка

1. `python -m py_compile src/AtomixAI.Main/Orchestrator/orchestrator.py` и `git diff --check`.
2. Ручные сценарии в Revit:
   - обычный успешный сценарий: текст отчёта печатается инкрементально в том же пузыре, финал
     заменяется на markdown + рейтинг, второго пузыря нет;
   - сценарий с ошибкой выполнения (`success: false`): стрим + финальный текст без `#`;
   - 2+ флуда → `execute_eviction_protocol` стримится, финальный `ui_log` завершает тот же пузырь;
   - Cancel в фазе плана и в фазе выполнения батча — ответ по-прежнему подавляется;
   - Cancel после первого чанка — ссылка уже скрыта, ответ доходит до конца.
3. `atomix_debug.log`: есть `[STREAM]` метки, нет дублей финального текста, нет исключений pipe.
4. Fallback: временно отправить запрос без `stream` (или замокать non-SSE ответ) — текст
   отображается одним куском, финальный пузырь корректен.

## Риски и ограничения

- `reasoning.exclude: True` + `effort: low` → до первого токена тишина, в это время работает
  «thinking»-анимация; Cancel в этот момент ещё доступен, но abort не прервёт HTTP-запрос —
  он сработает только после завершения стрима.
- Обрыв посреди SSE: показываем частичный текст; пустой ответ идёт по старому error-пути.
- Пуш снапшотов идёт через синхронный pipe round-trip (~10/с) — при узком канале возможен
  лёгкий лаг UI; при необходимости поднять `STREAM_FLUSH_MS`.
- Утечка системного промпта может мигнуть в чате до финальной проверки (осознанное решение).
- Мёртвый роутинг-код строк 943–947 (`human_payload`-ветка правит `plan_payload`) и опечатки
  `"teamoruter"` (519, 703, 943) в этой задаче не исправляются — отдельная задача.
