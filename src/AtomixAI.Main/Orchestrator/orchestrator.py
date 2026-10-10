#orchestrator.py
import argparse
import os
from socket import AI_NUMERICHOST
import sys
import json
from trace import Trace
import win32file
import win32pipe
import pywintypes
import time
import requests

# Добавляем путь для импорта инструкций
script_dir = os.path.dirname(os.path.abspath(__file__))
if script_dir not in sys.path:
    sys.path.append(script_dir)

import instructions

# 1. ПРИНУДИТЕЛЬНОЕ СОЗДАНИЕ (ИЛИ ОЧИСТКА) ЛОГА
LOG_FILE = "atomix_debug.log"
with open(LOG_FILE, "w", encoding="utf-8") as f:
    f.write(f"--- NEW SESSION: {time.ctime()} ---\n")

def log(msg):
    # Добавляем параметр encoding="utf-8", чтобы смайлики не ломали лог
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(f"{time.strftime('%H:%M:%S')} | {msg}\n")
    
    # Чтобы print в консоли Windows тоже не падал на смайликах:
    try:
        print(msg)
    except UnicodeEncodeError:
        print(msg.encode('utf-8', errors='ignore').decode('utf-8'))

log(f"REVIT PYTHON VERSION: {sys.version}")
log("[*] Checking dependencies...")

# --- NEW CONFIGURATION ---
PIPE_NAME = r'\\.\pipe\AtomixAI_Bridge_Pipe'

# --- PROVIDERS ---------------------------------------------------------------
# Все провайдеры — OpenAI-совместимые chat/completions эндпоинты.
# Добавить нового: одна запись здесь, либо переопределить без правки кода через
# %APPDATA%\AtomixAI\providers.json (ключ берётся из окружения/реестра, не из репо).
DEFAULT_PROVIDERS = {
    "openrouter": {
        "base_url": "https://openrouter.ai/api/v1/chat/completions",
        "api_key_env": "OPENROUTER_API_KEY",
        # опробованные модели: 'openai/gpt-oss-safeguard-20b', 'openai/gpt-5-nano',
        # 'google/gemini-2.5-flash-lite', 'deepseek/deepseek-v4-flash-0731'
        "model": "google/gemini-2.5-flash-lite",
    },
    "teamorouter": {
        "base_url": "https://api.teamorouter.com/v1/chat/completions",
        "api_key_env": "TEAMOROUTER_API_KEY",
        "model": "gemini-3.5-flash-lite",
    },
}

USER_CFG = os.path.join(
    os.environ.get("APPDATA", os.path.expanduser("~")), "AtomixAI", "providers.json"
)


def load_providers():
    """Дефолты + опциональные переопределения из %APPDATA% (вне репозитория)."""
    cfg = {name: dict(p) for name, p in DEFAULT_PROVIDERS.items()}
    active = "openrouter"
    try:
        if os.path.isfile(USER_CFG):
            with open(USER_CFG, "r", encoding="utf-8") as f:
                data = json.load(f)
            for name, p in (data.get("providers") or {}).items():
                cfg.setdefault(name, {}).update(p)
            active = data.get("active") or active
    except Exception as ex:
        log(f"[!] providers.json: {ex}")
    return active, cfg


def _registry_env(name):
    """Свежие значения из реестра User/Machine (без перезапуска Revit)."""
    try:
        import winreg
    except ImportError:
        return ""
    for root in (winreg.HKEY_CURRENT_USER, winreg.HKEY_LOCAL_MACHINE):
        try:
            with winreg.OpenKey(root, "Environment") as k:
                value, _ = winreg.QueryValueEx(k, name)
                if isinstance(value, str) and value.strip():
                    return value.strip()
        except OSError:
            continue
    return ""


# legacy: `python orchestrator.py --api-key ...` продолжает работать
_parser = argparse.ArgumentParser(add_help=False)
_parser.add_argument("--api-key", dest="api_key", default=None)
_cli_args, _ = _parser.parse_known_args()
CLI_API_KEY = (_cli_args.api_key or "").strip()


def resolve_provider_key(provider):
    """--api-key (legacy) -> env -> реестр User/Machine -> api_key в providers.json."""
    if CLI_API_KEY:
        return CLI_API_KEY
    env_name = provider.get("api_key_env", "")
    for src in (os.environ.get(env_name, ""), _registry_env(env_name), provider.get("api_key", "")):
        if isinstance(src, str) and src.strip():
            return src.strip()
    return ""


ACTIVE_NAME, PROVIDERS = load_providers()
MODEL_NAME = PROVIDERS[ACTIVE_NAME]["model"]
FULL_URL = PROVIDERS[ACTIVE_NAME]["base_url"]
API_KEY = resolve_provider_key(PROVIDERS[ACTIVE_NAME])


