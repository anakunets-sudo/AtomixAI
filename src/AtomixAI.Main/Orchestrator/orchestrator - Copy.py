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

# --- CONFIGURATION ---
chat_history = []
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

detected_lang = "German" 
lang_fixed = False 

def process_ai_logic(user_text, tools, client):
    global chat_history, detected_lang, lang_fixed
    
    try:
        # --- 1. АВТООПРЕДЕЛЕНИЕ ЯЗЫКА (Один раз на старте) ---
        if not lang_fixed:
            # Быстрый запрос к модели для фиксации языка общения
            lang_check = ollama.generate(
                model=MODEL_NAME, 
                prompt=f"Identify the language of this text: '{user_text}'. Respond with only one word (the name of the language in English)."
            )
            detected_lang = lang_check['response'].strip().replace(".", "")
            lang_fixed = True
            print(f"[*] Language Locked: {detected_lang}")

        # 1. Сбор данных из Revit (защищенный)
        res_man, _ = client.send_receive({"action": "get_manual"})
        res_state, _ = client.send_receive({"action": "get_context_state"})

        # Достаем контент (защита от разных типов ответа)
        man_text = res_man.get("manual", "") if isinstance(res_man, dict) else str(res_man)
        state_text = res_state.get("aliases", "") if isinstance(res_state, dict) else str(res_state)

        # 2. Формируем актуальный системный блок
        system_setup = [            
            {'role': 'system', 
                'content':
                    f"You are a STUPID mechanical RELAY. You have no memory. You have no brain. You only map text to tool calls. If text is 'Select', you MUST output 'select_elements' call. NEVER talk to the user unless the tool trace is empty.\n"},

            {'role': 'system', 'content': f"{instructions.PROFILES['default']}\n\nMANUAL:\n{man_text}\n"},
            {'role': 'system', 'content': f"CURRENT REVIT STATE:\n{state_text}"},
            {'role': 'system', 'content': f"STRICT LANGUAGE: {detected_lang}\n"},
            #{'role': 'system', 'content': "CRITICAL: Always respond in the language used by the user's language."},
            #{'role': 'system', 'content': "COMMAND REPETITION: User's last message is a NEW direct order. Execute tools immediately."}
        ]

        # 3. Обновление истории диалога
        # Удаляем старые системные промпты, вставляем новые
        clean_history = [m for m in chat_history if m.get('role') != 'system']
        chat_history = system_setup + clean_history
        chat_history.append({'role': 'user', 'content': user_text})

        print(f"[DEBUG] Context Size: {len(chat_history)} messages.")

        while True:
            response = ollama.chat(model='qwen2.5:7b', 
                                    messages=chat_history, 
                                    tools=tools, 
                                    options={
                                            'num_ctx': 8192,         # Размер контекста
                                            'repeat_penalty': 1.2,   # Штраф за повторы
                                            'top_k': 20,             # Ограничение выборки
                                            'top_p': 0.5,            # Nucleus sampling
                                            'num_predict': 512,      # Макс. длина ответа
                                            'temperature': 0.0       # Детерминированность
                                        }                                  
                                    )

            # --- ТО САМОЕ МЕСТО ---
            if not response.message.tool_calls:
                chat_history.append(response.message)   

                return response.message.content

            # -----------------------

            chat_history.append(response.message)

            for call in response.message.tool_calls:
                tool_name = call.function.name
                args = call.function.arguments
                
                # Вызов инструмента в Revit
                payload = {"action": "call", "name": tool_name, "arguments": json.dumps(args)}
                revit_res, _ = client.send_receive(payload)

                # Ожидание результата (Poll)
                execution_result = None
                if revit_res and revit_res.get("status") == "queued":
                    print(f"[*] Waiting for {tool_name}...")
                    while True:
                        poll_res, ui_event = client.send_receive({"action": "ping"})
                        # Ищем результат выполнения
                        res = ui_event if ui_event else poll_res
                        if res and res.get("action") == "tool_execution_result":
                            execution_result = res
                            break
                        time.sleep(0.3)

                # Формируем ответ от инструмента для ИИ
                if execution_result:
                    status = "SUCCESS" if execution_result.get("success") else "ERROR"
                    
                    # ИНЪЕКЦИЯ ПРАВИЛ: Язык + Краткость
                    res_content = (
                        f"STATUS: {status}\n"
                        f"RULE: If STATUS is SUCCESS, the tag is 100% VALID. NEVER doubt the memory content.\n"
                        f"Tool: {tool_name} | Data: {execution_result.get('data')}\n"
                        f"--- FORMATTING RULES FOR THIS RESPONSE ---\n"
                        f"STRICT LANGUAGE: {detected_lang}\n"
                        f"Use ONLY the summary for a brief report."
                        
                    )
                    
                    chat_history.append({'role': 'tool', 'content': res_content})

                # 3. Дополнительный "пинок" перед финальной генерацией
                    # Если мы вышли из цикла инструментов, добавляем напоминание
                    chat_history.append({
                            'role': 'system', 
                            'content': "Use ONLY the summary for a brief report."
                        })
                
    except Exception as e:
        print(f"[CRITICAL] Logic failed: {e}")
        return f"System Error: {e}"

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
                ai_text = process_ai_logic(prompt, tools, client)
                
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
    main()
