# Refinement and Task Switching Plan

## Goal and decisions

Handle a new BIM request received while the assistant is waiting for a missing parameter, without merging it into the previous request or counting it as flood. Keep Python responsible for state transitions and Revit execution; use the model to classify the latest user turn. A new task replaces the unfinished task; resume/suspension is out of scope.

Flood policy:
- Explicit off-topic input during an active clarification lock increments the existing flood counter and retains its configured eviction threshold.
- Relevant but incomplete answers do not increment the flood counter.
- A new BIM task resets task-local context and starts a fresh clarification cycle; it is not flood.
- Explicit cancellation clears the active task without the flood eviction response.

## Implementation steps

1. Extend the `execute_bim_sequence` output schema in `orchestrator.py` with a required, constrained intent classification for the latest turn: `refinement`, `new_task`, `off_topic`, or `abort_task`. Preserve existing sequence, active-context, and user-facing fields. Update prompt rules in `instructions.py` to classify relative to the active lock, use only the latest turn to detect a task switch, and never treat a valid new BIM task as off-topic. Keep the existing off-topic example narrowly scoped and add a contrasting task-switch example.

2. Refactor `ContextStateManager` to expose an explicit task-replacement transition that clears prior `user_steps`, replaces the active lock, and resets the flood counter. Do not retain or resume the abandoned task. Ensure the current request payload still includes the earlier conversation needed for classification, but subsequent model calls contain only the new task's text and refinements.

3. Apply intent-specific transitions in `process_ai_logic` before existing lock/flood handling:
   - `abort_task`: clear task state and return a concise cancellation acknowledgement without invoking eviction or Revit.
   - `new_task`: replace task state using the latest turn; execute a complete sequence in this turn, or retain only the new task and its missing-parameter lock when incomplete.
   - `refinement`: keep the existing task history; do not increment flood for a relevant but incomplete response. Preserve a bounded flood safeguard for repeated unresolved refinements if needed, but do not classify them as off-topic.
   - `off_topic`: only count as flood while a lock is active; preserve current threshold and eviction behavior.

4. Add defensive validation around the model classification. Unknown/missing intent must not clear a lock or authorize Revit execution; use a conservative fallback that preserves current task state and asks for clarification. Ensure only a complete, validated sequence reaches `call_batch`, and that a new-task intent with an empty sequence cannot accidentally execute stale sequence data.

5. Update `tests/test_chip_context.py` or add focused Python tests following its AST-isolation/mocking pattern. Cover: an incomplete new task replaces old `user_steps`; the next refinement contains only the new task; a complete new task executes despite the old lock; a relevant incomplete refinement does not increment flood; off-topic increments and evicts only at the existing threshold; abort clears state without eviction; malformed intent cannot execute a sequence.

## Boundaries and risks

- Scope is limited to the orchestrator's clarification state and its prompt/examples. No UI, Revit command, provider, history persistence, or suspended-task changes.
- Model intent is not trusted as authorization to execute tools. Validate sequence presence and existing schema/catalog constraints in Python before dispatch.
- Prompt examples can bias classification; tests should assert Python transitions independently of live model behavior.
- Preserve cancellation-generation behavior and existing chip context for the latest request; do not let old-task chips leak into the replacement task's later refinements.

## Validation

- Run `python -m unittest tests.test_chip_context` (or the repository-compatible invocation if `tests` is not a package).
- Run the full available Python test suite.
- Manually inspect mocked request traces for: old lock → incomplete new task → refinement → complete new task, and old lock → explicit off-topic through eviction.
- Verify no sequence is sent to Revit for `abort_task`, off-topic, or incomplete new tasks, and a complete new task is sent exactly once.
