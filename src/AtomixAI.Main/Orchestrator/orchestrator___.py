import os
import sys
import json
import time

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

# Добавляем путь для импорта инструкций
script_dir = os.path.dirname(os.path.abspath(__file__))
if script_dir not in sys.path:
    sys.path.append(script_dir)

try:
    import instructions
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
    """
    Конвертирует список инструментов из Revit Bridge в формат Google AI SDK.
    """
    function_declarations = []
    
    if not mcp_tools or not isinstance(mcp_tools, list):
        return [{"function_declarations": []}]

    for t in mcp_tools:
        # Извлекаем схему или создаем пустую, если её нет
        schema = t.get("inputSchema", {"type": "object", "properties": {}})
        props = schema.get("properties", {})
        
        # Исправляем специфику массивов для Gemini
        for name, p_info in props.items():
            if p_info.get("type") == "array":
                if "items" not in p_info:
                    p_info["items"] = {"type": "string"} # Дефолт для массивов
                elif isinstance(p_info["items"], dict) and p_info["items"].get("type") == "object":
                     if "properties" not in p_info["items"]:
                         p_info["items"]["properties"] = {}
        
        function_declarations.append({
            "name": t["name"],
            "description": t.get("description", ""),
            "parameters": schema
        })
        
    return [{"function_declarations": function_declarations}]

def clean_args(obj):
    if hasattr(obj, "items"):
        return {k: clean_args(v) for k, v in obj.items()}
    elif hasattr(obj, "__iter__") and not isinstance(obj, str):
        return [clean_args(i) for i in obj]
    return obj

# --- ГЛОБАЛЬНЫЕ ДАННЫЕ (Загружаются 1 раз) ---
GEMINI_API_KEY = "AIzaSyAewRvnUD-uCuzmgs0DotiOPjj6Y5EWN3A" 
genai.configure(api_key=GEMINI_API_KEY)

SYSTEM_CORE = ""
REVIT_MANUAL = ""
TOOLS_CACHE = []
SESSION = None
DETECTED_LANG = "Russian"
MODEL_NAME = 'models/gemini-2.5-flash-lite'

from google.generativeai.types import content_types

# --- ПРАВИЛЬНАЯ ЛОГИКА ИНИЦИАЛИЗАЦИИ ---
def initialize_session(client):
    """
    Initial AI setup: manual upload, model creation with system instruction
    and forced tool configuration (Force Function Calling).
    """
    global SESSION, DETECTED_LANG
    
    log("[*] Initializing AtomixAI session...")
    
    try:
        # 1. DATA UPLOAD FROM REVIT (Once at startup)
        # Get a list of functions and 10 pages of text manual
        res_list, _ = client.send_receive({"action": "list"})
        res_man, _ = client.send_receive({"action": "get_manual"})
        
        if not res_list or not res_man:
            log("[!] Failed to get data from Revit. Check Bridge.")
            return None

        # Convert tools to Gemini format
        tools = mcp_to_gemini(res_list.get("tools", []))
        manual_text = res_man.get("manual", "") if isinstance(res_man, dict) else str(res_man)

        # 2. FORMATION OF "PERSONALITY" AND ROLE (System Instruction)
        # Here we strictly prohibit writing code and force the use of tools.
        full_identity = (
            f"{instructions.PROFILES['default']}\n\n"
            f"--- REVIT KNOWLEDGE BASE (MANUAL) ---\n{manual_text}\n\n"
            f"--- STRICT OPERATIONAL RULES ---\n"
            f"1. **INTENT FILTER**: If the user asks for information (find, count, show), use ONLY 'Search' tools. NEVER call 'Construction' tools (like create_wall) unless explicitly asked to build.\n"
            f"2. **LANGUAGE**: All final answers and summaries MUST be in {DETECTED_LANG}.\n"
            f"3. **FORMAT**: Follow the JSON structure defined in the MANUAL above. If tool calls are generated, provide ONLY the JSON object without markdown or conversational filler."
        )

        # 3. CREATING A MODEL WITH HARD SETTINGS
        # We DO NOT include standard Google tools (code_execution), only yours.
        model = genai.GenerativeModel(
            model_name=MODEL_NAME,
            system_instruction=full_identity,
            generation_config={
                "temperature": 0.0,  # ZERO fantasies, complete determinism
                "top_p": 1,
                "max_output_tokens": 1024,
            }
        )

        # 4. CREATING A CHAT OBJECT (SESSION)
        # Now SESSION will store the dialogue history and remember the manual.
        SESSION = model.start_chat(history=[])
        
        log(f"[+] Session is ready. Model: {MODEL_NAME}. Language: {DETECTED_LANG}")
        return tools

    except Exception as e:
        log(f"[!] Initialization error: {e}")
        import traceback
        log(traceback.format_exc())
        return None

