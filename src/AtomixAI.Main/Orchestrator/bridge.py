import json

class RevitAIBridge:
    @staticmethod
    def format_tool_result(tool_name, revit_res):
        """Превращает данные Revit в понятный отчет для ИИ."""
        is_success = revit_res.get("success", False)
        data = revit_res.get("data")
        msg = revit_res.get("message", "No message")

        # Умный подсчет количества
        count = len(data) if isinstance(data, list) else (1 if data else 0)
        
        # Формируем отчет с четкими метками
        return (
            f"STATUS: {'SUCCESS' if is_success else 'ERROR'}\n"
            f"TOOL: {tool_name}\n"
            f"ELEMENTS_COUNT: {count}\n"
            f"DATA_CONTENT: {str(data)[:300]}\n" # Ограничение длины
            f"REVIT_LOG: {msg}\n"
            f"INSTRUCTION: Use ELEMENTS_COUNT for your answer. Speak RUSSIAN only."
        )

    @staticmethod
    def mcp_to_ollama(mcp_tools):
        """Конвертация схемы инструментов."""
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
