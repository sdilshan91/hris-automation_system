---
title: Admin Console — Full Module Test Report (US-ADM-001…010)
module: Admin Console
created: 2026-06-25
method: REPORT-ONLY deep API pass via @test-runner (curl+JWT, read-only psql, bound xUnit where present)
stack: native localhost — API :5000, FE :4200, PostgreSQL 18 :5432, Docker up
ledgers: TEST-FINDINGS.md (findings) · TEST-STATUS.md (per-US tracker)
policy: identify + document only — NO fixes, NO PRs (see CLAUDE.md /test-all)
---

# Admin Console — Full Module Test Report

Deep, story-by-story execution of all 10 admin-console user stories against the running stack, after
seeding tenant personas. **Report-only:** every defect is logged for human triage; nothing was fixed.

## 1. Scoreboard (per user story)

| US | Title | PASS | FAIL | BLOCKED | Findings |
|----|-------|-----:|-----:|--------:|----------|
| US-ADM-001 | Provision new tenant | 7 | 0 | 9 | — |
| US-ADM-002 | Monitor platform health/usage | 7 | 0 | 11 | ISSUE-002, ISSUE-003 |
| US-ADM-003 | Impersonate tenant user (audit) | 9 | 1 | 7 | **BUG-001 (HIGH)**, ISSUE-001 |
| US-ADM-004 | Suspend / terminate tenant | 9 | 1 | 11 | **BUG-002 (MED)** |
| US-ADM-005 | Manage users & role assignments | 15 | 2 | 4 | ISSUE-004/005/006/007 |
| US-ADM-006 | Configure company settings | 10 | 4 | 7 | **BUG-003 (CRIT)**, BUG-004 (HIGH), BUG-005 (MED), ISSUE-008/009 |
| US-ADM-007 | Manage approval workflows | 12 | 1 | 5 | BUG-006 (MED), ISSUE-010/011 |
| US-ADM-008 | View audit logs | 13 | 1 | 7 | BUG-007 (HIGH), ISSUE-012 |
| US-ADM-009 | Manage subscription plans | 11 | 2 | 5 | BUG-008 (HIGH), ISSUE-013 |
| US-ADM-010 | Tenant data export | 17 | 0 | 7 | ISSUE-014 |
| **TOTAL** | | **110** | **12** | **73** | **8 BUGs + 14 ISSUEs** |

195 TC verdicts recorded (the remainder of the 217 designed are ISO TCs shared across stories). All
executed TCs have a terminal `status:` (0 draft). Tracker tally: `[!]`9 + `[x]`1 = 10 admin stories.

## 2. The headline: BUG-003 — platform-wide tenant-isolation bypass (CRIT)

**A tenant-A admin can act as ANY other tenant by setting the `X-Tenant-Subdomain` header (host in prod).**
The JWT `tenant_id` claim is never validated against the subdomain-resolved tenant — there is no
`CurrentUser.TenantId == ITenantContext.TenantId` check after auth. Confirmed across four surfaces, escalating:

| Surface | Story | Proven impact |
|---|---|---|
| Settings write | US-ADM-006 | Read **and wrote** another tenant's settings |
| Workflows | US-ADM-007 | Cross-tenant workflow create / list / delete |
| Audit log | US-ADM-008 | Read **and exported** a victim tenant's full forensic audit trail |
| **Data export** | US-ADM-010 | **Initiated + downloaded a victim tenant's complete GDPR data bundle** (users.csv, audit_log.jsonl, all entity CSVs) |

This is the escalation of the June **BUG-8** (then rated LOW because no leak was *observed*). It is now
proven to allow cross-tenant **writes and bulk PII exfiltration** → **CRIT**. The row-level isolation
(EF query filters, `TenantInterceptor` write-stamping, body-`tenantId`-ignore) all work correctly — the
single hole is the unvalidated subdomain selecting the tenant *before* auth.

## 3. All findings (severity-ordered)

