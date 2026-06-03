---
name: debug-ui
description: Debug the HRM UI in a real browser via Playwright MCP — reproduce an issue, pull console errors + network logs + DOM state, and return a root-cause diagnosis. Use for "why is this page broken", failed API calls from the UI, auth/tenant routing bugs, or verifying a fix in the browser.
user_invocable: true
---

# Debug the UI in a Real Browser

Drives Chrome through the **Playwright MCP server** (via the `browser-debugger` sub-agent) to
investigate a UI problem and return an evidence-backed diagnosis. Read-only — it does not change
code or open PRs.

## Usage

```
/debug-ui login page throws a console error after submit
/debug-ui employees list is empty for tenant "acme" but the API returns 200
/debug-ui verify US-AUTH-006 RBAC — non-admin should not see the Admin nav item
/debug-ui http://localhost:4200/leave  attendance widget never loads
```

You can pass a free-text symptom, a URL, a tenant, and/or a story ID to verify.

## Prerequisites

1. **Playwright MCP must be connected.** It's configured in `.claude/settings.json`. If it was just
   added, **reload the VS Code window** (or restart Claude Code) and confirm with `/mcp` that
   `playwright` is connected. If no `mcp__playwright__*` tools are available, stop and tell the user
   to reload.
2. **The app must be running.** Default URLs:
   - Frontend: `http://localhost:4200` (`ng serve` in `src/frontend/`)
   - Backend: per `src/frontend/src/environments/` (typically `http://localhost:5000`)
   If unreachable, report it and offer to start the dev server — do not assume it's up.

## Process

1. **Parse the request** — extract URL (default `http://localhost:4200`), tenant, story ID, and the
   described symptom.
2. **Pre-flight** — quick reachability check of the target URL (read-only, e.g.
   `curl -s -o /dev/null -w "%{http_code}"`). Abort with a clear message if down.
3. **Delegate to `@browser-debugger`** — launch the sub-agent with the parsed context. It will:
   navigate, reproduce the steps, then collect **console messages**, **network requests**,
   **accessibility snapshot**, runtime state via `browser_evaluate`, and a screenshot if useful.
4. **If verifying a story** — read `user-stories/{module}/US-{ID}.md`, drive the relevant flow, and
   check each acceptance criterion against observed behavior.
5. **Return the diagnosis** in the agent's report format (Symptom → Reproduction → Evidence → Likely
   root cause → Suggested fix / next agent). Screenshots/traces are saved under
   `.playwright-artifacts/` (gitignored).
6. **Hand off** — if a code fix is needed, name the culprit and recommend `@frontend-dev` or
   `@backend-dev`. This skill itself never edits code.

## Notes

- **Multi-tenant:** always state which tenant was active for any auth/data finding (critical rule #1).
- **Secrets:** never print full JWTs/passwords — presence and claims only.
- **Cleanup:** the browser is closed at the end of the run.
- Pairs with the built-in `/verify` skill — use `/debug-ui` when the question is specifically about
  browser-level evidence (console/network/DOM).
