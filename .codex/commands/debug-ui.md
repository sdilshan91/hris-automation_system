# Codex Command: Debug UI

Claude source of truth:

- `.claude/skills/debug-ui.md`
- `.claude/agents/team/browser-debugger.md`

Use this when the user asks Codex to run `/debug-ui`, debug the running UI in a browser, inspect console/network behavior, or verify a UI story with browser evidence.

## Codex Adaptation

1. Read the Claude source files above.
2. Parse the user request for URL, tenant, story ID, expected role, and symptom.
3. Confirm the app is reachable before browser investigation.
   - Frontend default: `http://localhost:4200`
   - Backend default: check `src/frontend/src/environments/`
4. Use browser or Playwright tools when available. If unavailable, report that browser-level evidence cannot be collected in this session.
5. Keep the workflow read-only:
   - Do not edit code.
   - Do not run git commands.
   - Do not open PRs.
6. Collect and report:
   - Symptom
   - Reproduction steps
   - Console evidence
   - Network evidence
   - DOM or accessibility observations
   - Active tenant context
   - Likely root cause
   - Recommended next agent or fix area
7. Never print full tokens, passwords, or secrets.
