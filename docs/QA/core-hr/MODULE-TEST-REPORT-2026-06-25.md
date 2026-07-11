---
title: Core HR — Module Test Report (US-CHR-001…012)
module: Core HR
created: 2026-06-25
method: REPORT-ONLY representative API pass via @test-runner (curl+JWT, read-only psql, Serilog root-cause, k6, Playwright+CDP for UI/a11y/perf samples)
stack: native localhost (debugger-free) — API :5000, FE :4200, PostgreSQL 18 :5432
policy: identify + document only — NO fixes, NO PRs
---

# Core HR — Module Test Report (representative)

All 12 Core HR stories tested representative-depth (API happy/negative/boundary + security/authz + multi-tenant
isolation, plus UI/a11y/perf samples where reachable). Report-only — every defect logged for human triage.

## 1. Scoreboard (per story — all `[!]` tested-with-findings)

| US | Title | PASS | FAIL | BLOCKED/def | Headline findings |
|----|-------|-----:|-----:|---:|---|
| CHR-001 | Add employee | 18 | 2 | 24 | BUG-003, ISSUE-015/016/017 |
| CHR-002 | View/edit profile | 13 | 2 | 12 | BUG-003 (PII **write**), BUG-010 |
| CHR-003 | Directory | 12 | 1 | 15 | BUG-003, **ISSUE-018**, ISSUE-019 |
| CHR-004 | Departments | 28 | 3 | 7 | **BUG-014**, BUG-013, BUG-015, BUG-003, ISSUE-020 |
| CHR-005 | Job titles | 19 | 5 | 9 | BUG-016, ISSUE-021/022, BUG-003 |
| CHR-006 | Org tree | 10 | 1 | 14 | BUG-003, ISSUE-023 |
| CHR-007 | Office locations | 9 | 4 | 11 | BUG-017, BUG-018, BUG-003 |
| CHR-008 | Documents | 10 | 5 | 14 | **BUG-019**, **BUG-020**, ISSUE-024 |
| CHR-009 | Status mgmt | 14 | 2 | 10 | **BUG-021**, ISSUE-025 |
| CHR-010 | Bulk import | 17 | 1 | 15 | BUG-022, **ISSUE-026** |
| CHR-011 | Reporting structure | 14 | 2 | 15 | BUG-023, ISSUE-027 |
| CHR-012 | Custom fields | 18 | 1 | 15 | BUG-024, ISSUE-028 |
| **TOTAL** | | **~182** | **~29** | **~150** | **13 new BUGs + 14 new ISSUEs** |

## 2. ⚠️ Three findings RETRACTED (debugger artifacts)
**BUG-009, BUG-011, BUG-012** (originally HIGH/CRIT "validation-path hangs / API-wide DoS") were **debugger
artifacts** — the backend was running under the VS Code debugger, which breaks on the first-chance
`ValidationException` at `ValidationBehavior.cs:37` and waits for a human "Continue". Re-verified
debugger-free: every validation failure returns an **instant 400** (<0.15s), bursts cause no pool
exhaustion. **Lesson saved:** run perf/availability tests without an exception-breaking debugger.

## 3. Cross-cutting themes (the real signal)
1. **BUG-003 — cross-tenant bypass (CRIT, systemic).** Confirmed on *every* Core HR read surface tested
   (employees, profile, directory, departments, job-titles, org-tree, locations, documents, status,
   reporting) and several writes (employee create, profile PII edit, dept). Root: `TenantResolutionMiddleware`
   never validates the resolved-tenant (from `X-Tenant-Subdomain`/host) against the JWT `tenant_id` claim.
   BUG-020/021 and ISSUE-026 are the same root on the document/status/import surfaces. **One guard fixes all.**
2. **Missing audit coverage (systemic).** Many write/read paths emit only a Serilog line, no queryable audit
   row: BUG-010 (profile view PII-access), BUG-018 (location CRUD), BUG-022 (bulk import), BUG-023 (manager
   assign), BUG-024 (custom-field changes), ISSUE-024/025 (document ops, status snapshot). NFR-5 gap.
3. **Case-sensitive + un-trimmed uniqueness (systemic).** BUG-013 (dept), BUG-016 (job title), BUG-017
   (location), ISSUE-028 (custom field) — names differing only by case/whitespace coexist. One root: no
   `LOWER()`/citext + no trim. BR-1 violated module-wide.
4. **Authorization gaps.** BUG-019 (document **list** has no authz — any tenant user lists any employee's
   docs), BUG-014 (department `managerId` not tenant-scoped — accepts a foreign-tenant employee), ISSUE-018
   (flat-string permission: `View.All` superset denied where `View.Own` is required).

## 4. Good news (things that work)
- Employee **manager FK IS tenant-scoped** (no BUG-014-equivalent leak) and **cycle detection works** (CHR-011).
- **Tenant write-stamping is correct** — `TenantInterceptor` stamps the resolved tenant; in-body `tenantId`
  spoof is ignored (CHR-001/004/010). The leak is purely the unvalidated *subdomain*, not row-level.
- **Bulk-import read-isolation is clean** (cross-tenant job status → 404) — contrast with the leaky reads.
- **k6 performance** within SLA where measured (CHR-004 dept CRUD P95 20–55ms).
- Status-machine transitions, validation, and RBAC deny-arms are correct across the module.

## 5. Coverage caveats
- **UI / a11y / cross-browser** mostly BLOCKED: the dev **FE is bound to the `platform` subdomain**, so
  tenant-feature pages aren't reachable as an `acme` persona. Only the auth-shell a11y was sampled
  (Lighthouse 95 → ISSUE-016 color-contrast, ISSUE-017 favicon). Re-point the FE at a business tenant to
  audit tenant UIs.
- **RLS** (disabled) and **Redis cache-key** (off) isolation TCs are env-blocked.
- Representative depth: exhaustive UI/security permutations left `draft`/deferred per story.

## 6. Test-data note (cleanup pending)
`acme` holds throwaway depts/jobs/locations/employees/custom-fields/import rows from the runs. The real
`techoneglobal` tenant has **2 residual rows from early BUG-003 write-proofs** (a `crosswrite@example.com`
employee + `ToneEng` dept) flagged for deletion; later runs were **read-only** against techoneglobal.
