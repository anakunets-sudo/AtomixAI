import ollama
import requests
import json

# Адрес вашего Revit-плагина (если внедрим локальный сервер в Bridge)
REVIT_URL = "http://localhost:5000/tools" 

def run_orchestrator():
    # 1. Получаем список инструментов из Revit (тот самый GetToolsJson)
    try:
        response = requests.get(f"{REVIT_URL}/list")
        revit_tools = response.json()["tools"]
    except Exception as e:
        print(f"Ошибка связи с Revit: {e}")
        return

    # 2. Запрос к пользователю
    user_input = input("Что нужно сделать в Revit? ")

    # 3. Отправляем в Ollama (Qwen 2.5)
    print("Думаю...")
    response = ollama.chat(
        model='qwen2.5:7b',
        messages=[{'role': 'user', 'content': user_input}],
        tools=revit_tools # Передаем схему из вашего Registry.cs
    )

    # 4. Если модель решила вызвать инструмент
    if response.get('message', {}).get('tool_calls'):
        for tool in response['message']['tool_calls']:
            print(f"Выполняю команду: {tool['function']['name']}")
            
            # Шлем команду обратно в Revit
            requests.post(f"{REVIT_URL}/call", json={
                "name": tool['function']['name'],
                "arguments": tool['function']['arguments']
            })
    else:
        print("Ответ модели:", response['message']['content'])

if __name__ == "__main__":
    run_orchestrator()