import os
import sys
import json
import ollama
import win32file
import win32pipe
import pywintypes
import time
import re

# Добавляем путь для импорта инструкций
script_dir = os.path.dirname(os.path.abspath(__file__))
if script_dir not in sys.path:
    sys.path.append(script_dir)

import instructions

# 1. ПРИНУДИТЕЛЬНОЕ СОЗДАНИЕ (ИЛИ ОЧИСТКА) ЛОГА
LOG_FILE = "atomix_debug.log"
# Используем "w", чтобы стереть старое содержимое при старте сессии
with open(LOG_FILE, "w", encoding="utf-8") as f:
    f.write(f"--- NEW SESSION: {time.ctime()} ---\n")
def log(msg):
    # Для последующих записей используем "a", чтобы лог рос в течение сессии
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(f"{time.strftime('%H:%M:%S')} | {msg}\n")
    print(msg)
log("[*] Checking dependencies...")

# --- CONFIGURATION ---
PIPE_NAME = r'\\.\pipe\AtomixAI_Bridge_Pipe'
MODEL_NAME = 'qwen2.5:7b' # deepseek-r1:8b  qwen2.5:7b

class RevitPipeClient:
    def __init__(self, name):
        self.name = name
        self.handle = None

    def connect(self):
        print(f"[*] Connecting to Revit ({self.name})...")
        while True:
            try:
                self.handle = win32file.CreateFile(
                    self.name, win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                    0, None, win32file.OPEN_EXISTING, 0, None)
                print("[+] Connection with Revit established.")
                return True
            except pywintypes.error:
                time.sleep(1)

    def send_receive(self, payload):
        try:
            win32file.WriteFile(self.handle, (json.dumps(payload) + "\n").encode('utf-8'))
            _, data = win32file.ReadFile(self.handle, 65536)
            decoded_data = data.decode('utf-8').strip()
            if not decoded_data: return None, None
            
            raw_res = json.loads(decoded_data)
            if isinstance(raw_res, dict) and "ui_event" in raw_res:
                return raw_res["result"], raw_res["ui_event"]
            return raw_res, None
        except Exception as e:
            print(f"[!] Pipe Error: {e}")
            return {"error": str(e)}, None

def mcp_to_ollama(mcp_tools):
    ready_tools = []
    for t in mcp_tools:
        ready_tools.append({
            "type": "function",
            "function": {
                "name": t["name"],
                "description": t.get("description", ""),
                "parameters": t.get("inputSchema", {"type": "object", "properties": {}})
            }
        })
    return ready_tools

detected_lang = "Rassian" 
lang_fixed = False 

HISTORY_FILE = os.path.join(script_dir, "atomix_history.json")

def save_history(history):
    try:
        # Сохраняем только User и Assistant (без тяжелых системных промптов)
        to_save = [m for m in history if m.get('role') in ['user', 'assistant']]
        with open(HISTORY_FILE, 'w', encoding='utf-8') as f:
            json.dump(to_save, f, ensure_ascii=False, indent=2)
    except Exception as e:
        log(f"[!] Error saving history: {e}")

def load_history():
    if os.path.exists(HISTORY_FILE):
        try:
            with open(HISTORY_FILE, 'r', encoding='utf-8') as f:
                data = json.load(f)
                log(f"[+] History loaded: {len(data)} messages.")
                return data
        except:
            return []
    return []

def extract_summary(data_obj):
    """Автоматически вытаскивает число и теги из любого анонимного класса."""
    # 1. Ищем количество элементов (total_success или count)
    count = data_obj.get("total_success") or data_obj.get("count") or "0"
    
    # 2. Ищем теги (из словаря tags)
    tags_dict = data_obj.get("tags") or {}
    tag_list = list(tags_dict.keys())
    primary_tag = tag_list[0] if tag_list else "#result"
    
    return str(count), primary_tag

# Инициализация при старте
chat_history = load_history()

detected_lang = None 

