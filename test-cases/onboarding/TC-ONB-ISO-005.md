---
id: TC-ONB-ISO-005
user_story: US-ONB-002
module: Onboarding / Offboarding
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-005: Tenant A cannot see Tenant B onboarding assignments (cross-tenant READ block)

## 1. Test Objective
Verify NFR-2 / tenant isolation for assigned checklists: a user in Tenant A listing/reading onboarding assignments and their task instances sees ONLY Tenant A's data; Tenant B's assignments are invisible, and vice versa. Reads are scoped by the EF Core global query filter (`TenantId == _tenantContext.TenantId`).

## 2. Related Requirements
- User Story: US-ONB-002
- Acceptance Criteria: AC-2
- Non-Functional Requirements: NFR-2 (asserted via EF query filters; Postgres RLS deferred — see note)
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: NFR-2 names PostgreSQL RLS as an isolation layer. This codebase enforces tenant isolation via EF Core global query filters (read) + `TenantInterceptor` (write stamping); Postgres RLS is a deferred platform extension. These ISO tests assert the EF query-filter mechanism in force today. STORY MISMATCH to flag: reword the RLS claim as future hardening.

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) both exist with valid scoped JWTs.
- Each tenant has at least one employee with an active onboarding checklist and task instances.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | employee + assigned checklist + tasks |
| Tenant B | globex | employee + assigned checklist + tasks |
| Probe entities | onboarding_checklist_instance + onboarding_task_instance | tenant-scoped tables |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As an acme user, `GET /api/v1/onboarding/assignments` | Returns ONLY acme's checklist instances; globex's are absent (filtered by `TenantId`). |
| 2 | As a globex user, list assignments | Returns ONLY globex's; acme's are absent. |
| 3 | As acme, read task instances for an assignment | Only acme's task rows returned; no globex task instances. |
| 4 | Cross-check counts | The union seen by A and by B equals the full set with zero overlap — separation is strictly by `tenant_id`. |

## 6. Postconditions
- No cross-tenant read leakage of checklist instances or task instances.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
