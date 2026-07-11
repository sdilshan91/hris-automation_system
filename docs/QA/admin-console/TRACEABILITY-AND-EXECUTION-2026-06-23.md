---
title: Admin Console — Per-User-Story Traceability, Tagging & Execution (PILOT)
module: Admin Console
created: 2026-06-23
scope: pilot (one module) — pattern to scale to the other 10 modules
executed_by: agent-driven (curl + JWT as platform SystemAdmin admin@hrm.local)
stack: native localhost (API http://localhost:5000, FE http://localhost:4200) — UP
---

# Admin Console — US-wise Traceability + Tagging + Execution (Pilot)

Pilot for the "validate test cases user-story-wise, then test likewise" request. Three parts:
**A** per-US traceability + test-type coverage, **B** the tagging fixes applied, **C** live per-US execution.
217 TCs across US-ADM-001…010.

---

## Part A — Per-US traceability & test-type coverage (post-tag)

Counts = number of TCs under each user story carrying that test-type tag.
(Hap=Happy, Neg=Negative, Bnd=Boundary, Sec=Security, MTI=Multi-tenant isolation, Prf=Performance, A11y=Accessibility, Xbr=Cross-browser.)

| User story | Title | TCs | Hap | Neg | Bnd | Sec | MTI | Prf | A11y | Xbr | Deferred |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| US-ADM-001 | Provision new tenant | 16 | 2 | 7 | 4 | 10 | 5 | 1 | 1 | 1 | 0 |
| US-ADM-002 | Monitor platform health/usage | 20 | 5 | 5 | 4 | 5 | 3 | 6 | 1 | 1 | 5 |
| US-ADM-003 | Impersonate tenant user (audited) | 17 | 3 | 12 | 3 | 16 | 1 | 0 | 1 | 0 | 0 |
| US-ADM-004 | Suspend / terminate tenant | 23 | 8 | 13 | 6 | 15 | 2 | 2 | 0 | 1 | 2 |
| US-ADM-005 | Manage users & role assignments | 25 | 11 | 13 | 11 | 19 | 11 | 1 | 0 | 0 | 1 |
| US-ADM-006 | Configure company settings | 24 | 9 | 13 | 6 | 16 | 10 | 1 | 1 | 1 | 1 |
| US-ADM-007 | Manage approval workflows | 22 | 12 | 13 | 11 | 8 | 7 | 1 | 0 | 0 | 2 |
| US-ADM-008 | View audit logs | 25 | 10 | 18 | 5 | 17 | 6 | 1 | 1 | 0 | 0 |
| US-ADM-009 | Manage subscription plans | 21 | 7 | 14 | 9 | 12 | 6 | 2 | 0 | 0 | 1 |
| US-ADM-010 | Tenant data export on demand | 24 | 10 | 12 | 9 | 17 | 5 | 1 | 0 | 0 | 2 |
| **TOTAL** | | **217** | **77** | **120** | **68** | **135** | **56** | **16** | **5** | **4** | **14** |

**Coverage read:**
- Negative (120) and Security (135) are strong — right weighting for a privileged, tenant-isolating admin surface.
- **Accessibility (5/217 = 2.3%) and Cross-browser (4 = 1.8%) are near-zero.** US-ADM-003/005/007/009/010 have **no a11y TC at all**. If WCAG is an AC, this is a design-level hole, not an execution one.
- US-ADM-007 (workflows) has the **weakest security coverage** (8) relative to its size — workflow authz/escalation is security-sensitive; worth 2-3 more negative-authz TCs.
- US-ADM-001 has only 2 happy-path TCs for the single most complex transaction (atomic tenant+owner+seed). Defensible (it's mostly negative/security) but thin on happy variants.

---

## Part B — Tagging fixes applied (this run)

**14 TCs had an empty "Test Category Tags" block.** Investigation: **all 14 are `status: blocked` / `[DEFERRED]`** — they trace ACs for capabilities not yet built (OpenTelemetry metrics, real email delivery, blob deletion, live workflow runtime, Stripe billing, Phase-2 white-label). They were left blank deliberately, not by mistake.

**Action:** tagged each with its *intended* category so the matrix reflects planned coverage, **keeping `status: blocked`** so execution reports them BLOCKED, never PASS. (Flag if you'd rather keep deferred TCs untagged.)

| TC | Tagged | Reason |
|---|---|---|
| TC-ADM-002-14/15/16/17/18 | Performance | error-rate %, P95 latency, SLA uptime, usage gauges — perf/observability KPIs |
| TC-ADM-004-18, -19 | Happy path | lifecycle email delivery; termination blob deletion (functional) |
| TC-ADM-005-19 | Happy path | invitation / password-reset email delivery (functional) |
| TC-ADM-006-21 | Happy path | white-label / custom-CSS (Phase 2, functional) |
| TC-ADM-007-16, -17 | Happy path | live delegation routing; SLA-breach auto-escalation (runtime functional) |
| TC-ADM-009-17 | Happy path | Stripe billing / self-serve plan change (Phase 2, functional) |
| TC-ADM-010-20 | Happy path | uploaded-docs ZIP subtree by entity (functional) |
| TC-ADM-010-17 | Security | schema-doc PDF + **PII fields clearly marked** (the testable assertion is PII handling) |

Result: **admin-console untagged TCs 14 → 0.**

---

## Part C — Per-US execution (live API, SystemAdmin smoke)

Method: representative executable endpoint(s) per US hit with a real `admin@hrm.local` JWT (holds all permissions). System routes under `X-Tenant-Subdomain: admin`, tenant routes under `platform`. This is **per-US smoke**, not all 217 TCs individually; deferred TCs are BLOCKED by design.

| US | Endpoint(s) exercised | HTTP | Verdict |
|---|---|---|---|
| US-ADM-001 | `GET system/tenants`, `…/subdomain-availability`, `…/tenants/plans` | 200·200·200 | ✅ PASS |
| US-ADM-002 | `GET system/monitoring/health`, `…/tenant-usage` | 200·200 | ✅ PASS (live KPIs; OTel-sourced metrics still deferred) |
| US-ADM-003 | `GET system/impersonation/targets?tenantId=…` | 200 | ✅ PASS · ⚠ 404 (not 400) when `tenantId` omitted — contract nit |
| US-ADM-004 | `GET system/tenants/{id}/lifecycle/history` | 200 | ✅ PASS |
| US-ADM-005 | `GET tenant/users`, `tenant/roles` | 200·200 | ✅ PASS |
| US-ADM-006 | `GET tenant/settings` (org-profile is PUT-only) | 200 | ✅ PASS |
| US-ADM-007 | `GET tenant/workflows` | 200 | ✅ PASS |
| US-ADM-008 | `GET tenant/audit-logs`, `…/filter-options` | 200·200 | ✅ PASS |
| US-ADM-009 | `GET system/plans` | 200 | ✅ PASS |
| US-ADM-010 | `GET tenant/data-exports` (system create is POST) | 200 | ✅ PASS |

**Result: 10/10 US PASS at API smoke level. 0 5xx. 0 tenant-isolation breaches observed.**

### Honest limits of this execution
1. **Smoke, not exhaustive** — one to three endpoints per US, read-path biased. Negative/boundary/security TCs (the bulk of the 217) were **not** individually driven; their *tags* are validated, their *assertions* are not.
2. **No UI layer** — API only. FE rendering, a11y, and cross-browser TCs are untested here (and barely designed — see Part A).
3. **SystemAdmin persona only** — tenant-scoped US (005-008) were hit under the platform tenant context, not a real business-tenant persona. Tenant-isolation TCs need a second tenant to be meaningful.
4. **14 deferred TCs = BLOCKED**, correctly excluded from PASS.

### Findings opened
- **CT-ADM-1 (LOW):** `GET system/impersonation/targets` returns `404 tenant_not_found` when the required `tenantId` query param is missing — should be `400 Bad Request`.

---

## Scale plan (to the other 10 modules)
This pilot = the repeatable recipe: (A) script per-US TC + 8-type tally, (B) detect untagged → classify deferred-vs-mistake → tag by intent, (C) login + per-US endpoint smoke from Swagger routes. Recommend running it module-by-module (core-hr next — largest at 372 TCs, weakest boundary coverage). Fan out one agent per module for A+B (read/tag only, no write collisions); serialize C per module to avoid shared-DB write races.