def use_provider(name):
    """Глобальный переключатель провайдера — вызывать из любого места кода."""
    global ACTIVE_NAME, MODEL_NAME, FULL_URL, API_KEY
    if name not in PROVIDERS:
        log(f"[!] Unknown provider '{name}'. Available: {', '.join(PROVIDERS)}")
        return False
    provider = PROVIDERS[name]
    ACTIVE_NAME = name
    MODEL_NAME = provider["model"]
    FULL_URL = provider["base_url"]
    API_KEY = resolve_provider_key(provider)
    if not API_KEY:
        log(f"[!] {provider.get('api_key_env')} is empty — setx it or add 'api_key' to providers.json")
    log(f"[*] Provider -> {name} ({MODEL_NAME})")
    return True


if not API_KEY:
    log(f"[!] {PROVIDERS[ACTIVE_NAME].get('api_key_env')} is empty — set it in the environment or in %APPDATA%\\AtomixAI\\providers.json")
PROVIDER = { "only": ["google-vertex", "google-vertex/en", "google-ai-studio"], "allow_fallbacks": True }
PROVIDER_FLEX = {
    # Сначала пробуем сэкономить на AI Studio Flex, при сбое — прыгаем на Vertex
    "order": ["google-ai-studio/flex", "google-ai-studio", "google-vertex"], 
    "allow_fallbacks": True 
}

# '{"only": ["groq"],"allow_fallbacks": false}'
# 'deepseek/deepseek-v4-flash-0731'
#{ "only": [ "openai/flex" ],  "allow_fallbacks": False }
#{ "only": ["google-ai-studio/flex"], "allow_fallbacks": False }
#{ "only": ["relace/fp4"], "allow_fallbacks": False }
# { "only": ["gmicloud/fp8"], "allow_fallbacks": False }

# Описание главной обертки команд для ИИ
bim_sequence_tool = {
    "type": "function",
    "function": {
        "name": "execute_bim_sequence",
        "description": "Executes a pipeline of atomic Revit commands based on user request.",
        "parameters": {
            "type": "object",
            "properties": {
                "thought": {
                    "type": "string",
                    "description": 
                        "Internal reasoning process. CRITICAL CHAIN-OF-THOUGHT MANDATE: Before building the sequence, you MUST explicitly write your step-by-step reasoning here: "
                        "1) State that you have read the <ACTIVE_REVIT_MEMORY_TAGS> block. "
                        "2) Find the highest index number matching the pattern #[category]_[action]_[NUMBER] within <ACTIVE_REVIT_MEMORY_TAGS>. "
                        "3) Explicitly calculate the next increment number (max + 1) and state the exact new tag name you will use for the 'Out' argument. "
                        "Be concise and strict (30-50 words in English). MANDATORY FOR EVERY TURN!"
                },
                #"user_language": {
                #    "type": "string",
                #    "description": "You MUST detect the language of the <USER_REQUEST_PAYLOAD>. Must be a single word (e.g., 'German')."
                #},
                "sequence": {
                    "type": "array",
                    "description": (
                        "Write this BEFORE 'active_context_tag'. Sequential BIM commands for Revit. "
                        "Fill every known Params value from the FULL <USER_REQUEST_PAYLOAD> (ORIGINAL + all REFINEMENT messages). "
                        "If a mandatory value is still unknown after reading the payload, leave sequence EMPTY [] and ask via active_context_tag. "
                        "NEVER put a filled Params value into sequence and then claim that same parameter is missing."
                    ),
                    "items": {
                        "type": "object",
                        "properties": {
                            "name": { 
                                "type": "string", 
                                "description": "The exact tool name." 
                            },
                            "arguments": { 
                                "type": "object", 
                                "description": (
                                    "Dynamic key-value parameters. "
                                )
                            }
                        },
                        "required": ["name", "arguments"]
                    }
                },
                "active_context_tag": {
                    "type": "string",
                    "description": (
                        "Write this AFTER 'sequence'. Focus lock for ONE still-missing mandatory parameter. "
                        "Format: 'ToolName:ParameterName' (e.g. 'search_init:Scope', 'filter_parameters:ParameterName', 'create_wall:Length'). "
                        "CRITICAL SELF-CHECK: Inspect the sequence you just wrote. If ToolName.Params.ParameterName (or arguments.ParameterName) "
                        "already has a non-empty value, you MUST return \"\" — do NOT invent a lock for a filled field. "
                        "Return \"\" when the sequence is ready to execute, or for small talk. "
                        "Only set a non-empty tag when sequence is [] OR that exact parameter is still empty/absent."
                    )
                },
                "user_facing_message": {
                    "type": "string",
                    "description": (
                        "Write this AFTER 'active_context_tag'. Message shown to the engineer in the UI. Keep it scannable (1-2 short sentences). "
                        "If 'active_context_tag' is populated: a warm, direct question asking for the missing parameter. "
                        "If 'sequence' is complete and will execute now: a short preview of THAT sequence — "
                        "name the tools and key arguments in plain language. Use future / about-to-start tense. "
                        "Do NOT greet. Do NOT claim the work is already done. Do NOT dump raw JSON. "
                        "Leave EMPTY ('') only for small talk without a sequence."
                    )
                }
            },
            "required": ["thought", "sequence", "active_context_tag", "user_facing_message"] 
        }
    }
}


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

