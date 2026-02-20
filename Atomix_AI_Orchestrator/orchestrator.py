import os
import sys

# Добавляем папку со скриптом в пути поиска модулей
script_dir = os.path.dirname(os.path.abspath(__file__))
if script_dir not in sys.path:
    sys.path.append(script_dir)

import json
import ollama
import win32file
import win32pipe
import pywintypes
import time
import sys
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
            # Добавляем \n при отправке, чтобы C# считал это одной строкой
            win32file.WriteFile(self.handle, (json.dumps(payload) + "\n").encode('utf-8'))
        
            # Читаем ответ
            _, data = win32file.ReadFile(self.handle, 65536)
        
            # ОЧИЩАЕМ строку от возможных лишних байтов в конце (особенно важно для отладки)
            decoded_data = data.decode('utf-8').strip()
            if not decoded_data:
                return None, None
            
            raw_res = json.loads(decoded_data)
        
            if isinstance(raw_res, dict) and "ui_event" in raw_res:
                return raw_res["result"], raw_res["ui_event"]
        
            return raw_res, None
        except Exception as e:
            print(f"[!] Error in Pipe: {e}")
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
    """Логика взаимодействия с Ollama и вызова инструментов"""
    print(f"[Thinking] Анализ запроса: {user_text}")
    
    response = ollama.chat(
        model=MODEL_NAME,
        messages=[
            {'role': 'system', 'content': instructions.PROFILES["default"]}, 
            {'role': 'user', 'content': user_text}
        ],
        tools=tools
    )

    if response.message.tool_calls:
        for call in response.message.tool_calls:
            print(f"[Action] Вызов {call.function.name}...")
            args = call.function.arguments if call.function.arguments else {}
            
            payload = {
                "action": "call",
                "name": call.function.name,
                "arguments": json.dumps(args)
            }
            # Отправляем в Revit и игнорируем вложенные события во время выполнения
            res, _ = client.send_receive(payload)
            print(f"[Revit Result]: {res}")
            return f"Выполнено: {call.function.name}"
    else:
        return response.message.content

def main():
    client = RevitPipeClient(PIPE_NAME)
    if not client.connect(): return

    # 1. Sync Tools
    res, _ = client.send_receive({"action": "list"})
    tools = mcp_to_ollama(res.get("tools", []))
    print(f"[*] Инструменты синхронизированы ({len(tools)} шт.)")

    print("[*] Ожидание команд из интерфейса Revit...")
    
    while True:
        # 2. Пинг-опрос (Heartbeat). Мы шлем пустой запрос, чтобы забрать события из UI
        # В McpHost.ProcessRequest мы добавили обработку неизвестных экшенов -> статус "ok"
        res, ui_event = client.send_receive({"action": "ping"})

        if ui_event:
            action = ui_event.get("action")
            
            if action == "chat_request":
                prompt = ui_event.get("prompt")
                print(f"\n[UI User]: {prompt}")
                
                # Запускаем мозг
                ai_text = process_ai_logic(prompt, tools, client)
                
                # Отправляем ответ обратно, чтобы C# переслал его в WebView2
                client.send_receive({
                    "action": "ui_log", 
                    "role": "ai", 
                    "content": ai_text
                })

            elif action == "stop":
                print("\n[!] Получен сигнал EMERGENCY STOP. Прерывание текущих задач.")
                # Здесь можно добавить логику сброса контекста Ollama
        
        time.sleep(0.5) # Пауза между опросами, чтобы не грузить CPU

if __name__ == "__main__":
    main()
