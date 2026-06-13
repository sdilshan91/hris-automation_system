# HRM SaaS Automation System

## Project Overview
Multi-tenant HRM SaaS platform built with **Angular 20 + ASP.NET Core 10 + PostgreSQL**.
Reference: `docs/hrm_technical_document_v4.0.md`
Repo: `sdilshan91/hris-automation_system`

## Engineering Discipline (how every agent should work)

These behavioral rules apply to **all** agents and skills, in addition to the
project rules below. They exist to cut wasted diff, rework, and late surprises.

1. **Think before coding.** Don't assume — surface tradeoffs and ask when a
   requirement is genuinely ambiguous. If there are multiple reasonable
   interpretations, name them instead of silently picking one. Don't hide
   confusion; a question before implementation is cheaper than a rewrite after.
2. **Simplicity first.** Write the minimal code that solves the stated problem.
   No speculative abstractions, unrequested flexibility, or error handling for
   impossible cases. Self-check: *would a senior engineer call this overcomplicated?*
3. **Surgical changes.** Touch only what the task requires; match adjacent style.
   Clean up only the mess **your** change created (e.g. imports/vars *your* edit
   orphaned) — don't refactor pre-existing dead code as a side quest. When a change
   forces you to touch files outside the task's scope, **flag it explicitly** rather
   than burying it. *Carve-out:* the `/implement-all` **remediation loop** is allowed
   to edit sibling tests/contracts when a verified failure demands it — but it still
   may never weaken, skip, or delete a test to go green.
4. **Goal-driven execution.** Turn each task into verifiable success criteria
   (build passes, tests green, AC met) before starting; for multi-step work keep a
   short checkpointed plan. Strong criteria are what let the agent loop unattended;
   "make it work" is not a success criterion.

## Advisor Stance (how to talk to the user)

The user wants a **candid advisor, not an agreeable assistant** — pushback over
comfort. These rules govern *communication and recommendations*, not task execution
(an implementation sub-agent still just builds the story; it applies this when
reporting risks, not by narrating confidence on every line).

- **Lead with the truth, including the uncomfortable part.** If a request rests on a
  bad idea, say so up front — don't bury it after praise.
- **Challenge assumptions.** Name a flawed premise instead of silently executing it;
  surface the tradeoff the user didn't ask about.
- **Rate confidence on non-obvious claims** (e.g. *Confidence: 75%*) so the user can
  calibrate how far to trust them.
- **Say when the user is wrong** — with the reason and evidence, not just the verdict.
- **No empty validation.** Cut "You're absolutely right", "Great question", and
  reflexive agreement. Agree only after checking, and then say *why*, briefly.
- **Honesty over contrarianism.** Do NOT manufacture disagreement to look critical —
  that is just inverse sycophancy. When the user is right, say so plainly and move on.
  The goal is an accurate signal, not a negative one.

## Execution Modes

| Mode | Command | Requires | Best For |
|------|---------|----------|----------|
| **Local + MCP** | `/orchestrate` | Claude Code + GITHUB_TOKEN | Day-to-day development (no API credits needed) |
| **GitHub Actions** | `/github-pipeline` | ANTHROPIC_API_KEY secret + credits | Fully autonomous CI/CD |

**Recommended:** Use **Local + MCP** mode. Agents run in your Claude Code session and push to GitHub via MCP server. No Anthropic API credits needed.

## MCP Server Integration

### GitHub MCP Server
Connected via `https://api.githubcopilot.com/mcp/`

Enables agents to directly:
- Create feature branches per agent per module
- Push code directly to branches
- Open PRs with story/test references
- Create GitHub Issues for tracking and integration review

**Setup:** `GITHUB_TOKEN` env var from `.env` file (PAT with `repo`, `workflow`, `issues`, `pull_requests` scopes)

### Playwright MCP Server (Browser Debugging)
Local stdio server (`npx @playwright/mcp@latest`) that gives agents a **real Chrome browser** for
runtime investigation of the Angular UI and its calls to the .NET API. Configured in
`.claude/settings.json` with `--browser chrome --caps vision,pdf,devtools --save-trace
--output-dir .playwright-artifacts`.