log("[*] List of available models:")
for m in genai.list_models():
    if 'generateContent' in m.supported_generation_methods:
        log(f"  - {m.name}")

def process_ai_logic(user_text, tools, client):
    """
    Основная логика взаимодействия с Gemini: 
    Получает контекст -> Делает запрос -> Выполняет инструменты -> Возвращает ответ.
    """
    try:
        # 1. Сбор динамического контекста (теги #tag выделенных элементов)
        res_state, _ = client.send_receive({"action": "get_context_state"})
        # Безопасно извлекаем текст состояния (из словаря или напрямую)
        state_text = res_state.get("aliases", "") if isinstance(res_state, dict) else str(res_state)

        # 2. Формируем "дельту" (только то, что изменилось: теги и запрос)
        prompt = f"REVIT CONTEXT (TAGS): {state_text}\nUSER REQUEST: {user_text}"
        
        # Первый запрос к модели (SESSION уже содержит системный мануал и историю)
        response = SESSION.send_message(prompt, tools=tools)

        parts = response.candidates[0].content.parts
        commands = [{"action": p.function_call.name, "params": clean_args(p.function_call.args)}
                        for p in parts if p.function_call]

        if not commands:
            return response.text  # Just a text answer

        # BATCH EXECUTION (Saves 429 quota)
        results = []
        for cmd in commands:
            log(f"[*] Execution: {cmd['action']}")
            payload = {"action": "call", "name": cmd['action'], "arguments": json.dumps(cmd['params'])}
            revit_res, _ = client.send_receive(payload)
            results.append(f"{cmd['action']}: {revit_res.get('message', 'Ok')}")

        # Final AI report (consumes only 1 quota request instead of N)
        report_prompt = f"Execution results: {'; '.join(results)}. Answer the user briefly."
        final_report = SESSION.send_message(report_prompt)
        return final_report.text

        # 4. ФИНАЛЬНЫЙ ВЫВОД (Безопасное извлечение текста)
        if response.candidates and response.candidates[0].content.parts:
            for part in response.candidates[0].content.parts:
                if hasattr(part, 'text') and part.text:
                    return part.text.strip()
        
        return "Действие в Revit завершено успешно."

    except Exception as e:
        log(f"[!] КРИТИЧЕСКАЯ ОШИБКА В LOGIC: {e}")
        # Печатаем полный трейсбэк в консоль/лог для отладки
        import traceback
        log(traceback.format_exc())
        return f"Произошла ошибка логики: {e}"

def main():
    client = RevitPipeClient(PIPE_NAME)
    if not client.connect(): return

    # 1. Загружаем инструкции, мануал и создаем ГЛОБАЛЬНЫЙ SESSION
    tools = initialize_session(client) 

    client.send_receive({
        "action": "ui_log", 
        "type": "system_status", 
        "model": MODEL_NAME, 
        "status": "online"
    })
    
    log("[+] Готов к работе.")

    while True:
        _, ui_event = client.send_receive({"action": "ping"})
        if ui_event and ui_event.get("action") == "chat_request":
            # Передаем только 3 аргумента
            ans = process_ai_logic(ui_event.get("prompt"), tools, client)
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
