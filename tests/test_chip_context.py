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
        names = {"ContextStateManager", "build_bim_sequence_tool", "process_ai_logic", "main",
                 "_matches_command_schema", "_is_valid_sequence", "_sequence_has_context_param"}
        selected = [node for node in tree.body if isinstance(node, (ast.ClassDef, ast.FunctionDef)) and node.name in names]
        selected = [node for node in tree.body if isinstance(node, ast.Assign)
                    and any(isinstance(target, ast.Name) and target.id == "bim_sequence_tool" for target in node.targets)] + selected
        self.plans = []
        self.actions = []
        self.exchanges = []
        self.evictions = []
        self.reports = []
        self.reply = {"intent": "refinement", "active_context_tag": "filter_parameters:Value",
                      "user_facing_message": "Provide a value.", "sequence": []}
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
                "choices": [{"message": {"tool_calls": [{"function": {
                    "name": "execute_bim_sequence", "arguments": copy.deepcopy(self.reply)
                }}]}}]
            })

        def exchange(client, payload, *args):
            action = payload.get("action")
            self.actions.append(action)
            self.exchanges.append(copy.deepcopy(payload))
            if action == "list":
                return self.tool_catalog, None
            if action == "get_context_state":
                return {"tags": "There are no active tags in memory.", "recent_user_tags": ""}, None
            return {"language": "English"}, None

        def report(payload, *args, **kwargs):
            self.reports.append(copy.deepcopy(payload))
            return "Done.", False, None

        def evict(*args):
            self.evictions.append(args)
            return "Cancelled due to off-topic input."

        self.env = {
            "use_provider": lambda *args: None, "log": lambda *args: None,
            "API_KEY": "test", "FULL_URL": "mock", "MODEL_NAME": "mock", "PROVIDER": {},
            "requests": SimpleNamespace(post=post, exceptions=SimpleNamespace(Timeout=TimeoutError)),
            "instructions": SimpleNamespace(PROFILES={"default": "rules"}, FEW_SHOT_EXAMPLES=[]),
            "exchange": exchange,
            "chat_history": [], "detected_lang": None,
            "sequence_tool": None, "res_tools": None,
            "json": json, "MAX_FLOOD_ATTEMPTS": 10,
            "RequestAborted": type("RequestAborted", (Exception,), {}),
            "ACTIVE_NAME": "openrouter", "PROVIDER_FLEX": {},
            "stream_completion": report, "execute_eviction_protocol": evict,
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

    def lock_old_task(self):
        state = self.env["session_state"]
        state.replace_task({"text": "Build old wall using [chip_old]", "chips": [
            {"id": "chip_old", "data": {"value": "old"}}]}, "create_wall:Length")
        state.flood_count = 1
        return state

    def valid_sequence(self):
        return [{"name": "filter_elements", "arguments": {
            "Params": {"Categories": ["OST_Doors"]}, "Out": "#doors_1"}}]

    def test_intent_schema_is_required_and_constrained(self):
        schema = self.env["bim_sequence_tool"]["function"]["parameters"]
        self.assertIn("intent", schema["required"])
        self.assertEqual(set(schema["properties"]["intent"]["enum"]),
                         {"refinement", "new_task", "off_topic", "abort_task"})

    def test_new_task_replaces_history_and_chips_before_next_refinement(self):
        state = self.lock_old_task()
        self.reply.update(intent="new_task", active_context_tag="filter_elements:Categories")
        chips = [{"id": "chip_new", "label": "New value", "data": {"value": "new"}}]
        self.env["process_ai_logic"]("Find [chip_new]", None, 7, chips)
        classification = self.plans[-1]["messages"][-1]["content"]
        self.assertIn("Build old wall", classification)
        self.assertIn("<ACTIVE_CLARIFICATION_LOCK>create_wall:Length", classification)
        self.assertIn("<LATEST_USER_TURN>", classification)
        self.assertEqual(state.user_steps, [{"text": "Find [chip_new]", "chips": chips}])
        self.assertEqual(state.flood_count, 0)
        self.assertEqual(state.active_param, "filter_elements:Categories")
        self.reply["intent"] = "refinement"
        self.env["process_ai_logic"]("Doors", None, 8, [])
        refinement = self.plans[-1]["messages"][-1]["content"]
        self.assertIn('ORIGINAL_MESSAGE: "Find [chip_new]"', refinement)
        self.assertIn('REFINEMENT_MESSAGE_1: "Doors"', refinement)
        self.assertIn("chip_new", refinement)
        self.assertNotIn("chip_old", refinement)
        self.assertNotIn("Build old wall", refinement)
        self.assertNotIn("call_batch", self.actions)

    def test_complete_new_task_executes_once_despite_old_lock(self):
        state = self.lock_old_task()
        self.reply.update(intent="new_task", active_context_tag="", sequence=self.valid_sequence())
        self.assertEqual(self.env["process_ai_logic"]("Find doors", None, 12), ("Done.", False))
        batches = [payload for payload in self.exchanges if payload["action"] == "call_batch"]
        self.assertEqual(batches, [{"action": "call_batch", "sequence": self.valid_sequence(), "generation": 12}])
        self.assertNotIn("chip_old", self.reports[-1]["messages"][-1]["content"])
        self.assertEqual(state.user_steps, [])
        self.assertIsNone(state.active_param)
        self.assertEqual(state.flood_count, 0)

    def test_relevant_incomplete_refinement_never_increments_flood(self):
        state = self.lock_old_task()
        self.reply.update(intent="refinement", active_context_tag="create_wall:Length", user_facing_message="")
        for _ in range(5):
            self.env["process_ai_logic"]("Use a short wall", None)
        self.assertEqual(state.flood_count, 1)
        self.assertEqual(state.active_param, "create_wall:Length")
        self.assertEqual(len(state.user_steps), 6)
        self.reply["active_context_tag"] = ""
        self.env["process_ai_logic"]("Still short", None)
        self.assertEqual(state.flood_count, 1)
        self.assertEqual(state.active_param, "create_wall:Length")
        self.assertFalse(self.evictions)
        self.assertNotIn("call_batch", self.actions)

    def test_off_topic_only_evicts_above_existing_threshold(self):
        state = self.lock_old_task()
        state.flood_count = 0
        self.env["MAX_FLOOD_ATTEMPTS"] = 2
        # Even a contradictory executable sequence must never be dispatched.
        self.reply.update(intent="off_topic", active_context_tag="different:Field", sequence=self.valid_sequence())
        for count in (1, 2):
            self.env["process_ai_logic"]("Tell me a cat joke", None)
            self.assertEqual(state.flood_count, count)
            self.assertEqual(state.active_param, "create_wall:Length")
            self.assertFalse(self.evictions)
        result = self.env["process_ai_logic"]("More cats", None)
        self.assertEqual(result, ("Cancelled due to off-topic input.", False))
        self.assertEqual(len(self.evictions), 1)
        self.assertEqual(state.user_steps, [])
        self.assertIsNone(state.active_param)
        self.env["process_ai_logic"]("More cats without a task", None)
        self.assertEqual(state.flood_count, 0)
        self.assertEqual(len(self.evictions), 1)
        self.assertNotIn("call_batch", self.actions)

    def test_explicit_abort_clears_without_eviction_or_execution(self):
        state = self.lock_old_task()
        self.reply.update(intent="abort_task", sequence=self.valid_sequence(), user_facing_message="Cancelled.")
        self.assertEqual(self.env["process_ai_logic"]("Cancel it", None), ("Cancelled.", False))
        self.assertIsNone(state.active_param)
        self.assertEqual(state.flood_count, 0)
        self.assertEqual(state.user_steps, [])
        self.assertFalse(self.evictions)
        self.assertNotIn("call_batch", self.actions)

    def test_malformed_intent_preserves_lock_and_never_executes(self):
        for intent in (None, "execute", [], {}):
            with self.subTest(intent=intent):
                state = self.lock_old_task()
                self.reply.update(intent=intent, active_context_tag="", sequence=self.valid_sequence())
                self.env["process_ai_logic"]("Doors", None)
                self.assertEqual(state.active_param, "create_wall:Length")
                self.assertEqual(state.flood_count, 1)
                self.assertIn("Build old wall", state.user_steps[0]["text"])
        del self.reply["intent"]
        self.env["process_ai_logic"]("Doors", None)
        self.assertEqual(state.active_param, "create_wall:Length")
        self.assertNotIn("call_batch", self.actions)

    def test_invalid_sequences_are_never_dispatched(self):
        valid = self.valid_sequence()
        invalid = [None, "stale", {}, [None], [{"name": "unknown", "arguments": {}}],
                   [{"name": "filter_elements", "arguments": {"Params": {}}}],
                   [{"name": "filter_elements", "arguments": {
                       "Params": {"Categories": "OST_Doors"}, "Out": "#doors_1"}}]]
        for sequence in invalid:
            with self.subTest(sequence=sequence):
                state = self.lock_old_task()
                self.reply.update(intent="refinement", active_context_tag="", sequence=sequence)
                self.env["process_ai_logic"]("Doors", None)
                self.assertEqual(state.active_param, "create_wall:Length")
        self.assertTrue(self.env["_is_valid_sequence"](valid, self.tool_catalog))
        self.assertNotIn("call_batch", self.actions)

    def test_catalog_validation_checks_nested_required_types_and_unknown_fields(self):
        validate = self.env["_matches_command_schema"]
        schema = {"type": "object", "required": ["Params"], "additionalProperties": False,
                  "properties": {"Params": {"type": "object", "required": ["Value"],
                      "additionalProperties": False, "properties": {
                          "Value": {"type": "string"}, "Enabled": {"type": "boolean"},
                          "Count": {"type": "integer"}, "Mode": {"type": "string", "enum": ["A", "B"]}}}}}
        self.assertTrue(validate({"Params": {"Value": "", "Enabled": False, "Count": 0, "Mode": "A"}}, schema))
        for arguments in ({"Params": {}}, {"Params": {"Value": None}}, {"Params": {"Value": 1}},
                          {"Params": {"Value": "x", "Count": True}},
                          {"Params": {"Value": "x", "Enabled": "false"}},
                          {"Params": {"Value": "x", "Mode": "C"}},
                          {"Params": {"Value": "x", "Unexpected": 1}},
                          {"Params": {"Value": "x"}, "Unexpected": 1}):
            with self.subTest(arguments=arguments):
                self.assertFalse(validate(arguments, schema))

    def test_new_task_empty_sequence_cannot_reuse_previous_sequence(self):
        self.lock_old_task()
        self.reply.update(intent="new_task", active_context_tag="", sequence=self.valid_sequence())
        self.env["process_ai_logic"]("Find doors", None)
        self.reply.update(sequence=[], active_context_tag="")
        self.env["process_ai_logic"]("Build something", None)
        self.assertEqual(self.actions.count("call_batch"), 1)
        self.assertEqual(self.env["session_state"].user_steps, [{"text": "Build something", "chips": []}])

    def test_stale_filled_lock_does_not_block_valid_new_task(self):
        self.lock_old_task()
        self.reply.update(intent="new_task", active_context_tag="filter_elements:Categories", sequence=self.valid_sequence())
        self.env["process_ai_logic"]("Find doors", None)
        self.assertEqual(self.actions.count("call_batch"), 1)

    def test_unfilled_lock_blocks_sequence_even_without_message(self):
        self.lock_old_task()
        self.reply.update(intent="new_task", active_context_tag="filter_elements:Missing", sequence=self.valid_sequence(),
                          user_facing_message="")
        self.env["process_ai_logic"]("Find doors", None)
        self.assertNotIn("call_batch", self.actions)
        self.assertEqual(self.env["session_state"].active_param, "filter_elements:Missing")


if __name__ == "__main__":
    unittest.main()
