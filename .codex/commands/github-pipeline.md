# Codex Command: GitHub Pipeline

Claude source of truth:

- `.claude/skills/github-pipeline.md`

Use this when the user asks Codex to run `/github-pipeline` or trigger the remote Claude GitHub Actions pipeline.

## Codex Adaptation

1. Read `.claude/skills/github-pipeline.md`.
2. Confirm the target module.
3. Warn that this path expects the GitHub workflow and `ANTHROPIC_API_KEY` secret to be configured.
4. Trigger the workflow with `gh workflow run claude-agent-pipeline.yml --field agent=orchestrate --field module={module-name}`.
5. Monitor with `gh run list --workflow=claude-agent-pipeline.yml --limit 1`.
6. Report workflow status and PR links if available.

Use local `.codex/commands/orchestrate.md` instead when the user wants the no-API-credit local workflow.