def process_ai_logic(user_text, client):
    """
    Оптимизированная логика: Прямая инъекция состояния Revit + Изоляция от старой истории.
    ИИ видит только: Правила + Текущие метки в Revit + Вопрос пользователя.
    """
    global chat_history, detected_lang
    log(">>> [LOGIC] Start of a clean session (Stateless Mode)...")

    # --- 1. АВТООПРЕДЕЛЕНИЕ ЯЗЫКА (Один раз за сессию через ИИ) ---
    if detected_lang is None:
        try:
            log(">>> [LANG] Detecting the user's language...")
            # Быстрый запрос без системных промптов
            lang_check = ollama.chat(
                model=MODEL_NAME,
                messages=[{'role': 'user', 'content': f"Identify the language of this text and return ONLY the language name in English (e.g., 'Russian', 'English', 'German'): {user_text}"}]
            )
            detected_lang = lang_check.message.content.strip()
            log(f">>> [LANG] Detect language: {detected_lang}")
        except:
            detected_lang = "English" # Дефолт при сбое
    
    try:
        # --- 1. СИНХРОНИЗАЦИЯ С REVIT (Актуальное состояние) ---
        # Получаем инструменты и живые метки (#), которые реально существуют в модели сейчас
        res_manual, _ = client.send_receive({"action": "get_manual"})
        res_context, _ = client.send_receive({"action": "get_context_state"})
        
        dynamic_manual = res_manual.get("manual", "Tools are not available.")
        current_tags = res_context.get("tags", "There are no active tags in memory.")

        # --- 2. СБОРКА СВЕЖЕГО КОНТЕКСТА (БЕЗ ИСТОРИИ) ---
        # Мы НЕ используем chat_history для Ollama, чтобы она не копировала старые ошибки.
        # Формируем промпт "Золотая рыбка": Правила + Текущая ситуация.
        
        system_rules = instructions.PROFILES['default']
        
        # Инъекция живого состояния Revit прямо перед вопросом
        revit_state = (
            f"### CURRENT REVIT STATE (TAGS):\n{current_tags}\n\n"
            f"### AVAILABLE BIM TOOLS:\n{dynamic_manual}\n\n"
            f"STRICT LANGUAGE: Respond ONLY in {detected_lang}."
        )

        # Формируем пакет сообщений: Инструкции -> Состояние -> Вопрос
        current_session = [
            {'role': 'system', 'content': system_rules},
            {'role': 'system', 'content': revit_state},
            {'role': 'user', 'content': user_text}              
        ]

        # --- 3. ЗАПРОС ПЛАНА У LLM ---
        log(f">>> [LOGIC] Request from {MODEL_NAME}...")
        response = ollama.chat(
            model=MODEL_NAME,
            messages=current_session,
            format='json', # Гарантируем JSON структуру {thought, sequence}
            options={'num_ctx': 8192, 'temperature': 0.0}
        )
        
        data = json.loads(response.message.content)
        ai_thought = data.get("thought", "Обработка...")
        ai_sequence = data.get("sequence", [])

        log(f">>> [LOGIC] Thought : {ai_thought}")
        log(f">>> [LOGIC] Sequence : {ai_sequence}")

        # --- 4. ВЫПОЛНЕНИЕ В REVIT (Batch Mode) ---
        execution_result = {"success": False, "message": "No action taken"}
        
        if ai_sequence:
            log(f"[*] Sending the sequence to Revit: {len(ai_sequence)} шагов...")
            # Отправляем весь пакет команд одной транзакцией
            revit_res, _ = client.send_receive({"action": "call_batch", "sequence": ai_sequence})
            
            # Ожидание результата через PING (Polling)
            if revit_res and "batch_queued" in str(revit_res.get("status", "")):
                # Ждем "Квитанцию" (AtomicResult) от Revit
                for _ in range(100): # Тайм-аут ~30 секунд
                    time.sleep(0.3)
                    poll_res, ui_event = client.send_receive({"action": "ping"})
                    
                    # Ищем маркер завершения батча в ответе
                    res = ui_event if ui_event else poll_res
                    if res and res.get("action") == "tool_execution_result":
                        execution_result = res # Это наш плоский AtomicResult с Message и Data
                        break
            
            # --- 5. ФИКСАЦИЯ ТОЛЬКО УДАЧНЫХ ОПЕРАЦИЙ ---
            # Мы пишем в историю только для внешнего лога/повтора, ИИ это не увидит в след. раз
            if execution_result.get("success"):
                log("[+] Success. Saving to the session log.")
                chat_history.append({
                    'role': 'user', 'content': user_text, 
                    'sequence': ai_sequence, 'status': 'success'
                })
                save_history(chat_history)
            else:
                log("[!] Execution error. Not adding to log history.")

            SYSTEM_PROMPT = (
                    f"### ROLE: Professional Revit Assistant"
                    f"### TASK: Humanize technical Revit reports."
                    f"### RULES:"
                    f"1. Language: {detected_lang}.\n"
                    f"2. BREVITY: Past tense, one short sentence only. No intros like 'Successfully', 'Steps completed', 'Report' or  'OK'.\n"                    
                    f"3. NO LISTS: Do not use bullet points, steps, numbered lists or status OK.\n"
                    f"4. PLAIN TEXT ONLY: Write one natural sentence in plain text, with BRIEF CONCLUSIONS."
            )
            # --- 6. ФИНАЛЬНЫЙ ОТЧЕТ ---
            if execution_result.get("success"):
                # 1. Заранее подготавливаем очень жесткий системный промпт
                SYSTEM_PROMPT += (
                    f"5. VARIABLE TAGS: IF message contains tags that begin with the '#' symbol (e.g., #tag) you MUST include ONLY unique tags from message to FINAL result.\n"
                    f"6. NO GHOST TAGS: Never mention a tag in your response (e.g., #tag) that were not specified in the message.\n"
                    f"7 SYNC: If the message contains a specific tag (e.g., #tag), you MUST use that tag, NOT a generic one.\n"
                    
                )
            else:
                SYSTEM_PROMPT += (                    
                    f"5. DO NOT include '#' symbols in your answer. Describe the error briefly.\n"
                )

            # 2. Оптимизированный вызов
            final_word = ollama.chat(
                model=MODEL_NAME,
                messages=[
                    {'role': 'system', 'content': SYSTEM_PROMPT},
                    {'role': 'user', 'content': f"Format this report: {execution_result.get('message')}"}
                ],
                options={
                    "num_predict": 100,      # Жестко ограничиваем количество токенов (не даст Квен "болтать")
                    "temperature": 0.0,     # Снижаем креативность для скорости
                    "top_p": 0.9,
                    #"stop": ["\n", "###"]   # Моментальный стоп при попытке начать новый абзац
                }
            )

            return f"{final_word.message.content}"

            #status_emoji = "✅" if execution_result.get("success") else "❌"
            # Возвращаем склеенный Message из Revit + мысли ИИ
            #return f"{ai_thought}\n\n{status_emoji} **Revit Report:** {execution_result.get('message')}"

            ##########################

            """if execution_result.get("success"):
                log("[+] Успех. Сохраняем в лог сессии.")
                chat_history.append({
                    'role': 'user', 'content': user_text, 
                    'sequence': ai_sequence, 'status': 'success'
                })
                save_history(chat_history)
            else:
                log("[!] Ошибка выполнения. В историю лога не добавляем.")

            # --- 6. ФИНАЛЬНЫЙ ОТЧЕТ ---
            # Достаем данные из анонимного класса, который пришел из C#
            count, tag = extract_summary(execution_result.get("data", {}))
            final_answer = ai_thought.replace("[COUNT]", count).replace("[TAG]", tag)
    
            return f"{final_answer}"""
        
            #return ai_thought

    except Exception as e:
        log(f"[CRITICAL] Logic Crash: {e}")
        return f"Системная ошибка: {str(e)}"

