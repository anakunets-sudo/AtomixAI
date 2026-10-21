import ast
import copy
import json
from pathlib import Path
import unittest
from types import SimpleNamespace


class ChipContextTests(unittest.TestCase):
    def setUp(self):
        source = Path(__file__).resolve().parents[1] / "src/AtomixAI.Main/Orchestrator/orchestrator.py"
        tree = ast.parse(source.read_text(encoding="utf-8-sig"))
        names = {"ContextStateManager", "build_bim_sequence_tool", "process_ai_logic", "main"}
        selected = [node for node in tree.body if isinstance(node, (ast.ClassDef, ast.FunctionDef)) and node.name in names]
        self.plans = []
        self.actions = []
        self.tool_catalog = {"bim_tools": {
            "SEARCH": {
                "category_description": "SAFE: Find and filter elements.",
                "tools": [{
                    "name": "filter_elements",
                    "keywords": ["filter", "category", "class"],
                    "description": "Filter elements by category or class.",
                    "parameters": {
                        "type": "object",
                        "properties": {"Params": {"type": "object", "properties": {"Categories": {"type": "array", "items": {"type": "string"}}}}},
                        "required": ["Params", "Out"],
                    },
                }, {
                    "name": "select_elements",
                    "keywords": ["select", "highlight"],
                    "description": "Select a saved set of elements.",
                    "parameters": {"type": "object", "properties": {"In": {"type": "string"}}, "required": ["In", "Out"]},
                }],
            }
        }}

        def post(url, json, **kwargs):
            self.plans.append(copy.deepcopy(json))
            return SimpleNamespace(status_code=200, text="mock", json=lambda: {
                "choices": [{"message": {"tool_calls": [{"function": {"arguments": {
                    "active_context_tag": "filter_parameters:Value",
                    "user_facing_message": "Provide a value.", "sequence": []
                }}}]}}]
            })

        def exchange(client, payload, *args):
            action = payload.get("action")
            self.actions.append(action)
            if action == "list":
                return self.tool_catalog, None
            if action == "get_context_state":
                return {"tags": "There are no active tags in memory.", "recent_user_tags": ""}, None
            return {"language": "English"}, None

        self.env = {
            "use_provider": lambda *args: None, "log": lambda *args: None,
            "API_KEY": "test", "FULL_URL": "mock", "MODEL_NAME": "mock", "PROVIDER": {},
            "requests": SimpleNamespace(post=post, exceptions=SimpleNamespace(Timeout=TimeoutError)),
            "instructions": SimpleNamespace(PROFILES={"default": "rules"}, FEW_SHOT_EXAMPLES=[]),
            "exchange": exchange,
            "chat_history": [], "detected_lang": None, "bim_sequence_tool": {
                "type": "function",
                "function": {
                    "name": "execute_bim_sequence",
                    "parameters": {
                        "type": "object",
                        "properties": {
                            "sequence": {"type": "array", "items": {"type": "object", "properties": {"name": {}, "arguments": {}}}}
                        }
                    }
                }
            },
            "json": json, "MAX_FLOOD_ATTEMPTS": 10,
            "_sequence_has_context_param": lambda *args: False,
            "RequestAborted": type("RequestAborted", (Exception,), {}),
        }
        exec(compile(ast.Module(body=selected, type_ignores=[]), str(source), "exec"), self.env)
        self.env["session_state"] = self.env["ContextStateManager"]()

    def test_snapshot_survives_refinement_and_reaches_model(self):
        chips = [{"id": "chip_1", "label": "#Height", "data": {"value": 12, "units": "mm"}}]
        process = self.env["process_ai_logic"]
        self.assertEqual(process("Use [chip_1]", None, 1, chips), ("Provide a value.", False))
        sequence_items = self.plans[-1]["tools"][0]["function"]["parameters"]["properties"]["sequence"]["items"]
        self.assertEqual(len(sequence_items["anyOf"]), 2)
        self.assertEqual(sequence_items["anyOf"][0]["properties"]["name"]["enum"], ["filter_elements"])
        self.assertIn("SEARCH: SAFE: Find and filter elements.", self.plans[-1]["tools"][0]["function"]["description"])
        self.assertEqual(self.plans[-1]["tools"][0]["function"]["description"].count("SAFE: Find and filter elements."), 1)
        name_schema = sequence_items["anyOf"][0]["properties"]["name"]
        self.assertIn("SEARCH command: Filter elements by category or class.", name_schema["description"])
        self.assertIn("filter, category, class", name_schema["description"])
        arguments_schema = sequence_items["anyOf"][0]["properties"]["arguments"]
        self.assertEqual(arguments_schema["properties"]["Params"]["properties"]["Categories"]["type"], "array")
        self.assertIsInstance(arguments_schema["required"], list)
        self.assertIn("Out", arguments_schema["required"])
        self.assertNotIn("get_manual", self.actions)
        chips[0]["data"]["units"] = "changed"
        process("Use 20 instead", None, 2, [])
        content = self.plans[-1]["messages"][-1]["content"]
        self.assertIn("[chip_1]", content)
        self.assertIn('"units": "mm"', content)
        self.assertEqual(len(self.env["session_state"].user_steps), 2)
        self.env["session_state"].clear()
        process("New task", None, 3, [])
        self.assertNotIn("chip_1", self.plans[-1]["messages"][-1]["content"])

    def test_abort_clears_snapshot(self):
        self.env["process_ai_logic"]("Use [chip_1]", None, 1, [{"id": "chip_1", "data": {}}])
        def abort(*args):
            raise self.env["RequestAborted"]()
        self.env["exchange"] = abort
        with self.assertRaises(self.env["RequestAborted"]):
            self.env["process_ai_logic"]("refinement", None, 1, [])
        self.assertEqual(self.env["session_state"].user_steps, [])

    def test_sequence_schema_binds_arguments_to_command(self):
        tool = {
            "function": {
                "parameters": {
                    "description": "Executes a pipeline.",
                    "properties": {
                        "sequence": {"items": {"type": "object", "properties": {"name": {}, "arguments": {}}}}
                    }
                }
            }
        }
        self.env["bim_sequence_tool"] = tool
        schema = self.env["build_bim_sequence_tool"](self.tool_catalog)
        variants = schema["function"]["parameters"]["properties"]["sequence"]["items"]["anyOf"]
        self.assertEqual(variants[0]["properties"]["name"]["enum"], ["filter_elements"])
        self.assertIn("SEARCH command", variants[0]["properties"]["name"]["description"])
        self.assertIn("filter, category, class", variants[0]["properties"]["name"]["description"])
        self.assertEqual(variants[0]["properties"]["arguments"]["properties"]["Params"]["properties"]["Categories"]["type"], "array")
        self.assertEqual(tool["function"]["parameters"]["properties"]["sequence"]["items"].get("type"), "object")


if __name__ == "__main__":
    unittest.main()
