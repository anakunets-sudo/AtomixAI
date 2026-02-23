import os
import sys
import json
import ollama
import win32file
import win32pipe
import pywintypes
import time

# Добавляем путь для импорта инструкций
script_dir = os.path.dirname(os.path.abspath(__file__))
if script_dir not in sys.path:
    sys.path.append(script_dir)

import instructions

# --- CONFIGURATION ---
PIPE_NAME = r'\\.\pipe\AtomixAI_Bridge_Pipe'
MODEL_NAME = 'qwen2.5:7b'

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

def process_ai_logic(user_text, tools, client):
    """
    Улучшенная логика: 
    1. ИИ решает вызвать инструмент.
    2. Мы выполняем его в Revit.
    3. Мы возвращаем результат в ИИ.
    4. ИИ формулирует ответ пользователю.
    """
    messages = [
        {'role': 'system', 'content': instructions.PROFILES["default"]},
        {'role': 'user', 'content': user_text}
    ]

    # --- ШАГ 1: Первичный запрос к модели ---
    response = ollama.chat(model=MODEL_NAME, messages=messages, tools=tools)

    # --- ШАГ 2: Обработка вызовов инструментов (если есть) ---
    if response.message.tool_calls:
        # Добавляем ответ модели (с намерением вызвать инструмент) в историю
        messages.append(response.message)

        for call in response.message.tool_calls:
            tool_name = call.function.name
            args = call.function.arguments if call.function.arguments else {}
            
            print(f"[Action] Вызываю Revit: {tool_name}({args})")
            
            payload = {
                "action": "call",
                "name": tool_name,
                "arguments": json.dumps(args)
            }
            
            # Отправляем в Revit
            revit_res, _ = client.send_receive(payload)
            
            # Формируем ответ от 'инструмента' для ИИ
            # Мы берем Success, Message и Data из AtomicResult
            status = "Success" if revit_res.get("success") else "Error"
            msg = revit_res.get("message", "No message")
            data = revit_res.get("data", "")
            
            tool_result_content = (
                f"STATUS: SUCCESS. COMMAND COMPLETED.\n"
                f"Tool: {tool_name}\n"
                f"Revit_Output: {msg}\n"
                f"Created_Element_ID: {data}\n"
                f"IMPORTANT: Respond to the user in RUSSIAN language only."
            )
            print(f"[Revit Context]: {tool_result_content}")

            # Добавляем результат выполнения в контекст диалога
            messages.append({
                'role': 'tool',
                'content': tool_result_content,
            })

        # --- ШАГ 3: Финальный проход (ИИ анализирует результаты и отвечает юзеру) ---
        final_response = ollama.chat(model=MODEL_NAME, messages=messages)
        return final_response.message.content
    
    # Если инструментов не потребовалось
    return response.message.content

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