Enables agents to:
- Navigate the running app and reproduce user flows (click, type, fill forms)
- Read **browser console** messages — JS/Angular errors (`browser_console_messages`)
- Inspect **network requests** — status, headers, payloads, CORS (`browser_network_requests`)
- Capture the accessibility snapshot, run page JS (`browser_evaluate`), take screenshots
- Diagnose auth / **multi-tenant** routing issues from real traffic

**Activation:** the server connects at Claude Code startup. After first adding/changing it,
**reload the VS Code window** and confirm with `/mcp` that `playwright` is connected. Artifacts
(traces/screenshots) save to `.playwright-artifacts/` (gitignored). It is **read-only on the
codebase** — used to investigate, not to edit code. Driven by the `@browser-debugger` agent and the
`/debug-ui` skill.

## Agent Team

| Agent | Role | Branch | MCP Tools |
|-------|------|--------|-----------|
| `@business-analyst` | Analyzes docs → IEEE 830 user stories | `feature/user-stories-{module}` | create_issue, create_branch, push_files, create_pull_request |
| `@frontend-dev` | Implements Angular 20 UI | `feature/frontend-{module}` | create_branch, push_files, create_pull_request |
| `@backend-dev` | Implements ASP.NET Core 10 API | `feature/backend-{module}` | create_branch, push_files, create_pull_request |
| `@qa-engineer` | Writes IEEE 829 test cases | `feature/qa-{module}` | create_branch, push_files, create_pull_request, create_issue |
| `@browser-debugger` | Drives Chrome to debug UI (console, network, DOM) — read-only investigator | _(no branch — diagnoses only)_ | playwright (navigate, console_messages, network_requests, snapshot, evaluate, screenshot, interactions) |

## Skills (Slash Commands)

| Command | Mode | Description |
|---------|------|-------------|
| `/implement-all [module\|US-ID]` | Local + MCP | **Loop driver.** Picks the next pending story from `user-stories/STATUS.md`, builds it end-to-end (BE + FE + QA in parallel), runs the full verify gate with an autonomous remediation loop, then commits + opens a PR. One story per call; rerun (or `/loop`) to continue. See below. |
| `/orchestrate` | Local + MCP | Full pipeline: BA → (FE + BE + QA in parallel via worktrees) |
| `/analyze-module {name}` | Local + MCP | Generate user stories for a specific module |
| `/research-story US-{ID}` | Local + MCP | **Feasibility gate (RPI-style).** Read-only: reads ONE story + codebase + vault and writes `research/US-{ID}.md` with a GO / GO-WITH-CONDITIONS / NO-GO verdict. Run before implementing a large/risky/unclear story. |
| `/implement-story US-{ID}` | Local + MCP | Implement ONE specific story end-to-end (manual single-shot; does NOT touch STATUS.md) |
| `/security-audit [scope]` | Local + MCP | **HRM security gate.** Reviews a diff (branch/US-ID/path) against this platform's threat model — tenant isolation, authz, injection, secrets, PII — and writes `security-reviews/{scope}.md` with severity-by-exploitability findings + fixes. Read-only; run before opening a PR. `--deep` fans out parallel reviewers. |
| `/debug-ui {symptom\|URL}` | Local + MCP (Playwright) | Debug the running UI in a real browser — console + network + DOM diagnosis via `@browser-debugger` |
| `/github-pipeline {module}` | GitHub Actions | Trigger remote pipeline (needs API credits) |

### `/implement-all` — autonomous story loop

Source of truth: [.claude/skills/implement-all.md](.claude/skills/implement-all.md). Per story it:

1. Picks the first `[ ]` story in `user-stories/STATUS.md` (scoped by module/ID arg, else priority order), marks it `[~]`, and cuts `feature/US-{MODULE}-{NNN}` from fresh `main`.
2. Runs `@backend-dev` (incl. DB/EF/migrations), `@frontend-dev`, and `@qa-engineer` **in parallel** on non-overlapping paths; sub-agents do **not** commit.
3. **Verify gate:** `dotnet build` → `dotnet test` → `npm run build` → `ng test` (headless). Any failure enters the **remediation loop** — up to 3 attempts that hand the verbatim errors to the owning dev agent and re-run the whole gate. It may **never** weaken/skip a test to go green; if it can't fix cleanly in 3 attempts it reverts the story to `[ ]` and stops without a PR.
4. On green: commits `feat(US-XXX)`, pushes, opens a PR, flips STATUS.md `[~]`→`[x]` on `main`.

