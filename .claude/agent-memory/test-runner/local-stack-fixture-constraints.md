---
name: local-stack-fixture-constraints
description: Docker-compose local stack (2026-09) has NO acme tenant, 3 users, 0 perf cycles — how to seed fixtures via API, DB creds, log path, and why extra personas cannot be minted
metadata:
  type: project
---

# Local stack fixture constraints (observed 2026-09-02)

The `acme` tenant + `tenantadmin@acme.test` / `hr@acme.test` persona set referenced by most TC preconditions
**does not exist** on the current docker-compose stack. Verify before planning a run — several older memories
here assume acme and will mislead you.

**Why it matters:** any TC needing >1 persona, or needing an employee-linked reviewer/approver, is
`blocked: persona-gap` on this stack unless you first build the fixture. Budget for that, or pick TCs whose
arms are single-persona.

**How to apply:**
- Tenants: `platform` (HRM Platform Admin), `techoneglobal`, `e2e`. Users: **3 total** —
  `admin@hrm.local`/`Admin@123!` (platform, all permissions), `sachithra@techoneglobal.org`,
  `owner@e2e.test` (passwords unknown). Employees: 2. Appraisal cycles: 0.
- **You cannot mint a new login-capable persona.** `POST /api/v1/tenant/users/invite` stores only
  `user_invitations.token_hash` (BCrypt) and `LogOnlyUserManagementNotificationService` deliberately never
  logs the raw token — and the REAL SMTP sender is the DI-registered one, so nothing is captured anywhere.
  `POST /api/v1/auth/accept-invitation` is therefore undrivable. Multi-persona authz arms → BLOCKED, or
  fall back to the xUnit HTTP-layer tests which mint personas in-process.
- **Cross-tenant is correctly refused:** `admin@hrm.local` + `X-Tenant-Subdomain: techoneglobal` →
  **403 `cross_tenant_denied`**. Admin can only act inside `platform`. Seed fixtures there.
- **DB:** `docker exec -e PGPASSWORD='Sanjesi#123' hris-postgres-1 psql -U developer -d hris_dev_db -c "..."`
  (`postgres`/`hrm_db` do NOT exist — the role and DB names differ from the defaults). Tables are snake_case
  (`appraisal_cycle`, `feedback_360`, `feedback_360_release`, `employees`), and most carry FORCED RLS keyed on
  `current_setting('app.current_tenant')` — a raw INSERT needs `SET app.current_tenant = '<tenant-uuid>'` first.
- **Serilog lives INSIDE the container**, not at `src/backend/HRM.Api/Logs/`:
  `docker exec hris-backend-1 sh -c 'grep -n "ERR" /app/Logs/hrm-YYYYMMDD.log'`. It carries the EF SQL and the
  full exception; correlate by `RequestId`. This is how BUG-431's root cause was found in one pass.
- **Seeding an employee via `POST /api/v1/tenant/employees` requires `departmentId` + `jobTitleId`** (the 400
  says "Department is required" but the OpenAPI schema marks neither as required). Fetch existing ones from
  `/api/v1/tenant/departments` and `/api/v1/tenant/job-titles` first.
- **All date fields must be sent UTC-suffixed** (`2026-08-01T00:00:00Z`). Date-only `2026-08-01` 500s on
  cycle creation — that is BUG-431, not your payload being wrong; don't burn time re-deriving it.
- JWT: capture with `printf '%s'` (a trailing newline in the token file yields a confusing 401).

See [[us-prf-005-360-release-exec-2026-09-02]].
