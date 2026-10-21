# Migrate BIM tool catalog to grouped JSON

## Goal and decisions

- Replace the XML/text `<BIM_TOOLS>` manifest with the grouped JSON catalog shape:

  ```json
  {
    "bim_tools": {
      "SEARCH": {
        "category_description": "...",
        "tools": [
          {
            "name": "filter_elements",
            "keywords": ["filter", "category", "class"],
            "description": "...",
            "parameters": {
              "type": "object",
              "properties": {},
              "required": []
            }
          }
        ]
      }
    }
  }
  ```
- `GetToolsJson()` is the sole runtime catalog source. `GetAiManual()`, `CachedAiManual`, the `get_manual` bridge action, and Python's `dynamic_manual` state are removed after their non-catalog instructions are checked and retained in `instructions.py` where not already covered.
- Build each group from `AiInfo.Group`; use the corresponding `AtomicGroupType` member's `AiParam.Description` for `category_description`. Do not repeat long category descriptions in every tool.
- Populate tool `name`, `description`, and `keywords` from `AiInfoAttribute`; preserve parameter descriptions and types from `AiParamAttribute` and the existing schema builder.
- Keep valid JSON Schema semantics: `required` is an array of property names, never a boolean. Derive top-level required fields from `AiParam.IsRequired`, with `Out` always required as the existing contract requires. For nested objects, preserve their own `required` arrays. Do not mark `In` required unless its command attribute does so.
- In the actual Function Calling wrapper, flatten grouped catalog entries into the existing `anyOf` variants. Add group name, tool description, and keywords to the `description` of each variant's `name` property; add each category description once to the wrapper function description. Keep the enum-constrained exact tool name and command-specific `arguments` schema.
- The supplied example is a structural target, not a request to encode non-standard schema fields such as `"required": true` inside a property.
- No filtering, retrieval, or caching strategy for a very large catalog is included in this migration.

## Ordered implementation steps

1. **Consolidate prompt rules.** Compare `Registry.GetAiManual()`'s execution protocol with `instructions.py` and preserve only useful rules not already present. Update stale prompt/schema references to `<AVAILABLE_BIM_TOOLS>` so command identity and arguments are described as coming from the Function Calling catalog/schema. Preserve runtime Revit tags in their existing user-context path.
2. **Generate grouped catalog JSON.** In `src/AtomixAI.Atomic/Registry.cs`, keep the existing property-schema builders, but group commands by `AiInfo.Group` and emit the agreed `bim_tools` root with category descriptions and each command's `keywords`, description, and `parameters` schema. Ensure serialization escapes attribute text and produces valid JSON Schema required arrays.
3. **Adapt orchestration.** In `src/AtomixAI.Main/Orchestrator/orchestrator.py`, update `build_bim_sequence_tool()` to iterate group entries, attach category descriptions once to the wrapper function description, and create one `anyOf` variant per command. Preserve command name enums and bind `arguments` to that command's `parameters`; put group name, description, and keywords in the `name` property's description. Update the base schema descriptions that refer to the removed text manifest.
4. **Remove the old manual path.** Remove the `get_manual` request from `process_ai_logic`, the `get_manual` action from `src/AtomixAI.Bridge/McpHost.cs`, `Registry.GetAiManual()` and `CachedAiManual`, plus obsolete `dynamic_manual` declarations/state and XML-building-only helpers/usings. Search for remaining consumers before removal; current repository search found no other production consumers.
5. **Update focused tests.** Update `tests/test_chip_context.py` fixtures to the grouped catalog shape and assert that category descriptions, tool description, group name, and keywords reach the Function Calling schema; assert command-specific arguments remain bound, enum names remain exact, and the old `get_manual` exchange is no longer issued. Add or adapt a focused check for grouped `GetToolsJson()` output and required-array semantics if the existing .NET test setup can host Registry tests.

## Validation

- Run `python -m unittest tests.test_chip_context` and `python -m py_compile "src/AtomixAI.Main/Orchestrator/orchestrator.py" "src/AtomixAI.Main/Orchestrator/instructions.py" "tests/test_chip_context.py"`.
- Build the affected Atomic and Bridge projects using the repository's supported Revit configuration; verify no schema-generation compile errors.
- Validate generated catalog JSON parses and has the agreed grouped shape. Check representative Search and Creation entries, categories with multiple commands, descriptions/keywords requiring JSON escaping, nested schemas, and that all required fields are arrays with `Out` included.
- Inspect the final Function Calling payload to confirm each `anyOf` branch has the right `name.enum`, descriptive `name.description`, and matching `arguments` schema; category descriptions appear once in the wrapper description rather than once per command.
- Search production code for `GetAiManual`, `CachedAiManual`, `get_manual`, `dynamic_manual`, and `<AVAILABLE_BIM_TOOLS>`; there should be no stale runtime path or prompt reference.

## Risks and boundaries

- The catalog becomes grouped JSON, but the provider-facing Function Calling schema remains the existing `execute_bim_sequence` with `anyOf`; the grouped catalog itself is not sent as a second prompt block.
- Function Calling providers can differ in how much they use descriptions on enum-constrained properties and wrapper descriptions. Validate the serialized request shape and, if available, a smoke request through the configured provider before relying on description-driven tool selection.
- Repeatedly sending all command schemas can still become costly as the catalog grows; catalog selection/retrieval is explicitly out of scope.
- Removing `get_manual` assumes there are no external consumers outside this repository; retain it only if a deployment boundary proves otherwise.