Run continuously with `/loop /implement-all [scope]` — it re-fires until the scope reports "all done." Requires a **clean working tree on `main`**; only run unattended when you're willing to review the stacked PRs after the fact (they are opened, not auto-merged).

## Automation Hooks

| Hook | Trigger | Action |
|------|---------|--------|
| `post-user-story-commit` | User story files committed | Notifies dev + QA agents to start |
| `post-dev-commit` | Frontend/backend code committed | Notifies QA to review test cases |
| `sound notifications` | `Stop`, `Notification`, `PermissionRequest`, `SubagentStop` | Plays a short sound via `python .claude/hooks/scripts/hooks.py` so you know when a long `/implement-all` run finishes or needs you. Toggle per-hook in `.claude/hooks/config/hooks-config.json` (or git-ignored `…local.json`); disable all via `disableAllHooks` in `settings.local.json`. Needs Python 3. |

## Pipeline Flow (Local + MCP)

```
                      LOCAL (Claude Code)              GITHUB (via MCP)
                      ──────────────────               ────────────────
[docs/]
   │
   ▼
@business-analyst ─────────────────────── MCP ──► branch: feature/user-stories-{module}
   │  (writes user-stories/)                      PR: "IEEE 830 stories for {module}"
   │                                              Issues: epic per module
   │
   ├── Stage 2 (parallel via git worktrees) ──┐
   │                                          │
   ▼                 ▼                        ▼
@frontend-dev   @backend-dev           @qa-engineer
   │                 │                        │
   MCP               MCP                     MCP
   ▼                 ▼                        ▼
branch + PR      branch + PR             branch + PR
(Angular 20)     (.NET Core 10)          (test cases)
   │                 │                        │
   └─────────────────┼────────────────────────┘
                     ▼
           GitHub: Integration Review Issue
```

## Branch Strategy

```
main
├── feature/user-stories-{module}   ← @business-analyst
├── feature/frontend-{module}       ← @frontend-dev (worktree)
├── feature/backend-{module}        ← @backend-dev  (worktree)
└── feature/qa-{module}             ← @qa-engineer  (worktree)
```

## Directory Structure

```
├── .env                           # API keys (gitignored, local only)
├── .env.example                   # Template for .env
├── .gitignore
├── docs/                          # Technical documentation (source of truth)
│   └── vault/                     # Obsidian vault — shared agent memory (see Shared Memory section)
├── user-stories/                  # IEEE 830 user stories (by module)
│   ├── {module-name}/
│   │   └── US-{MOD}-001.md
│   └── INDEX.md
├── test-cases/                    # IEEE 829 test cases (by module)
│   ├── {module-name}/
│   │   ├── TC-{MOD}-001.md
│   │   └── TEST-MATRIX.md
│   ├── TRACEABILITY-MATRIX.md
│   └── TEST-PLAN.md
├── src/
│   ├── frontend/                  # Angular 20 SPA
│   └── backend/                   # ASP.NET Core 10 API
├── .claude/
│   ├── agents/team/               # Agent definitions (with MCP tools)
│   │   ├── business-analyst.md
│   │   ├── frontend-dev.md
│   │   ├── backend-dev.md
│   │   ├── qa-engineer.md
│   │   └── browser-debugger.md    # Playwright-driven UI debugger (read-only)
│   ├── skills/                    # Slash command skills
│   │   ├── orchestrate.md         # Local + MCP pipeline
│   │   ├── analyze-module.md
│   │   ├── implement-story.md
│   │   ├── debug-ui.md            # Browser debugging via Playwright MCP
│   │   └── github-pipeline.md     # Remote pipeline (needs credits)
│   ├── hooks/                     # Automation hooks
│   │   ├── post-user-story-commit.sh
│   │   └── post-dev-commit.sh
│   └── settings.json              # MCP servers, hooks, permissions
└── .github/
    └── workflows/
        └── claude-agent-pipeline.yml  # GitHub Actions (future, needs credits)
```

## Shared Memory (Obsidian Vault)

