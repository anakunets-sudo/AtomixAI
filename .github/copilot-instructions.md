# Copilot Instructions

## General Guidelines
- First general instruction
- Second general instruction

## Code Style
- Use specific formatting rules
- Follow naming conventions

## Project-Specific Rules
- Custom requirement A
- Custom requirement B

## LLM Interaction
- The Python orchestrator must send command results back to the LLM for analysis and to generate an informative response to the user.
- Maintain a history of messages (messages[]) and make two requests: first for an action plan, then for analysis of the command execution results.