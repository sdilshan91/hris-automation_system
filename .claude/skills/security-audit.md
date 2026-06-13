---
name: security-audit
description: HRM-specific security review of a code change. Audits a diff against this platform's real threat model — multi-tenant isolation, authorization, injection, secrets, and PII — and reports findings with severity-by-exploitability and concrete fixes. Use before opening a PR, when reviewing a branch/PR, or for a pre-release security pass. Complements the generic built-in /security-review with checks tuned to ASP.NET Core 10 + EF Core + Angular 20 + PostgreSQL.
user_invocable: true
---

# Security Audit (HRM SaaS)

A **project-specific** security review. The generic built-in `/security-review` finds
generic vulnerabilities; this one knows *this* platform's highest-impact risks — above
all **tenant isolation** (Critical Rule #1: "every query, cache key, and API call must be
tenant-scoped"). It reviews a change, not the whole repo, and reports only **defensible,
exploitable** findings — not theoretical noise.

## Usage

```
/security-audit                 # review current branch diff vs main (default)
/security-audit US-LV-002       # review the diff for a story (its merge/PR range)
/security-audit src/backend/HRM.Application/Features/Leave   # review a path
/security-audit --deep          # fan out parallel reviewers per dimension (thorough)
```

## Scope (what to review)

1. **Resolve the target.** Default: `git diff main...HEAD`. If given a US-ID, review that
   story's range; if a path, review files under it; if a PR number, its diff. State the
   exact range reviewed.
2. **Load context** (read-only): the relevant `docs/vault/modules/{module}.md`, the
   multi-tenancy section of `CLAUDE.md`, and the touched entities/handlers/controllers.
3. Review **only the changed surface** plus anything it directly endangers (e.g. a new
   entity's query filter, a new endpoint's authorization). Do not audit the whole codebase.

## Threat checklist (this platform's real risks)

Score each against *actual* exploitability, not presence-of-pattern. The first block is
where this platform gets breached — weight it hardest.

### 1. Multi-tenant isolation — CRITICAL (the #1 risk)
- **New tenant-scoped entity** must derive from `BaseEntity` AND have a **global query
  filter** (`TenantId == _tenantContext.TenantId`) wired in `AppDbContext.OnModelCreating`.
  A new entity with no filter = cross-tenant data leak.
- **`IgnoreQueryFilters()`** — flag every use. It's only legitimate in deliberate spots
  (e.g. the tenant-resolution lookup). Anywhere in feature code it's a leak.
- **Raw SQL / `FromSqlRaw` / `ExecuteSqlRaw`** — must carry an explicit `WHERE tenant_id`;
  raw SQL bypasses the global query filter entirely.
- **Cache keys** must include the tenant id. A tenant-agnostic cache key serves one
  tenant's data to another.
- **Cross-tenant object references** — accepting a foreign `Id` from the request without
  confirming it belongs to the current tenant (IDOR across tenants).
- **Background jobs (Hangfire)** run with no HTTP context — confirm they set/scope the
  tenant explicitly rather than relying on `ITenantContext` being populated.

### 2. Authorization & access control — HIGH
- Every new controller/endpoint has `[Authorize]` (or a deliberate `[AllowAnonymous]`)
  and the **correct role/policy**. A missing attribute = open endpoint.
- **IDOR within a tenant**: does the handler verify the caller may act on *this* record
  (ownership/role), not just that they're authenticated?
- Mutating endpoints check role (e.g. only HR/admin can edit others' records).

### 3. Injection & unsafe data flow — HIGH
- No string-interpolated SQL; parameterized EF/Dapper only.
- No command/path injection from user input into `Process`/file paths.
- Angular: no `bypassSecurityTrustHtml`/`innerHTML` on user-controlled data (XSS).

### 4. Secrets & configuration — HIGH
- No hardcoded secrets/connection strings/keys in tracked files (Rule #6 — secrets via
  `.env`/user-secrets/`${ENV_VAR}`). (The `secret-guard` hook blocks the obvious cases;
  catch what it can't — encoded/assembled secrets.)
- JWT signing key, BCrypt usage, refresh-token handling unchanged and sound.

### 5. PII & data exposure — HIGH (HR data is sensitive by definition)
- PII (names, salaries, national IDs, contact info) not written to logs (Serilog
  `LogContext`), error messages, or returned in DTOs that over-expose.
- Sensitive fields encrypted at rest where required (`pgcrypto`).
- No PII in URLs/query strings (ends up in logs/history).

### 6. Input validation & abuse — MEDIUM
- Commands have FluentValidation validators; no mass-assignment (DTO/command, not entity,
  bound from the request).
- Idempotency on critical writes where the design calls for it.
- File uploads: type/size limits, no path traversal.

## Output

Write the report to `security-reviews/{scope}.md` (create the folder if absent; `{scope}`
= branch name or US-ID) using this structure, and print the verdict + path:

```markdown
# Security Audit: {scope}

- **Reviewed:** {git range / paths}
- **Date:** {today}
- **Verdict:** PASS | FINDINGS  (Critical: N, High: N, Medium: N, Low: N)

## Findings
### [CRITICAL] {title}
- **Where:** `path/to/file.cs:42`
- **Category:** Tenant isolation | AuthZ | Injection | Secrets | PII | Validation  (CWE-xxx)
- **Why it's exploitable:** {concrete attack path — who does what to get what}
- **Fix:** {specific change, ideally with the corrected snippet}

### [HIGH] ...

## Defense-in-depth (non-blocking) suggestions
- {hardening that isn't an exploitable bug — kept separate so it doesn't inflate severity}
```

### Severity rules
- **Critical** = cross-tenant data access, auth bypass, RCE, secret leak in tracked code.
- **High** = within-tenant IDOR, missing authz on a sensitive endpoint, injection, PII leak.
- **Medium / Low** = exploitable only under unlikely conditions, or defense-in-depth.
- Calibrate by *exploitability*. A `bypassSecurityTrustHtml` on a hardcoded constant is not
  High — say so. Do not pad the report with theoretical concerns; put those under
  "Defense-in-depth."

## `--deep` mode (optional, thorough)
For a release candidate or a high-blast-radius change, fan out **parallel sub-agents via the
Agent tool**, one per dimension (tenant-isolation, authz, injection, secrets/PII), each
returning structured findings; then **dedupe and synthesize** into the single report above.
Default (no flag) is a single-pass review — cheaper, fine for routine story diffs.

## Honesty contract
- Report **only what you can justify** with a concrete exploit path. Mark uncertainty with a
  confidence note rather than asserting.
- A clean change gets **PASS** — do not invent findings to look thorough.
- This is a **review**: do NOT edit code, commit, or open PRs. Hand findings to the owning
  dev agent / the user.

## Relationship to other tooling
- **`secret-guard` hook** prevents *new* hardcoded secrets at write time; this audit catches
  what regex can't (assembled/encoded secrets, design-level leaks).
- **Built-in `/security-review`** = generic vuln pass; run this for the HRM-specific tenant/
  PII/authz depth. They compose.
- Run before opening a story PR as a security gate alongside the build/test verify gate.