**BUGs (8):** 1 CRIT · 4 HIGH · 3 MED
- **BUG-003 · CRIT** — cross-tenant bypass (above). Affects every tenant-scoped endpoint.
- **BUG-001 · HIGH** — System Support impersonation is NOT read-only; support can write while impersonating (AC-6/BR-1 bypass). Confirmed: `PUT primary-color` → 200.
- **BUG-004 · HIGH** — tenant password policy stored but never enforced (hardcoded reset validator; no change-password endpoint; history/max-age unenforced).
- **BUG-007 · HIGH** — audit-log `search=` returns HTTP 500 on real Postgres (`string.Contains` on `jsonb` can't translate) — full-text search 100% non-functional.
- **BUG-008 · HIGH** — plan-limit propagation broken: plan edits and per-tenant overrides have no runtime effect; `PlanLimitResolver.Resolve` has zero live callers.
- **BUG-002 · MED** — terminate with `graceDays` omitted → 400 instead of plan default (30).
- **BUG-005 · MED** — localization update validates only `defaultLanguage`; invalid date-format/timezone/currency silently accepted.
- **BUG-006 · MED** — restoring an archived workflow while another active one exists for the same entity type → HTTP 500 (partial-unique-index collision; passes on InMemory, fails on real Postgres).

**ISSUEs (14):** contract-correctness + completeness nits
- **Audit completeness (recurring):** ISSUE-005 (denied actions write no audit row), ISSUE-006 (audit rows omit IP/user-agent) — recur across US-ADM-005/006/007/009.
- **4xx semantics:** ISSUE-001 (404 vs 400), ISSUE-003 (invalid enum silently ignored), ISSUE-012 (inverted date range → 200 not 400), ISSUE-011 (201 vs 200 + 409 vs 400/422 drift).
- **Search/sort/paging:** ISSUE-002 (tenant directory no search/paging), ISSUE-004 (`status=Invited` filter silently returns all), ISSUE-013 (plan list sort ignored).
- **Export/data hygiene:** ISSUE-014 (export CSVs dump raw EF columns incl. `RowVersion`/`IsDeleted`/`TenantId`/`CreatedBy`).
- **Other:** ISSUE-007 (`{id}` path param overloaded userTenantId vs userId), ISSUE-008 (at-cap logo opaque 400), ISSUE-009 (no idle≤absolute session invariant).

## 4. Why 73 BLOCKED (legitimate, not gaps in this run)
- **Playwright MCP down** → all UI / accessibility / cross-browser TCs unexecutable.
- **k6 not wired into the TC flow** → performance TCs blocked.
- **`[DEFERRED]` capabilities** → email delivery, OpenTelemetry observability, blob/S3 storage, Stripe billing, Postgres RLS, Phase-2 white-label — no system to test yet.
- **Unreachable states** → Terminated tenant (needs forcing the Hangfire deletion job), plan/employee caps (would require mutating seeded-tenant limits).

## 5. Test environment seeded this run
acme tenant `019ef3ba-ffb7-7eec-b24f-7ad806ca1cb9` (Trial). Personas (all pw `Admin@123!`, seeded via SQL
reusing `admin@hrm.local` bcrypt hash): `tenantadmin@/hr@/manager@/employee@acme.test` (acme),
`support@hrm.local` (System Support, platform). All login-verified 200. Throwaway `qa0X-*` tenants/plans/
users/exports left in place; seeded personas + acme/platform/techoneglobal untouched.

## 6. Recommended fix priority (human decides — NOT applied)
1. **BUG-003 (CRIT)** — add a post-auth `CurrentUser.TenantId == ITenantContext.TenantId` guard (exempt `/auth/*`, impersonation, system context). Single highest-value fix; closes a platform-wide bypass.
2. **BUG-001 (HIGH)** — fix the support→read-only determination in `ImpersonationService.StartAsync`.
3. **BUG-007 (HIGH)** — audit search must not `Contains` on jsonb (500s).
4. **BUG-004, BUG-008 (HIGH)** — enforce password policy; wire `PlanLimitResolver` into live limit checks.
5. **BUG-002/005/006 (MED)**, then the ISSUE backlog (audit completeness first — ISSUE-005/006).