class RequestAborted(Exception):
    """Пользователь нажал Cancel. Ответ в чат отправлять нельзя."""
    pass

def _is_abort_event(event, generation=0):
    if not isinstance(event, dict):
        return False
    if event.get("action") not in ("abort", "stop"):
        return False
    event_generation = event.get("generation") or 0
    # Чужой abort (предыдущий ход) не должен гасить уже новый запрос.
    if generation and event_generation and event_generation != generation:
        return False
    return True

def raise_if_abort(event, generation=0):
    if _is_abort_event(event, generation):
        log("[!] Abort received. Current request will not be answered.")
        raise RequestAborted()

def exchange(client, payload, generation=0):
    """send_receive, который не выбрасывает abort/stop, пришедшие вместе с ответом."""
    result, ui_event = client.send_receive(payload)
    raise_if_abort(ui_event, generation)
    if isinstance(result, dict) and result.get("status") == "aborted":
        log("[!] Revit rejected the call because the request was cancelled.")
        raise RequestAborted()
    return result, ui_event

HISTORY_FILE = os.path.join(script_dir, "atomix_history.json")

# --- ИНИЦИАЛИЗАЦИЯ И ОЧИСТКА ПРИ СТАРТЕ ОРКЕСТРАТОРА ---
def clear_history():
    """Принудительно затирает файл истории пустым массивом для новой демо-сессии."""
    try:
        with open(HISTORY_FILE, 'w', encoding='utf-8') as f:
            json.dump([], f)
        log("[+] Session history has been cleared for the new session.")
    except Exception as e:
        log(f"[!] Error clearing history: {e}")

# Запускаем очистку старых файлов перед стартом цикла
clear_history()

def save_history(history):
    try:
        # Храним только User и Assistant (без системных промптов)
        to_save = [m for m in history if m.get('role') in ['user', 'assistant']]
        
        # БЕРЕМ ТОЛЬКО ПОСЛЕДНИЕ 5 СООБЩЕНИЙ ДЛЯ ДЕМО
        to_save = to_save[-5:] 
        
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
    count = data_obj.get("total_success") or data_obj.get("count") or "0"
    tags_dict = data_obj.get("tags") or {}
    tag_list = list(tags_dict.keys())
    primary_tag = tag_list if tag_list else "#result"
    return str(count), primary_tag

# Инициализация при старте
chat_history = load_history()
detected_lang = None  # Будет определен при первом запросе

# --- ШАГ 1. РЕГИСТРАЦИЯ СТЕЙТ-МЕНЕДЖЕРА И ЛИМИТА ФЛУДА (State-Tagged Prompt Hydration) ---
MAX_FLOOD_ATTEMPTS = 2  # 1 — раз дает пофлудить или неправильную попытку, на 2-й раз динамический сброс

class ContextStateManager:
    def __init__(self):
        #self.active_tool = None
        self.active_param = None  # Например, "filter_elements:Categories"
        #self.active_param_hint = None   # Человеческая подсказка
        self.flood_count = 0            # Счетчик пустой болтовни
        self.user_steps = []            # ХРОНОЛОГИЯ ШАГОВ ПОЛЬЗОВАТЕЛЯ [origin, refinement_1, ...]

    def set_context(self, param_name):
        #self.active_tool = tool
        self.active_param = param_name
        #self.active_param_hint = param_hint     

    def clear(self):
        #self.active_tool = None
        self.active_param = None
        #self.active_param_hint = None
        self.flood_count = 0 
        self.user_steps = []            # Полная очистка истории шагов при сбросе/успехе



# Инициализация глобального стейта памяти сессии
session_state = ContextStateManager()

dynamic_manual = None

def _sequence_has_context_param(sequence, context_tag):
    """True if active_context_tag like 'filter_parameters:Value' is already filled in sequence."""
    if not sequence or not context_tag or ":" not in str(context_tag):
        return False
    tool_name, param_name = str(context_tag).split(":", 1)
    tool_name, param_name = tool_name.strip(), param_name.strip()
    if not tool_name or not param_name:
        return False
    for step in sequence:
        if not isinstance(step, dict):
            continue
        if str(step.get("name", "")).strip() != tool_name:
            continue
        args = step.get("arguments") or {}
        if not isinstance(args, dict):
            continue
        # Params может лежать в arguments.Params или плоско в arguments
        candidates = []
        params = args.get("Params")
        if isinstance(params, dict):
            candidates.append(params.get(param_name))
        candidates.append(args.get(param_name))
        for val in candidates:
            if val is None:
                continue
            if isinstance(val, str) and not val.strip():
                continue
            if isinstance(val, (list, dict)) and len(val) == 0:
                continue
            return True
    return False