All agents share a persistent markdown knowledge base at `docs/vault/`. Open as an Obsidian vault for the human view; agents read/write the `.md` files directly. Start at [docs/vault/Home.md](docs/vault/Home.md) and follow conventions in [docs/vault/README.md](docs/vault/README.md).

| Folder | Use it for |
|---|---|
| `docs/vault/agents/{agent}.md` | Per-agent persistent notes (preferences, gotchas, working patterns) |
| `docs/vault/modules/{module}.md` | Domain rules, edge cases, why-decisions per module |
| `docs/vault/decisions/` | ADR-lite architecture/design decisions |
| `docs/vault/handoffs/` | Short-lived context drops between agents in a pipeline run |
| `docs/vault/incidents/` | Bug/incident post-mortems |

**Agent contract:**
- Before starting work on a module, check `docs/vault/modules/{module}.md` and your own `docs/vault/agents/{agent}.md` for prior context.
- When you make a non-obvious decision or learn a domain rule worth keeping, write it to the appropriate vault folder (not into the code as a comment).
- When handing off to another agent in the same run, drop a note in `docs/vault/handoffs/` with frontmatter `from:` and `to:`.
- Use Obsidian wiki links `[[note-name]]` between vault notes so backlinks work.
- Never put secrets, generated logs, or transient task state in the vault.

### Vault vs. built-in agent memory

There are **two** distinct memory stores — keep them separate so knowledge doesn't fragment:

| Store | What it is | Use for |
|---|---|---|
| **Obsidian vault** (`docs/vault/`) | Manual, **shared**, human-browsable. The cross-agent source of truth. | Domain rules, ADRs, handoffs, anything another agent/human should read. |
| **Built-in agent memory** (`.claude/agent-memory/{agent}/`) | Auto-loaded each run via `memory: project` in agent frontmatter. **Private to that one agent.** | An agent's own operational notes ("tried X, it failed", recurring gotchas) it wants auto-recalled next run. |

Rule of thumb: if it's worth sharing, it goes in the **vault**; if it's just one agent's working memory, the built-in store is fine. Never duplicate the same fact into both. Secrets/logs go in neither.

## Critical Rules
1. **Tenant isolation is non-negotiable** — every query, cache key, and API call must be tenant-scoped
2. **IEEE standards** — user stories follow IEEE 830, test cases follow IEEE 829
3. **Parallel execution** — dev agents and QA agent run simultaneously via git worktrees
4. **Traceability** — every test case must link back to a user story and acceptance criteria
5. **MCP-first** — prefer GitHub MCP tools over manual git commands for branch/PR/issue operations
6. **Secrets in .env only** — never hardcode tokens, always use `${ENV_VAR}` references

## Module Priority
1. Authentication & Authorization
2. Core HR (Employees, Departments, Org Tree)
3. Leave Management
4. Attendance
5. Recruitment
6. Payroll
7. Performance Management
8. Admin Console (System + Tenant)
9. Onboarding/Offboarding
10. Training & Benefits
11. Reports & Analytics
12. Notifications & Audit

---

# Application Development

> The sections above describe the **agent-orchestration meta-system**. The sections below describe the **actual HRM application** in `src/` — how to build, run, and test it, and how its architecture fits together.

## Commands

### Backend (`src/backend`, .NET 10)
```bash
dotnet restore HRM.sln
dotnet build HRM.sln
dotnet run --project HRM.Api          # serves API + Swagger UI at /swagger, Hangfire dashboard at /hangfire (dev only)

# EF Core migrations (run from src/backend; --startup-project supplies config/connection string)
dotnet ef migrations add <Name> --project HRM.Infrastructure --startup-project HRM.Api
dotnet ef database update --project HRM.Infrastructure --startup-project HRM.Api
```
Migrations are **applied automatically on startup** via `DbInitializer.RunAsync` (`Program.cs`), which also seeds a default admin tenant, roles, and admin user. There is currently no backend test project.

### Frontend (`src/frontend`, Angular 20)
```bash
npm install
npm start            # ng serve — dev server
npm run build        # ng build
npm test             # ng test — Karma + Jasmine (single project, headful Chrome)
npm run lint         # ng lint
ng test --include='**/auth.service.spec.ts'   # run a single spec
```

