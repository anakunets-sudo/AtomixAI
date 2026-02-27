import os
import sys
import json
import time

# Добавляем путь для импорта инструкций
script_dir = os.path.dirname(os.path.abspath(__file__))
if script_dir not in sys.path:
    sys.path.append(script_dir)

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

try:
    import win32file
    import win32pipe
    import pywintypes
    import google.generativeai as genai
    log("[+] Библиотеки загружены.")
except ImportError as e:
    log(f"[!] ОШИБКА ИМПОРТА: {e}. Выполни: pip install google-generativeai pywin32")
    input("Нажми ENTER для выхода...")
    sys.exit(1)

# --- CONFIG ---
detected_lang = "German"  # Default
lang_fixed = False

PIPE_NAME = r'\\.\pipe\AtomixAI_Bridge_Pipe'
GEMINI_API_KEY = "AIzaSyAl4U-4L08cJsV4DakVSS4gE10KEwoQCag" 

genai.configure(api_key=GEMINI_API_KEY)

log("[*] List of available models:")
for m in genai.list_models():
    if 'generateContent' in m.supported_generation_methods:
        log(f"  - {m.name}")

model_instance = genai.GenerativeModel('models/gemini-2.5-flash-lite')

class RevitPipeClient:
    def __init__(self, name):
        self.name = name
        self.handle = None

    def connect(self):
        log(f"[*] Поиск канала {self.name}...")
        while True:
            try:
                self.handle = win32file.CreateFile(
                    self.name, win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                    0, None, win32file.OPEN_EXISTING, 0, None)
                log("[+] Соединение с Revit установлено.")
                return True
            except Exception:
                time.sleep(1)

    def send_receive(self, payload):
        try:
            win32file.WriteFile(self.handle, (json.dumps(payload) + "\n").encode('utf-8'))
            _, data = win32file.ReadFile(self.handle, 65536)
            decoded = data.decode('utf-8').strip()
            if not decoded: return None, None
            res = json.loads(decoded)
            return (res.get("result"), res.get("ui_event")) if isinstance(res, dict) and "ui_event" in res else (res, None)
        except Exception as e:
            log(f"[!] Ошибка Pipe: {e}")
            return None, None

def mcp_to_gemini(mcp_tools):
    function_declarations = []
    for t in mcp_tools:
        schema = t.get("inputSchema", {"type": "object", "properties": {}})
        props = schema.get("properties", {})
        for name, p_info in props.items():
            if p_info.get("type") == "array":
                # If items is missing or empty
                if "items" not in p_info:
                    p_info["items"] = {"type": "string"}
                # If the object inside the array is also an array (recursion) - this is items.items
                elif isinstance(p_info["items"], dict) and p_info["items"].get("type") == "object":
                     if "properties" not in p_info["items"]:
                         p_info["items"]["properties"] = {}
        
        function_declarations.append({
            "name": t["name"],
            "description": t.get("description", ""),
            "parameters": schema
        })
    return [{"function_declarations": function_declarations}]

def process_ai_logic(user_text, tools, client, session):
    log(f"[*] Обработка: {user_text}")
    
    # Вспомогательная функция для очистки Protobuf-объектов (RepeatedComposite) в чистый Python dict/list
    def clean_args(obj):
        if hasattr(obj, "items"):  # Если это MapComposite или dict
            return {k: clean_args(v) for k, v in obj.items()}
        elif hasattr(obj, "__iter__") and not isinstance(obj, str):  # Если это RepeatedComposite или list
            return [clean_args(i) for i in obj]
        return obj

    try:
        # 1. Сбор контекста (какие #теги сейчас в памяти Revit)
        res_state, _ = client.send_receive({"action": "get_context_state"})
        
        # 2. Первый запрос к модели

        full_prompt = (
            f"SYSTEM: Ты BIM-координатор AtomixAI. Только выполняй действия, которые пользователь явно запросил.\n"
            f"CONTEXT: {res_state}\n"
            f"USER: {user_text}"
        )

        response = session.send_message(full_prompt, tools=tools)

        # Цикл Tool Use (модель может вызывать инструменты цепочкой)
        while True:
            if not response.candidates or not response.candidates[0].content.parts:
                break
                
            tool_calls = [p.function_call for p in response.candidates[0].content.parts if p.function_call]
            if not tool_calls:
                break

            for fn in tool_calls:
                log(f"[*] Вызов инструмента: {fn.name}")
                
                # КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: Очищаем аргументы перед сериализацией в JSON
                safe_args = clean_args(fn.args)
                
                # Формируем пакет для Revit Bridge
                payload = {
                    "action": "call", 
                    "name": fn.name, 
                    "arguments": json.dumps(safe_args) 
                }
                
                # Отправляем в Revit через Pipe
                revit_res, _ = client.send_receive(payload)

                exec_data = "No result"
                # Если команда ушла в очередь (Async External Event в Revit)
                if revit_res and revit_res.get("status") == "queued":
                    log(f"[*] Ожидание выполнения {fn.name}...")
                    
                    # Пинг-понг цикл ожидания результата (до 15 секунд)
                    for _ in range(50): 
                        p_res, ui_ev = client.send_receive({"action": "ping"})
                        # Ищем результат в обычном ответе или в ui_event (куда его шлет ToolDispatcher)
                        res = ui_ev if ui_ev else p_res
                        
                        if isinstance(res, dict) and res.get("action") == "tool_execution_result":
                            if res.get("tool") == fn.name:
                                exec_data = res.get("message", "Success")
                                log(f"[+] Revit ответил: {exec_data}")
                                break
                        time.sleep(0.3)

                # Возвращаем результат выполнения инструменту в сессию Gemini
                response = session.send_message(
                    genai.protos.Content(parts=[
                        genai.protos.Part(
                            function_response=genai.protos.FunctionResponse(
                                name=fn.name, 
                                response={'result': str(exec_data)}
                            )
                        )
                    ])
                )
        
        return response.text

    except Exception as e:
        log(f"[!] Ошибка в process_ai_logic: {e}")
        return f"Произошла ошибка: {e}"

def main():
    client = RevitPipeClient(PIPE_NAME)
    if not client.connect(): return

    log("[*] Синхронизация...")
    res, _ = client.send_receive({"action": "list"})
    tools = mcp_to_gemini(res.get("tools", []))
    session = model_instance.start_chat(history=[])

    client.send_receive({"action": "ui_log", "type": "system_status", "model": "Gemini 1.5 Flash", "status": "online"})
    log("[+] Готов к работе.")

    while True:
        _, ui_event = client.send_receive({"action": "ping"})
        if ui_event and ui_event.get("action") == "chat_request":
            ans = process_ai_logic(ui_event.get("prompt"), tools, client, session)
            client.send_receive({"action": "ui_log", "role": "ai", "content": ans})
        time.sleep(0.4)

if __name__ == "__main__":
    try:
        main()
    except Exception as fatal:
        log(f"ФАТАЛЬНАЯ ОШИБКА: {fatal}")
        import traceback
        log(traceback.format_exc())
        input("Окно не закроется. Проверь лог и нажми Enter...")
