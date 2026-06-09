# Codex Workspace Notes

This folder adds Codex compatibility without changing the existing Claude Code setup.

Claude remains the source of truth for the established automation system:

- Project instructions: `../CLAUDE.md`
- Owner preferences: `../.claude/dev-instructions.md`
- Slash-command specs: `../.claude/commands/`
- Skill workflow specs: `../.claude/skills/`
- Agent role specs: `../.claude/agents/team/`

Codex wrappers in this folder tell Codex how to execute the same workflows with Codex sub-agents when they are available.

## Available Wrappers

- `commands/implement-all.md` - Codex version of `/implement-all`
- `commands/implement-story.md` - Codex version of `/implement-story`
- `commands/orchestrate.md` - Codex version of `/orchestrate`
- `commands/analyze-module.md` - Codex version of `/analyze-module`
- `commands/debug-ui.md` - Codex version of `/debug-ui`
- `commands/github-pipeline.md` - Codex version of `/github-pipeline`
- `agents/team/*.md` - role wrappers that point back to the Claude agent definitions
- `agents/team/db-engineer.md` - Codex-only persistence role derived from the backend rules

Keep these files small. If the workflow changes, update `.claude/` first, then adjust only the Codex-specific adaptation here.
