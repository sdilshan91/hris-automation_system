---
id: TC-PAY-ISO-045
user_story: US-PAY-012
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-045: Cross-tenant READ -- Tenant B cannot see Tenant A's payroll history or audit_log entries; audit trail is tenant-scoped (name/action-collision probe)

## 1. Test Objective
Verify AC-5 and FR-3/FR-8: payroll history and the `audit_log` table are tenant-scoped. A user from Tenant B querying the payroll runs API or the audit trail API receives ONLY Tenant B's records; Tenant A's runs and audit entries are entirely invisible -- even when both tenants have identically named actions, actors, resources, and overlapping timestamps (collision probe).

## 2. Related Requirements
- User Story: US-PAY-012
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-3, FR-4, FR-8
- Business Rules: BR-2 (immutable), BR-4

## 3. Preconditions
- Two Active tenants "acme" (A) and "globex" (B), each with payroll runs + audit_log entries.
- Both tenants have an entry with identical action="SalaryComponent.Updated", a same-named component/actor, and an overlapping timestamp window (collision probe).
- Users authenticated in A and in B respectively.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| A audit entry | SalaryComponent.Updated @ acme | tenant_id=A |
| B audit entry | SalaryComponent.Updated @ globex | tenant_id=B |
| Probe | same action/actor-name/timestamp window | collision |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As B, GET `/api/v1/payroll/runs` (history). | Only globex's runs returned; none of acme's runs appear (EF global query filter on tenant_id). |
| 2 | As B, GET the audit trail (no filter / last 30 days). | Only globex's audit_log rows returned; acme's rows absent despite the collision probe. |
| 3 | As B, filter the audit trail by action=SalaryComponent.Updated. | Only globex's matching entry returned; acme's identically-named entry is NOT returned. |
| 4 | As B, request a run-detail audit timeline for one of A's run ids (guessed/known). | 404/403 -- B cannot read A's run timeline; no acme event leaks. |
| 5 | As B, export the audit trail. | The export contains zero acme rows. |
| 6 | (RLS note) Confirm the enforcement mechanism. | Isolation is enforced via EF Core global query filters + TenantInterceptor on payroll_run + audit_log; AC-5/FR-3 say "RLS on audit_log" -- Postgres RLS is noted as an extension point. |

## 6. Postconditions
- B sees only B's history + audit entries; A's data fully isolated under a collision probe.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