## Local Configuration (required to run)
`appsettings.json` ships with **blank secrets** — the app will not start until these are set, ideally via .NET user-secrets (`UserSecretsId` is already in `HRM.Api.csproj`), not by editing the committed file:
- `ConnectionStrings:DefaultConnection` — PostgreSQL (`Password` is empty in the template)
- `Jwt:PrivateKey` — signing key for JWT validation
- A running **PostgreSQL** instance (also backs Hangfire job storage)

## Architecture

### Backend: Clean Architecture + CQRS
Four projects, dependencies point inward (`Api → Application → Domain`; `Infrastructure → Application`):
- **HRM.Domain** — entities, value objects (e.g. `Email`), repository interfaces. No framework dependencies.
- **HRM.Application** — CQRS handlers organized by feature (`Features/{Feature}/Commands|Queries|DTOs|Validators`), MediatR pipeline behaviors (`ValidationBehavior`, `LoggingBehavior`), and `Common/Interfaces` abstractions (`ITenantContext`, `ICurrentUser`, `IJwtService`, `IAuthService`).
- **HRM.Infrastructure** — EF Core `AppDbContext`, entity configurations, interceptors, and interface implementations. Wired up in `DependencyInjection.AddInfrastructure`.
- **HRM.Api** — controllers (thin; dispatch via MediatR), middleware, filters, Hangfire jobs. Composition root is `Program.cs`.

Request flow: validation runs both via the MVC `ValidationFilter` and the MediatR `ValidationBehavior`; `ExceptionHandlingMiddleware` is the outermost layer and normalizes errors.

### Multi-Tenancy (the central architectural concern)
Tenant isolation is enforced in **three coordinated layers** — when adding entities or queries, all three matter:
1. **Resolution** (`TenantResolutionMiddleware`, runs before auth): extracts tenant from the request **subdomain** (`acme.yourhrm.com` → `acme`; `admin.*` → system context; reserved subdomains skip resolution). Looks up the tenant and populates the scoped `ITenantContext`. Dev fallback: the SPA sends an `X-Tenant-Subdomain` header (set by the frontend `tenantInterceptor`) so `*.localhost` hosts-file entries aren't needed.
2. **Write isolation** (`TenantInterceptor`, a `SaveChanges` interceptor): auto-stamps `TenantId` on any new `BaseEntity` when a tenant is resolved.
3. **Read isolation** (global query filters in `AppDbContext.OnModelCreating`): every tenant-scoped entity is filtered by `TenantId == _tenantContext.TenantId`. Use `IgnoreQueryFilters()` only deliberately (e.g. the tenant lookup in the resolution middleware itself).

`AuditInterceptor` similarly stamps audit fields. EF uses PostgreSQL with **snake_case** naming convention (`EFCore.NamingConventions`).

### Cross-cutting backend infrastructure
- **Auth**: JWT bearer; `JwtService` is registered as a singleton and also supplies `TokenValidationParameters`. BCrypt for password hashing. Refresh tokens are cleaned up by the `TokenCleanupJob` Hangfire recurring job (daily).
- **Background jobs**: Hangfire on PostgreSQL storage; dashboard at `/hangfire` (dev only).
- **Resilience**: a named `ResilientClient` HttpClient with Polly retry + circuit-breaker for outbound calls.
- **Logging**: Serilog; `TenantId`/`TenantSubdomain` are pushed into the log context per request.

### Frontend: standalone Angular 20
- `core/` holds singletons: `auth/` (service, guard, interceptor, models), `interceptors/` (`error`, `tenant`), `tenant/` (subdomain resolution mirroring the backend rules, using signals).
- `features/` holds route-lazy feature components (e.g. `auth/login`, `dashboard`); `layouts/` holds `auth-layout` / `main-layout`.
- HTTP interceptors are functional (`HttpInterceptorFn`). The `tenantInterceptor` injects `X-Tenant-Subdomain` from `environment.tenantSubdomain` for local dev.
- UI stack: Angular Material + Tailwind CSS, ngx-translate (i18n), ngx-toastr (notifications).

### Traceability convention
Code, user stories (`user-stories/`, IEEE 830), and test cases (`test-cases/`, IEEE 829) are cross-referenced by ID — e.g. `US-AUTH-007` appears in both `TenantService` comments and `test-cases/authentication/`. Preserve these references when modifying related code.
