---
id: TC-ONB-ISO-001
user_story: US-ONB-001
module: Onboarding / Offboarding
priority: critical
type: security
status: fail
created: 2026-06-17
---

# TC-ONB-ISO-001: Tenant A cannot see Tenant B onboarding templates (cross-tenant READ block)

## 1. Test Objective
Verify AC-5: onboarding templates are isolated by tenant. A user in Tenant A listing/reading templates sees ONLY Tenant A's templates; Tenant B's templates are invisible, and vice versa. Reads are scoped by the EF Core global query filter (`TenantId == _tenantContext.TenantId`). A deliberate name-collision probe (identical template names in A and B) confirms separation is by `tenant_id`, not by name.

## 2. Related Requirements
- User Story: US-ONB-001
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-2 (asserted via EF query filters; Postgres RLS deferred — see note)
- Business Rules: BR-1
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: AC-5/NFR-2 name PostgreSQL RLS as an isolation layer. This codebase enforces tenant isolation via EF Core global query filters (read) + `TenantInterceptor` (write stamping); Postgres RLS is a deferred platform extension. These ISO tests assert the EF query-filter mechanism in force today. STORY MISMATCH to flag: reword the RLS claim as future hardening.

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) both exist with valid scoped JWTs.
- Both have a template named "Standard Engineer Onboarding" (collision probe), each with its own tasks.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | template "Standard Engineer Onboarding" |
| Tenant B | globex | identically named template |
| Probe entities | onboarding_template + tasks | tenant-scoped tables |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As an acme user, `GET /api/v1/onboarding/templates` | Returns ONLY acme's templates; globex's identically named template is absent (filtered by `TenantId`). |
| 2 | As a globex user, list templates | Returns ONLY globex's templates; acme's are absent. |
| 3 | As acme, read each template's tasks | Only acme's task rows are returned; no globex tasks. |
| 4 | Cross-check counts | The union seen by A and by B equals the full set with zero overlap — separation is by `tenant_id`, not by name (BR-1). |

## 6. Postconditions
- No cross-tenant read leakage of templates or tasks.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
