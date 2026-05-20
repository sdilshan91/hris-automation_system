# Development Instructions (from Project Owner)

## Project Structure
- Frontend and backend are **separate codebases** in separate folders
  - `src/frontend/` — Angular standalone project
  - `src/backend/` — ASP.NET Core standalone solution

## Frontend Requirements
- **Angular 20** (latest, standalone components, signals)
- **UI Frameworks:** Tailwind CSS + Angular Material (NO Bootstrap)
- Must be **100% mobile responsive** (360px to 4K)
- **Rich UI** — modern, polished, Notion-like design aesthetic
  - Clean whitespace, subtle shadows, smooth animations/transitions
  - Minimalist but functional — inspired by Notion, Linear, Vercel dashboard style
  - Tenant-branded: logo + primary color customizable
- Use free/open-source plugins and libraries wherever available
- Use `ngx-bootstrap` only if a specific Bootstrap component is needed (e.g., carousel)

## Backend Requirements
- ASP.NET Core 10 Web API (Clean Architecture)
- **PostgreSQL** (localhost)
  - Host: `localhost`
  - Port: `5432`
  - Username: `developer`
  - Password: provided locally via `ConnectionStrings__DefaultConnection` env var or `dotnet user-secrets` (never commit the real password)
  - Database: `hris_dev_db`
- **Serilog** for structured logging
- **Polly** for resilience (retry, circuit-breaker, fallback)
- **Hangfire** for background jobs, scheduled tasks, notifications
  - Hangfire storage: PostgreSQL (`Hangfire.PostgreSql`)
- **Redis** for caching
  - Host: `localhost`
  - Port: `6379`
  - Setup: `docker run -d --name hris-redis -p 6379:6379 redis`
- Use free/open-source libraries wherever available

## Authentication (Local Dev)
- Admin login via **username + password** only for now
- Social logins (Google/Microsoft/Apple) deferred to later phase
- JWT + refresh token flow

## General Rules
- Prefer existing open-source plugins/libraries over custom implementations
- All libraries must be free and open-source
- Rich, modern UI with smooth animations and transitions

---

## Building stories with the automation loop

The repo ships with two skills that drive the story-by-story build pipeline. Source of truth: [`user-stories/STATUS.md`](../user-stories/STATUS.md).

### The daily loop

```
1. /implement-all auth        # picks the next [ ] story in authentication, builds it
2. (Claude opens PR, marks story [x] in STATUS.md, pushes status to main)
3. Review the PR in GitHub. Merge if happy.
4. /implement-all auth        # picks the NEXT pending story in authentication
5. Repeat until the module reports "all done", then move on:
   /implement-all core-hr
   /implement-all leave
   ...
```

One invocation = one user story = one branch = one PR. Rerun until the module is empty.

### Skill quick reference

| Command | What it does |
|---|---|
| `/implement-all` | Picks next `[ ]` story across ALL modules in priority order, builds + PRs + flips STATUS.md |
| `/implement-all {module}` | Same but scoped to one module (`auth`, `core-hr`, `leave`, ...) |
| `/implement-all US-AUTH-005` | Forces a specific story (overrides scope) |
| `/implement-story US-AUTH-005` | Same flow as above for ONE story, but does NOT touch STATUS.md (manual mode) |
| `/orchestrate` | Original full pipeline (BA → FE/BE/QA per module on per-agent branches). Use only for greenfield modules where stories don't exist yet. |
| `/analyze-module {name}` | Run business-analyst only — generate IEEE 830 stories for a new module |

### Hands-free batch mode (use with care)

If you want N stories built without reruns:

```
/loop /implement-all auth
```

The built-in `/loop` skill re-fires `/implement-all auth` after each completion and stops when the module reports done. Branches stack on `main` without waiting for PR merges — fine for independent stories, problematic if story N+1 depends on N's code being merged. Prefer the manual rerun loop while you're still tuning agent prompts.

### What the loop produces per story

- Branch: `feature/US-{MODULE}-{NNN}` (e.g. `feature/US-AUTH-005`)
- Commit: `feat(US-AUTH-005): Multi-factor authentication (TOTP)` (single squash-ready commit with BE + FE + QA)
- PR title: `feat: US-AUTH-005 — Multi-factor authentication (TOTP)`
- PR body: AC checklist + TC IDs added + build/test results
- STATUS.md update on `main`: `[ ]` → `[x]` for that story, tally counters updated

### State recovery

Lost the session? Just run `/implement-all {module}` again — STATUS.md is the only state. The skill reads the first `[ ]` and continues. Hand-edit STATUS.md to:
- skip a story → change `[ ]` to `[s] skipped: <reason>`
- redo a story → change `[x]` to `[ ]`
- mark an already-built story done → change `[ ]` to `[x]`
