# Codex Command: Analyze Module

Claude source of truth:

- `.claude/skills/analyze-module.md`
- `.claude/agents/team/business-analyst.md`

Use this when the user asks Codex to run `/analyze-module`, `analyze-module`, or generate IEEE-compliant stories for a module.

## Codex Adaptation

1. Read the Claude source files above, `CLAUDE.md`, `.claude/dev-instructions.md`, and `docs/hrm_technical_document_v4.0.md`.
2. Resolve the requested module name using the module list in the Claude skill and `user-stories/STATUS.md`.
3. Generate or update IEEE 830 / ISO/IEC/IEEE 29148 user stories under `user-stories/{module}/`.
4. Update `user-stories/INDEX.md` and `user-stories/STATUS.md` only if the generated story set changes.
5. Preserve traceability to the technical document and module vault notes.
6. Do not touch application source code or test-case implementation files.
7. If using a sub-agent, give it the business analyst role spec and restrict it to the story documentation write scope.
