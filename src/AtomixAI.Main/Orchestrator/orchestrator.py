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

def process_ai_logic(user_text, tools, client):
    # Запрос динамического мануала
    res, _ = client.send_receive({"action": "get_manual"})
    dynamic_manual = res.get("manual", "No specific instructions.")

    messages = [
        {'role': 'system', 'content': dynamic_manual}, # Наш Reflection-мануал
        {'role': 'system', 'content': instructions.PROFILES["default"]},
        {'role': 'user', 'content': user_text}
    ]

    # Бесконечный цикл для цепочки вызовов (Chain of Thought)
    while True:
        response = ollama.chat( model=MODEL_NAME, messages=messages, tools=tools )
        
        if not response.message.tool_calls:
            return response.message.content

        messages.append(response.message)

        for call in response.message.tool_calls:
            tool_name = call.function.name
            args = call.function.arguments
            
            print(f"[Action] Requesting Revit: {tool_name}")
            
            # 1. Отправляем запрос на выполнение
            payload = {"action": "call", "name": tool_name, "arguments": json.dumps(args)}
            revit_res, _ = client.send_receive(payload)

            # 2. ВАРИАНТ Б: Ждем реального завершения
            # Если Revit ответил "queued", мы начинаем "пинговать" пайп, 
            # пока не придет подтверждение выполнения.
            execution_result = None
            if revit_res.get("status") == "queued":
                print(f"[*] Waiting for {tool_name} to finish in Revit...")
                while True:
                    # Опрашиваем пайп. Dispatcher должен будет выплюнуть результат в ui_event
                    # или в основной ответ следующего пинга.
                    poll_res, ui_event = client.send_receive({"action": "ping"})
                    
                    # Ищем в ui_event или ответе признак завершения команды
                    if ui_event and ui_event.get("action") == "tool_execution_result":
                        execution_result = ui_event
                        break
                    
                    # Если Dispatcher сразу вернул результат в poll_res (зависит от реализации C#)
                    if poll_res and poll_res.get("action") == "tool_execution_result":
                        execution_result = poll_res
                        break
                        
                    time.sleep(0.2)

            # 3. Формируем контекст для ИИ на основе РЕАЛЬНОГО результата
            status = "SUCCESS" if execution_result.get("success") else "ERROR"
            tool_result_content = (
                f"STATUS: {status}\n"
                f"Tool: {tool_name}\n"
                f"Message: {execution_result.get('message')}\n"
                f"Data: {execution_result.get('data')}\n"
                f"Output_Alias: {args.get('Out', 'none')}\n"
                f"INSTRUCTION: Use ONLY the summary for the final report to avoid repetition."
            )
            
            print(f"[Revit Context]: {status} - {execution_result.get('message')}")

            messages.append({'role': 'tool', 'content': tool_result_content})

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
