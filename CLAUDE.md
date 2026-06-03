# HRM SaaS Automation System

## Project Overview
Multi-tenant HRM SaaS platform built with **Angular 20 + ASP.NET Core 10 + PostgreSQL**.
Reference: `docs/hrm_technical_document_v4.0.md`
Repo: `sdilshan91/hris-automation_system`

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

## Agent Team

| Agent | Role | Branch | MCP Tools |
|-------|------|--------|-----------|
| `@business-analyst` | Analyzes docs → IEEE 830 user stories | `feature/user-stories-{module}` | create_issue, create_branch, push_files, create_pull_request |
| `@frontend-dev` | Implements Angular 20 UI | `feature/frontend-{module}` | create_branch, push_files, create_pull_request |
| `@backend-dev` | Implements ASP.NET Core 10 API | `feature/backend-{module}` | create_branch, push_files, create_pull_request |
| `@qa-engineer` | Writes IEEE 829 test cases | `feature/qa-{module}` | create_branch, push_files, create_pull_request, create_issue |

## Skills (Slash Commands)

| Command | Mode | Description |
|---------|------|-------------|
| `/orchestrate` | Local + MCP | Full pipeline: BA → (FE + BE + QA in parallel via worktrees) |
| `/analyze-module {name}` | Local + MCP | Generate user stories for a specific module |
| `/implement-story US-{ID}` | Local + MCP | Implement a story with all agents in parallel |
| `/github-pipeline {module}` | GitHub Actions | Trigger remote pipeline (needs API credits) |

## Automation Hooks

| Hook | Trigger | Action |
|------|---------|--------|
| `post-user-story-commit` | User story files committed | Notifies dev + QA agents to start |
| `post-dev-commit` | Frontend/backend code committed | Notifies QA to review test cases |

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
│   │   └── qa-engineer.md
│   ├── skills/                    # Slash command skills
│   │   ├── orchestrate.md         # Local + MCP pipeline
│   │   ├── analyze-module.md
│   │   ├── implement-story.md
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