def main():
    client = RevitPipeClient(PIPE_NAME)
    if not client.connect(): return

    # Синхронизация инструментов
    res, _ = client.send_receive({"action": "list"})
    tools = mcp_to_ollama(res.get("tools", []))
    print(f"[*] Инструменты синхронизированы: {len(tools)} шт.")

    # Статус в UI
    client.send_receive({
        "action": "ui_log",
        "type": "system_status",
        "model": MODEL_NAME,
        "status": "online"
    })

    print("[*] Ожидание команд...")
    
    while True:
        # Пинг для получения событий из WebView2
        _, ui_event = client.send_receive({"action": "ping"})

        if ui_event:
            action = ui_event.get("action")
            if action == "chat_request":
                prompt = ui_event.get("prompt")
                print(f"\n[USER]: {prompt}")
                
                # Получаем осмысленный ответ от ИИ
                ai_text = process_ai_logic(prompt, client)
                
                # Отправляем в UI Revit
                client.send_receive({
                    "action": "ui_log", 
                    "role": "ai", 
                    "content": ai_text
                })

            elif action == "stop":
                print("\n[!] EMERGENCY STOP")
        
        time.sleep(0.4)

if __name__ == "__main__":
    try:
        main()
    except Exception as fatal:
        log(f"ФАТАЛЬНАЯ ОШИБКА: {fatal}")
        import traceback
        log(traceback.format_exc())
        input("Окно не закроется. Проверь лог и нажми Enter...")