def execute_eviction_protocol(user_text, detected_lang, headers):
    eviction_rules = (
        f"<TRANSACTION_EVICTION_PROTOCOL>\n"
        f"<AGENT_STATE_DIRECTIVES>\n"
        f"### ROLE: Professional Revit Assistant (Livvy, female persona).\n"
        f"### TASK: The user has repeatedly flooded and ignored your request for parameters. Adopt a strict, but professional female persona.\n"
        f"</AGENT_STATE_DIRECTIVES>\n"
        f"<CANCELLATION_BEHAVIOR_RULES>\n"                        
        f"Be brief (max 2 short sentences), use 1 line break (\\n).\n"                    
        f"Tell the user in {detected_lang} that you are officially CANCELING the current task because they got you completely distracted with off-topic chatter (like cats/jokes).\n"
        f"</CANCELLATION_BEHAVIOR_RULES>\n"
        f"</TRANSACTION_EVICTION_PROTOCOL>"
    )
    
    try:
        eviction_payload = {
            "model": MODEL_NAME,
            "messages": [
                {'role': 'system', 'content': eviction_rules},
                {'role': 'user', 'content': f"{user_text}"}
            ],
            "temperature": 0.6, 
            "provider": PROVIDER_FLEX,
            "reasoning": {"effort": "low", "exclude": True, "enabled": True} 
        }
        import requests
        ev_res = requests.post(FULL_URL, json=eviction_payload, headers=headers, timeout=20)
        if ev_res.status_code == 200:
            return ev_res.json()["choices"][0]["message"]["content"]
        else:
            log(f"[!] Eviction API returned HTTP {ev_res.status_code}: {ev_res.text}")
            return f"System Error: API returned HTTP {ev_res.status_code} during eviction query."
    except Exception as ev_err:
        log(f"[!] Dynamic eviction network failure: {ev_err}")
        return f"System Error: Network connection failed during eviction processing."

