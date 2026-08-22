# MCP servers — setup and operational detail

> Extracted from CLAUDE.md on 2026-08-23. Activation steps, capability flags and the plugin-collision
> history are operational reference, needed when configuring MCP — not on every request. The summary
> table in CLAUDE.md stays authoritative for *which* servers exist and *who* uses them.

### GitHub MCP Server
Connected via `https://api.githubcopilot.com/mcp/` (defined in `.mcp.json`)

Enables agents to directly:
- Create feature branches per agent per module
- Push code directly to branches
- Open PRs with story/test references
- Create GitHub Issues for tracking and integration review

**Setup:** `GITHUB_TOKEN` env var from `.env` file (PAT with `repo`, `workflow`, `issues`, `pull_requests` scopes)

### Playwright MCP Server (Browser Debugging)
Local stdio server (`npx @playwright/mcp@latest`) that gives agents a **real Chrome browser** for
runtime investigation of the Angular UI and its calls to the .NET API. Defined in `.mcp.json`
with `--browser chrome --caps vision,pdf,devtools --save-session --output-dir .playwright-artifacts`.
(Note: `--save-session`, not the older `--save-trace`, which current `@playwright/mcp` rejects and
which crashes the server on launch.)

Enables agents to:
- Navigate the running app and reproduce user flows (click, type, fill forms)
- Read **browser console** messages — JS/Angular errors (`browser_console_messages`)
- Inspect **network requests** — status, headers, payloads, CORS (`browser_network_requests`)
- Capture the accessibility snapshot, run page JS (`browser_evaluate`), take screenshots
- Diagnose auth / **multi-tenant** routing issues from real traffic

**Activation:** the server connects at Claude Code session startup from `.mcp.json`. After first
adding/changing it, **fully restart the Claude Code session** (a plain VS Code "Reload Window" on an
already-running session may not reconnect) and **approve the project-MCP trust prompt**, then confirm
`playwright` is connected (e.g. via `/mcp` where available). Artifacts (session/screenshots) save to
`.playwright-artifacts/` (gitignored). It is **read-only on the codebase** — used to investigate, not
to edit code. Driven by the `@browser-debugger` agent and the `/debug-ui` skill.

### Microsoft Learn MCP Server (official .NET/Azure docs)
Remote HTTP server (`https://learn.microsoft.com/api/mcp`, defined in `.mcp.json`) giving agents grounded
access to official Microsoft documentation instead of recalling it: `microsoft_docs_search` (breadth),
`microsoft_code_sample_search` (working snippets), `microsoft_docs_fetch` (full page depth). Used by
`@principal-advisor` for the `/advisor` dependency-currency pass and available to `@backend-dev` for
.NET 10 / EF Core / ASP.NET Core APIs that post-date the model's training data.

> **Note — three plugins were UNINSTALLED (2026-08-22) for colliding with this file.** The official
> `github`, `playwright` and `microsoft-docs` plugins each ship a `.mcp.json` declaring a server with the
> **same name** as ours but a **worse config**: `github` reads `${GITHUB_PERSONAL_ACCESS_TOKEN}` (we use
> `${GITHUB_TOKEN}`), and `playwright` is a bare `npx @playwright/mcp@latest` with **no**
> `--caps vision,pdf,devtools`, **no** `--save-session` and **no** `--output-dir`. Had the plugin's
> definition ever won resolution, `@browser-debugger` would have silently lost vision/PDF/devtools and
> stopped writing `.playwright-artifacts/` — presenting as *"the tool can't do that"*, not as a config
> error. **`.mcp.json` is the single source for MCP servers here**; it is committed, so the team and CI
> get the tuned flags. Do not re-install those three.

### Chrome DevTools MCP Server (performance / Lighthouse / memory)
Local stdio server (`npx chrome-devtools-mcp@latest --isolated`, defined in `.mcp.json`) that exposes the
**Chrome DevTools Protocol** — the front-end performance + audit layer Playwright doesn't cover. It launches
its **own isolated Chrome** (separate from the Playwright instance). Enables agents to:
- Run a one-shot **`lighthouse_audit`** (performance + accessibility + best-practices) — a second a11y signal alongside `@axe-core/playwright`.
- Record runtime **performance traces** (`performance_start_trace` → `performance_stop_trace` → `performance_analyze_insight`) for Core Web Vitals (LCP/CLS/TBT).
- **Throttle** CPU / network (`emulate`) to reproduce slow conditions; **`take_heapsnapshot`** for memory leaks.
- Inspect network/console under CDP (`list_network_requests`, `get_network_request`, `list_console_messages`).

**Division of labour:** **Playwright MCP** = functional UI, a11y (axe), cross-browser, DOM/console/network;
**Chrome DevTools MCP** = *why is it slow / leaking / failing a Lighthouse audit*; **k6** = server/load perf.
Same activation rules as Playwright (attaches at session startup from `.mcp.json`; needs a full restart +
trust-prompt approval). Used by `@test-runner` (perf/a11y TC execution) and `@browser-debugger` (deep
diagnosis). **Read-only on the codebase.**

