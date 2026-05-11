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
