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
        names = {"ContextStateManager", "process_ai_logic", "main"}
        selected = [node for node in tree.body if isinstance(node, (ast.ClassDef, ast.FunctionDef)) and node.name in names]
        self.plans = []

        def post(url, json, **kwargs):
            self.plans.append(copy.deepcopy(json))
            return SimpleNamespace(status_code=200, text="mock", json=lambda: {
                "choices": [{"message": {"tool_calls": [{"function": {"arguments": {
                    "active_context_tag": "filter_parameters:Value",
                    "user_facing_message": "Provide a value.", "sequence": []
                }}}]}}]
            })

        self.env = {
            "use_provider": lambda *args: None, "log": lambda *args: None,
            "API_KEY": "test", "FULL_URL": "mock", "MODEL_NAME": "mock", "PROVIDER": {},
            "requests": SimpleNamespace(post=post, exceptions=SimpleNamespace(Timeout=TimeoutError)),
            "instructions": SimpleNamespace(PROFILES={"default": "rules"}, FEW_SHOT_EXAMPLES=[]),
            "exchange": lambda *args: ({"manual": "commands", "language": "English"}, None),
            "chat_history": [], "detected_lang": None, "dynamic_manual": None, "bim_sequence_tool": {},
            "json": json, "MAX_FLOOD_ATTEMPTS": 10,
            "_sequence_has_context_param": lambda *args: False,
            "RequestAborted": type("RequestAborted", (Exception,), {}),
        }
        exec(compile(ast.Module(body=selected, type_ignores=[]), str(source), "exec"), self.env)
        self.env["session_state"] = self.env["ContextStateManager"]()

    def test_snapshot_survives_refinement_and_reaches_model(self):
        chips = [{"id": "chip_1", "label": "#Height", "data": {"value": 12, "units": "mm"}}]
        process = self.env["process_ai_logic"]
        self.assertEqual(process("Use [chip_1]", None, 1, chips), "Provide a value.")
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


if __name__ == "__main__":
    unittest.main()