def process_ai_logic(user_text, client, generation=0):
    global chat_history, detected_lang, dynamic_manual, bim_sequence_tool

    use_provider("teamorouter")
    
    log("[START] START OF SESSION (Stateless Mode via Function Calling)...") 
    
    headers = {
        "Authorization": f"Bearer {API_KEY}",
        "Content-Type": "application/json"
    }
    
    try:

        log(f"[***] Количество попыток повтора параметров:{session_state.flood_count}")
        

        # --- 1. СИНХРОНИЗАЦИЯ С REVIT (СТАТИЧНАЯ ЧАСТЬ ДЛЯ КЭША) ---
        system_rules = instructions.PROFILES['default']

        res_manual, _ = exchange(client, {"action": "get_manual"}, generation)

        if dynamic_manual is None:
            dynamic_manual = res_manual.get("manual", "Tools are not available.")
        tools_manifest = f"<AVAILABLE_BIM_TOOLS>:\n{dynamic_manual}\n</AVAILABLE_BIM_TOOLS>\n"

        #log(f"[***] AVAILABLE_BIM_TOOLS:{dynamic_manual}")
        
        
        current_session = [{
            'role': 'system',
            'content': [
                {"type": "string", "text": system_rules},
                {"type": "text", "text": tools_manifest, "cache_control": {"type": "ephemeral"}},
            ]
        }]
        
        """
        # Замок кэша ставится на САМОЕ ПОСЛЕДНЕЕ сообщение из вашего списка примеров
        instructions.FEW_SHOT_EXAMPLES[-1]["cache_control"] = {"type": "ephemeral"}

        current_session = [
            # 1. Сначала идет система
            {"role": "system", "content": f"{system_rules}\n\n{tools_manifest}", "cache_control": {"type": "ephemeral"}},
            *instructions.FEW_SHOT_EXAMPLES 
        ]
        """
        
        # Создаем базовую сессию
        current_session = [{
            'role': 'system',
            'content': [
                {"type": "string", "text": system_rules},
                {"type": "text", "text": tools_manifest, "cache_control": {"type": "ephemeral"}}, # ФИКСИРУЕМ КЭШ ЗДЕСЬ
            ]
        }]

        # =================================================================
        # ГРАНИЦА КЭША СЕРВЕРА ЗДЕСЬ. ВСЁ, ЧТО НИЖЕ — СБОРКА ОДНОГО ДИНАМИЧЕСКОГО ХВОСТА
        # ================================================================= 

        # Подключаем Few-Shot примеры
        current_session.extend(instructions.FEW_SHOT_EXAMPLES) 

        if detected_lang is None:
            res_lang, _  = exchange(client, {"action": "get_language"}, generation)
            detected_lang = res_lang.get("language", "English")

        # Перед началом сборки промпта проверяем состояние замка
        if session_state.active_param in [None,""]:
            # Чистый старт новой задачи — текущий текст становится базовым намерением
            session_state.user_steps = [user_text]
        else:
            # Мы внутри замка, пользователь прислал уточнение — пушим его в массив
            session_state.user_steps.append(user_text)

        # Собираем структурированный блок хронологии запроса
        payload_markup = []
        for idx, step in enumerate(session_state.user_steps):
            if idx == 0:
                payload_markup.append(f"- ORIGINAL_MESSAGE: \"{step}\"")
            else:
                payload_markup.append(f"- REFINEMENT_MESSAGE_{idx}: \"{step}\"")
        
        user_payload_string = "\n".join(payload_markup)

        log(f"[***] user payload string:{user_payload_string}")

        res_context, _ = exchange(client, {"action": "get_context_state"}, generation)           
        if not res_context or not isinstance(res_context, dict):
            res_context = {}
        current_tags = res_context.get("tags", "There are no active tags in memory.")    
        log(f"[***] Теги:{current_tags}")



        # Начало сборки финального пользовательского ввода
        user_composite_content = (
            f"<REVIT_RUNTIME_CONTEXT>\n"
            f"<ACTIVE_REVIT_MEMORY_TAGS>\n"
            f"{current_tags}\n"
            f"</ACTIVE_REVIT_MEMORY_TAGS>\n"
            \
            f"<TRACKING_AND_INCREMENTATION_RULES>\n"
            f"1. The tags inside <ACTIVE_REVIT_MEMORY_TAGS> represent elements currently existing in the Revit project.\n"
            f"2. You CAN use these exact tags as INPUT arguments (parameter 'In') if the user refers to them.\n"
            f"3. STRICT DUPLICATION FORBIDDEN: When creating a NEW output tag (parameter 'Out'), you MUST read the list above, find the highest index for the category, and increment it by +1. Never generate a tag name that is already listed in <ACTIVE_REVIT_MEMORY_TAGS>!\n"
            f"4. YOU ARE STRICTLY FORBIDDEN FROM OUTPUTTING TAGS WITHOUT A TRAILING NUMBER LIKE '#create_wall' '#ok' ! EVERY NEW TAG MUST HAVE AN INDEX!"
            f"</TRACKING_AND_INCREMENTATION_RULES>\n"
            \
            f"<LOCALIZATION_DIRECTIVES>\n"
            f"You MUST write the 'user_facing_message' field strictly in {detected_lang}.\n"
            f"When a sequence is ready to execute, user_facing_message MUST preview the Revit order before it runs.\n"
            f"</LOCALIZATION_DIRECTIVES>\n"
            \
            f"<TARGET_LANGUAGE>{detected_lang}</TARGET_LANGUAGE>\n"
            f"<USER_REQUEST_PAYLOAD>\n"
            f"{user_payload_string}\n"  # <-- ТЕПЕРЬ ЗДЕСЬ СТРУКТУРИРОВАННАЯ ХРОНОЛОГИЯ ШАГОВ
            f"</USER_REQUEST_PAYLOAD>\n"
            f"</REVIT_RUNTIME_CONTEXT>"
        )

 

        # Инжектируем собранный хвост в сессию как ЕДИНСТВЕННОЕ финальное USER сообщение
        current_session.append({'role': 'user', 'content': user_composite_content})         
         

        # --- ШАГ 4. ЗАПРОС ПЛАНА (РЕЖИМ AUTO) --- 
        log(f">>> [LOGIC] Requesting plan from AI via Function Calling...")
        plan_payload = {
            "model": MODEL_NAME,
            "messages": current_session,
            "tools": [bim_sequence_tool],
            "tool_choice": "auto", 
            "temperature": 0.0, 
            "provider": PROVIDER,
            "reasoning": {
                "effort": "high",
                "exclude": True,
                "enabled": True
            },
            "max_tokens": 8192,
            # "top_p": 0.01,
            # "frequency_penalty": 0.0,
            # "presence_penalty": 0.0,
        }
        
        try:
            response = requests.post(FULL_URL, json=plan_payload, headers=headers, timeout=60)
        except requests.exceptions.Timeout:
            log("[!] CRITICAL: Gemini API read timeout on step 3 (60s exceeded).")
            exchange(client, {"action": "ping"}, generation)
            return "The AI server is overloaded. Please retry your request."
        
        # --- ОТЛАДКА СЫРОГО ОТВЕТА ---
        log(f"[DEBUG] HTTP Status: {response.status_code}")
        log(f"[DEBUG] Raw Response Content: {response.text}") # Посмотрим первые 500 символов ответа
        
        if response.status_code != 200:
            log(f"[!] Plan API returned HTTP {response.status_code}: {response.text}")
            exchange(client, {"action": "ping"}, generation)
            return f"API Error {response.status_code}: {response.text}"

            
        res_data = response.json()
        message_node = res_data["choices"][0]["message"]

        # Отмена могла прийти, пока ждали план. Не выполняем sequence и не отвечаем в чат.
        exchange(client, {"action": "ping"}, generation)
    
                # -----------------------------------------------------------------
        # ШАГ 4. СЦЕНАРИЙ А: ПОЛЬЗОВАТЕЛЬ ПРОСТО БОЛТАЕТ (SMALL TALK / FLOOD)
        # -----------------------------------------------------------------
        if "tool_calls" not in message_node or not message_node["tool_calls"]:
            log("[*] AI chose SMALL TALK.")
            raw_content = message_node["content"]

            log(f"[DEBUG] СЦЕНАРИЙ А")       

            session_state.flood_count += 1
            log(f"[FLOOD] Inside context tracker: {session_state.flood_count}/{MAX_FLOOD_ATTEMPTS}")         
            
            # Если болтовня происходит внутри открытого замка контекста
            if session_state.active_param not in [None,""] or len(session_state.user_steps) > 1:   
                # КРАСНАЯ КАРТОЧКА: Превышен лимит попыток — принудительная отмена задачи
                if session_state.flood_count > MAX_FLOOD_ATTEMPTS:
                    log(f"[-] Flood limit blown. Dropping context dynamically via Gemini: {session_state.active_param}")
                    
                    # Полный сброс параметров на бэкенде — задача официально закрыта
                    session_state.clear()
                    
                    return execute_eviction_protocol(user_text, detected_lang, headers)

            return raw_content

        # -----------------------------------------------------------------
        # ШАГ 2. СЦЕНАРИЙ Б: ИИ СГЕНЕРИРОВАЛ ВЫЗОВ ФУНКЦИИ
        # -----------------------------------------------------------------
        tool_call = message_node["tool_calls"][0]["function"]
        data = json.loads(tool_call["arguments"]) if isinstance(tool_call["arguments"], str) else tool_call["arguments"]      

        log(f"[DEBUG] СЦЕНАРИЙ Б") 

        ai_active_context_tool = data.get("active_context_tag", "")
             
        ai_user_msg = data.get("user_facing_message", "").strip()
        ai_sequence = data.get("sequence", [])

        log(f">>> [LOGIC] Machine parameter from AI: '{ai_active_context_tool}'")
        log(f">>> [LOGIC] Sequence: {ai_sequence}")
        log(f">>> [LANG] User language: {detected_lang}")

        # Модель иногда ставит active_context_tag, хотя параметр уже есть в sequence
        # (как filter_parameters:Value при Value="Гараж"). Тогда не блокируем исполнение.
        if ai_active_context_tool not in [None, ""] and ai_sequence and _sequence_has_context_param(ai_sequence, ai_active_context_tool):
            log(f"[*] Ignoring stale active_context_tag '{ai_active_context_tool}' — value already present in sequence.")
            ai_active_context_tool = ""
            session_state.clear()

        # ВАРИАНТ 1: ИИ подтверждает, что параметров не хватает (удерживаем или создаем замок)
        if ai_active_context_tool not in [None,""]:
            log(f"[*] AI flagged incomplete query. Registering/Updating focus lock: {ai_active_context_tool}")            
            
            # Проверяем: ИИ просит ТОТ ЖЕ самый параметр или уже СЛЕДУЮЩИЙ?
            if session_state.active_param == ai_active_context_tool:
                # Пользователь не дал нужный параметр, флудит
                session_state.flood_count += 1
            else:
                # Пользователь дал параметр, ИИ перешел к следующему (или это первый параметр)
                session_state.set_context(param_name=ai_active_context_tool)
                session_state.flood_count = 1
                
            log(f"[FLOOD] сценарий Б AI ждет уточнения: {session_state.flood_count}/{MAX_FLOOD_ATTEMPTS}")
            
            if session_state.flood_count > MAX_FLOOD_ATTEMPTS:
                log(f"[-] Flood limit blown in Scenario B. Dropping context dynamically.")
                session_state.clear()
                return execute_eviction_protocol(user_text, detected_lang, headers)

            if ai_user_msg:
                return ai_user_msg

        # ВАРИАНТ 2: ИИ не вернул замок, но и команд в sequence нет — скрытый сбой/флуд модели
        if not ai_sequence:
            # Если замок висел, расцениваем это как неудачную попытку уточнения (флуд)
            if session_state.active_param:
                session_state.flood_count += 1
                log(f"[FLOOD] сценарий Б неудачное уточнение: {session_state.flood_count}/{MAX_FLOOD_ATTEMPTS}")
                if session_state.flood_count > MAX_FLOOD_ATTEMPTS:
                    log("[-] Empty sequence flood limit blown in Scenario B. Clearing state.")
                    session_state.clear()
                    return execute_eviction_protocol(user_text, detected_lang, headers)
            
            if ai_user_msg:
                return ai_user_msg
            return "The task could not be completed. Please refine your request."

        # ВАРИАНТ 3: ТАКТИЧЕСКИЙ СБРОС (ИИ прислал пустой тег контекста и живой sequence)
        # Задача успешно дозаполнена или юзер сменил тему на другую валидную команду.
        if session_state.active_param in (None, "", {}):
            log(f"[+] Task Switching / Resolution detected! Erasing previous lock.")
            # Счетчик флуда сбросится ниже, перед выполнением батча

        # -----------------------------------------------------------------
        # --- 3. ВЫПОЛНЕНИЕ В REVIT (Batch Mode) ---
        # -----------------------------------------------------------------
        execution_result = {"success": False, "message": "No action taken"}

        if ai_sequence:
            if ai_user_msg:
                log(f"[*] Plan preview before Revit: {ai_user_msg}")
                exchange(client, {
                    "action": "ui_log",
                    "role": "ai",
                    "phase": "plan",
                    "content": ai_user_msg,
                    "turn": generation,
                }, generation)

            log(f"[*] Sending the sequence to Revit: {len(ai_sequence)} steps...")
            revit_res, _ = exchange(client, {
                "action": "call_batch",
                "sequence": ai_sequence,
                "generation": generation,
            }, generation)
            
            if revit_res and "batch_queued" in str(revit_res.get("status", "")):
                for _ in range(100):
                    time.sleep(0.3)
                    poll_res, ui_event = exchange(client, {"action": "ping"}, generation)
                    res = ui_event if ui_event else poll_res
                    if not isinstance(res, dict):
                        continue
                    result_generation = res.get("generation") or 0
                    if generation and result_generation and result_generation != generation:
                        continue
                    if res.get("cancelled"):
                        raise RequestAborted()
                    if res.get("action") == "tool_execution_result":
                        execution_result = res
                        break

            # abort часто оказывается следующим событием сразу за результатом шага.
            # Если это ещё не abort, а сам результат — не выбрасываем его.
            _, trailing = exchange(client, {"action": "ping"}, generation)
            if isinstance(trailing, dict) and trailing.get("action") == "tool_execution_result":
                result_generation = trailing.get("generation") or 0
                if not (generation and result_generation and result_generation != generation):
                    if trailing.get("cancelled"):
                        raise RequestAborted()
                    execution_result = trailing

            # --- 5. ФИНАЛЬНЫЙ ОТЧЕТ (ГУМАНИЗАТОР — ЗАПРОС 2) ---

            # 1. Базовая статическая часть системного промпта
            SYSTEM_PROMPT = (
                f"###REPORT_HUMANIZATION_SYSTEM\n"
                f"ROLE_AND_MISSION:\n"
                f"ROLE: Professional Revit Assistant (female persona, Livvy).\n"
                f"TASK: Humanize technical Revit reports <TECHNICAL_REPORT_LOG> into friendly chat messages.\n"
                f"ROLE_AND_MISSION>\n"
                f"###NARROW_INTERFACE_UX_RULES"
                f"CRITICAL MANDATES:"
                f"1. MAX LENGTH: Keep the entire response under 2-3 short sentences.\n"
                f"2. NARROW CHAT FORMATTING: Write in short, punchy fragments. Avoid long, wide blocks of text.\n"
                f"3. NO ROBOTIC DRYNESS: Stay friendly, warm, and professional. Use 1-2 emojis naturally to maintain persona.\n"
                f"MAIN_OUTPUT_DIRECTIVES:"
                f"1. LANGUAGE: You MUST answer strictly in <TARGET_LANGUAGE> using the past tense. Preserve parameter values explicitly provided by the user exactly as written; never translate or alter them.\n"
                f"2. GENDER: Your gender is female. Your name is Livvy. Adopt a friendly, supportive, and professional female persona. Be concise but encouraging.\n"
                f" 3. NEVER say hello or greet the user.\n"
            )

            cmd_prompt_override = execution_result.get("prompt")

            # 2. Ветка А: ОВЕРРАЙД КОМАНДЫ (Перезаписывает логику тегов)
            if cmd_prompt_override:
                log(f">>> [LOGIC] Output prompt: {cmd_prompt_override}")
                SYSTEM_PROMPT += (
                    f"<SPECIFIC_COMMAND_INSTRUCTION>\n"
                    f"{cmd_prompt_override}\n"
                    f"</SPECIFIC_COMMAND_INSTRUCTION>\n"
                )
            else:
                # 3. Ветка Б: УСПЕШНОЕ ВЫПОЛНЕНИЕ (Добавляем правила форматирования и синхронизации тегов)
                if execution_result.get("success"):

                    log(f">>> [EXECUTION] execution result message: '{execution_result.get('message')}'")
                
                    SYSTEM_PROMPT += (
                        f"###SUCCESS_REPORT_HUMANIZATION_SYSTEM\n"
                        f"OUTPUT_FORMATTING:\n"
                        f"1. Combine the <TECHNICAL_REPORT_LOG> into a single generalized statement, emphasizing the LAST STEP.\n"
                        f"2. Don't count the steps of actions. Skip straight to the FINAL result.\n"
                        f"3. Be concise, instead of a success message, proceed directly to FINAL result.\n" 
                        f"###REPORT_TAG_SYNCHRONIZATION\n"
                        f"3. TOOL TAGS: You need to look for the tag from the END of the <TECHNICAL_REPORT_LOG> and IF you find the tag then include it in the FINAL RESULT. Usually one tag is enough.\n"
                        f"4. NO GHOST TAGS: Never mention a tag not specified in the <TECHNICAL_REPORT_LOG>.\n"
                        f"5. MANDATORY HASHTAG PREFIX: You MUST always keep the '#' symbol prefix before any tag name (e.g., '#new_wall_1', '#found_walls_2').\n"
                        f"6. MANDATORY HASHTAG PREFIX & SUFFIX: You MUST always keep the '#' symbol prefix before any tag name. The tag name MUST strictly end with an underscore and a numerical index (e.g., '#create_wall_1', '#create_wall_2'). NEVER append any textual suffixes, status words like '#ok', or custom comments to the tag string itself.\n"
                    )
                # 4. Ветка В: ОБРАБОТКА ОШИБКИ (Исправлена f-строка на каждой линии)
                else:
                    SYSTEM_PROMPT += (
                        f"###ERROR_OUTPUT_DIRECTIVES\n"
                        f"1. DO NOT include '#' symbols in your answer.\n"
                        f"2. Describe the error briefly.\n"
                    )

            human_payload = {
                "model": MODEL_NAME,
                "messages": [
                    {'role': 'system', 'content': SYSTEM_PROMPT},
                    {
                        'role': 'user', 
                        'content': (
                            f"<TARGET_LANGUAGE>{detected_lang}</TARGET_LANGUAGE>\n"
                            f"\n<TECHNICAL_REPORT_LOG>\n"
                            f"{execution_result.get('message')}\n"
                            f"\n</TECHNICAL_REPORT_LOG>\n"
                        )
                    }
                ],
                "temperature": 0.1,
                "provider": PROVIDER_FLEX,
                "reasoning": {
                    "effort": "none",
                    "exclude": True,
                    "enabled": False
                },
                "max_tokens": 2048,               
                }

            try:
                final_response = requests.post(FULL_URL, json=human_payload, headers=headers, timeout=60)
                final_data = final_response.json()
                final_report_text = f"{final_data['choices'][0]['message']['content']}"
                log(f"[DEBUG] Raw Response Content (final_data): {final_report_text}")

                # СПИСОК СТОП-СЛОВ (Если ИИ выплюнул хотя бы одно, значит это утечка промпта!)
                PROMPT_LEAK_TRIGGERS = ["REPORT_HUMANIZATION_SYSTEM"]

                # Проверяем, не слил ли ИИ наши секретные системные теги и инструкции
                # Отмена во время гуманизации: текст уже есть, в чат его не отдаём.
                exchange(client, {"action": "ping"}, generation)

                if any(trigger in final_report_text for trigger in PROMPT_LEAK_TRIGGERS):
                    log(">>> [SECURITY ALERT] System prompt leak detected! Overriding AI output.")    
                    return "An unexpected error occurred while formatting the report. Please try again."

                session_state.clear()  # Полное финальное обнуление стейта для stateless-режима

                # --- 4. ФИКСАЦИЯ УДАЧНЫХ ОПЕРАЦИЙ ---
                if execution_result.get("success"):
                    log("[+] Success. Clear state memory and save to history.")                    

                    # 2. ИМИТИРУЕМ ОТВЕТ ИИ С ВЫЗОВОМ ФУНКЦИИ (Возвращаем ей память)
                    chat_history.append({'role': 'user', 'content': user_text})                    
                    chat_history.append({
                        'role': 'assistant',
                        'content': final_report_text,
                        'tool_calls': [{
                            'id': f"call_exec_{int(time.time())}",
                            'type': 'function',
                            'function': {
                                'name': 'execute_bim_sequence',
                                'arguments': json.dumps({
                                    #'thought': ai_thought,
                                    'sequence': ai_sequence
                                })
                            }
                        }]
                    })
                
                    # Удерживаем строго последние 5 элементов в памяти
                    #chat_history = chat_history[-5:] 
                    save_history(chat_history)

                if session_state.active_param is None and session_state.flood_count > MAX_FLOOD_ATTEMPTS: session_state.clear()

                log(f"\n\n")

                return final_report_text

            except RequestAborted:
                raise
            except Exception as final_err:
                log(f"[!] Error on step final response: {final_err}")
                return f"Execution context: {execution_result.get('message')}"

        else:
            return res_data

    except RequestAborted:
        log("[!] User cancelled the request. Assistant reply suppressed.")
        session_state.clear()
        raise
    except Exception as e:
        log(f"[CRITICAL] Logic Crash inside Function Calling engine: {e}")
        return f"System Error: {str(e)}"

