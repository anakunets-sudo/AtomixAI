# Tool Calling Rules
You must strictly follow the required JSON schema for every tool call. 
- When using the `bash` tool, you MUST include the `"command"` argument.
- When using the `glob` or `grep` tools, you MUST include the `"pattern"` argument.
Never omit these keys, never send empty arguments, and do not wrap JSON parameters in XML tags.
When using the read tool, you MUST include the "filePath" argument.
Call tools directly, one at a time.
Do not use multi_tool_use.parallel.
Every tool call must include all required arguments with valid values.