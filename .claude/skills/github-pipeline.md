---
name: github-pipeline
description: Trigger the full agent pipeline via GitHub Actions (requires Anthropic API credits)
user_invocable: true
---

# GitHub Actions Pipeline Trigger (Remote Execution)

> **Requires:** Anthropic API credits configured as `ANTHROPIC_API_KEY` GitHub secret.
> If you don't have credits, use `/orchestrate` instead (runs locally).

Trigger the full BA > Dev > QA agent pipeline via GitHub Actions.

## Usage
```
claude /github-pipeline {module-name}
```

## Process
1. Use GitHub MCP to trigger the `claude-agent-pipeline` workflow
2. Pass the module name as input
3. Monitor the workflow run status
4. Report back with PR links when complete

## Implementation

When invoked, execute:

```bash
gh workflow run claude-agent-pipeline.yml \
  --field agent=orchestrate \
  --field module={module-name}
```

Then monitor with:
```bash
gh run list --workflow=claude-agent-pipeline.yml --limit 1
```

## Branch Strategy
```
main
├── feature/user-stories-{module}   ← BA agent
├── feature/frontend-{module}       ← Frontend agent
├── feature/backend-{module}        ← Backend agent
└── feature/qa-{module}             ← QA agent
```