def main():
    client = RevitPipeClient(PIPE_NAME)
    if not client.connect(): return

    # Статус в UI
    client.send_receive({
        "action": "ui_log",
        "type": "system_status",
        "model": MODEL_NAME,
        "status": "online"
    })

    print("[*] Waiting for commands...")
    
    while True:
        # Пинг для получения событий из WebView2
        _, ui_event = client.send_receive({"action": "ping"})

        if ui_event:
            action = ui_event.get("action")
            if action == "chat_request":
                prompt = ui_event.get("prompt")
                generation = ui_event.get("generation") or 0
                print(f"\n[USER]: {prompt}")
                
                try:
                    ai_text = process_ai_logic(prompt, client, generation)
                except RequestAborted:
                    log("[!] User cancelled the request. Assistant reply suppressed.")
                    continue
                
                # Отправляем в UI Revit. turn нужен, чтобы отменённый ход не всплыл новым сообщением.
                client.send_receive({
                    "action": "ui_log", 
                    "role": "ai", 
                    "content": ai_text,
                    "turn": generation,
                })

            elif action in ("stop", "abort"):
                print("\n[!] EMERGENCY STOP")
        
        time.sleep(0.4)

if __name__ == "__main__":
    try:
        main()
    except Exception as fatal:
        log(f"Fatal Error: {fatal}")
        import traceback
        log(traceback.format_exc())
        input("Window will not close. Check the log and press Enter...")
