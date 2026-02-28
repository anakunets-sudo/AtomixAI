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
    f.write(f"--- НОВАЯ СЕССИЯ: {time.ctime()} ---\n")
def log(msg):
    # Для последующих записей используем "a", чтобы лог рос в течение сессии
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(f"{time.strftime('%H:%M:%S')} | {msg}\n")
    print(msg)
log("[*] Проверка зависимостей...")

# --- CONFIGURATION ---
PIPE_NAME = r'\\.\pipe\AtomixAI_Bridge_Pipe'
MODEL_NAME = 'qwen2.5:7b' # deepseek-r1:8b  qwen2.5:7b

class RevitPipeClient:
    def __init__(self, name):
        self.name = name
        self.handle = None

    def connect(self):
        print(f"[*] Подключение к Revit ({self.name})...")
        while True:
            try:
                self.handle = win32file.CreateFile(
                    self.name, win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                    0, None, win32file.OPEN_EXISTING, 0, None)
                print("[+] Связь с Revit установлена.")
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
        log(f"[!] Ошибка сохранения истории: {e}")

def load_history():
    if os.path.exists(HISTORY_FILE):
        try:
            with open(HISTORY_FILE, 'r', encoding='utf-8') as f:
                data = json.load(f)
                log(f"[+] История загружена: {len(data)} сообщений.")
                return data
        except:
            return []
    return []

# Инициализация при старте
chat_history = load_history()

def process_ai_logic(user_text, client):
    """
    Основная логика: планирование цепочки (LLM) -> выполнение батча (Revit) -> отчет.
    """
    global chat_history, detected_lang
    log(">>> [LOGIC] Start processing...") # 1.
    try:
        log(">>> [LOGIC] Requesting Manual from Revit...")
        # --- 1. СИНХРОНИЗАЦИЯ КОНТЕКСТА ---
        # Получаем свежий мануал инструментов и состояние памяти (#теги) прямо из Revit
        res_manual, _ = client.send_receive({"action": "get_manual"})
        res_context, _ = client.send_receive({"action": "get_context_state"})
        log(f">>> [LOGIC] Manual received (len: {len(str(res_manual))})")
        log(">>> [LOGIC] Context received.")

        log(">>> [LOGIC] Requesting Context from Revit...")
        dynamic_manual = res_manual.get("manual", "No tools available.")
        current_tags = res_context.get("aliases", "No active aliases in memory.")

        # --- 2. УПРАВЛЕНИЕ ИСТОРИЕЙ (Smart History) ---
        # Оставляем последние 40 сообщений, чтобы не переполнить контекст модели
        if len(chat_history) > 40:
            chat_history = chat_history[-40:]

        # Очищаем историю от старых системных промптов и технических отчетов 'tool'
        # Оставляем только диалог User <-> Assistant для логики "Повтори"
        clean_history = [m for m in chat_history if m.get('role') in ['user', 'assistant']]

        # Формируем актуальный системный промпт (инъекция свежих данных)
        system_content = (
            f"{instructions.PROFILES['default']}\n\n"
            f"### BIM COMMANDS REFERENCE:\n{dynamic_manual}\n\n"
            f"### CURRENT MEMORY STATE (TAGS):\n{current_tags}\n\n"
            f"STRICT LANGUAGE: {detected_lang}\n"
            f"GOAL: Plan a 'sequence' of commands.\n"
            f"MEMORY RULE: Use '#tag' for Out and In parameters to link chains.\n"
        )
        
        # Сборка финального пакета для отправки в ИИ
        current_session = [{'role': 'system', 'content': system_content}] + clean_history
        current_session.append({'role': 'user', 'content': user_text})

        log(f">>> [LOGIC] Calling Ollama model: {MODEL_NAME}")
        # --- 3. ЗАПРОС К LLM (Ollama/Gemini) ---
        # Используем format='json' для Qwen/DeepSeek для гарантированной структуры
        response = ollama.chat(
            model=MODEL_NAME,
            messages=current_session,
            format='json',
            options={
                'num_ctx': 8192,         # Размер контекста
                'repeat_penalty': 1.2,   # Штраф за повторы
                'top_k': 20,             # Ограничение выборки
                'top_p': 0.5,            # Nucleus sampling
                'num_predict': 512,      # Макс. длина ответа
                'temperature': 0.0       # Детерминированность
            }
        )
        log(">>> [LOGIC] Ollama response received!")

        ai_raw = response.message.content
        data = json.loads(ai_raw)
        
        # Извлекаем компоненты ответа
        ai_thought = data.get("thought", "Planning completion...")
        ai_sequence = data.get("sequence", []) # Это база для функции "Повтори"

        log(f">>> [LOGIC ai_thought] {ai_thought}")
        log(f">>> [LOGIC ai_sequence] {ai_sequence}")

        # --- 4. ВЫПОЛНЕНИЕ ЦЕПОЧКИ (Batch Execution) ---
        if ai_sequence and len(ai_sequence) > 0:
            log(f"[*] Sending batch to Revit: {len(ai_sequence)} steps...")
            
            # Отправляем весь массив команд одним экшеном
            payload = {"action": "call_batch", "sequence": ai_sequence}
            revit_res, _ = client.send_receive(payload)
            
            # Ожидание результата через механизм Ping (Polling)
            execution_result = {"success": False, "message": "Revit timeout"}
            if revit_res and "batch_queued" in revit_res.get("status", ""):
                log("[*] Sequence in progress... Waiting for Revit signal.")
                while True:
                    poll_res, ui_event = client.send_receive({"action": "ping"})
                    
                    # Ищем маркер завершения батча в ui_event или основном ответе
                    res = ui_event if ui_event else poll_res
                    if res and res.get("action") == "tool_execution_result":
                        execution_result = res
                        break
                    
                    time.sleep(0.3) # Пауза между опросами
            
            # --- 5. ФИКСАЦИЯ В ИСТОРИИ (Для ГИПа) ---
            chat_history.append({'role': 'user', 'content': user_text})
            
            # Записываем ответ ИИ с сохранением "сырых" команд для будущего "Повтори"
            chat_history.append({
                'role': 'assistant', 
                'content': ai_thought,
                'raw_sequence': ai_sequence, # Скрытые данные для Суперкоманды
                'execution_status': 'success' if execution_result.get('success') else 'failed'
            })
            
            # Сохраняем на диск (atomix_history.json)
            save_history(chat_history)
            
            # Финальный отчет пользователю
            status_emoji = "✅" if execution_result.get("success") else "❌"
            return f"{ai_thought}\n\n{status_emoji} **Revit Report:** {execution_result.get('message')}"
        
        else:
            # Если команд нет, просто текстовый ответ
            chat_history.append({'role': 'user', 'content': user_text})
            chat_history.append({'role': 'assistant', 'content': ai_thought})
            save_history(chat_history)
            return ai_thought

    except json.JSONDecodeError as je:
        log(f"[!] AI JSON Error: {je}")
        return "The AI generated an invalid sequence format. Please try rephrasing."
    except Exception as e:
        log(f"[CRITICAL] Logic failed: {e}")
        return f"System Error: {str(e)}"

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