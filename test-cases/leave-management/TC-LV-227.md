---
id: TC-LV-227
user_story: US-LV-011
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-227: assign-lop / lop-summary reject a cross-tenant employeeId — no IDOR across tenants

## 1. Test Objective
Verify that an HR Officer in Tenant A cannot assign LOP to, or read the lop-summary of, an employee that belongs to Tenant B by passing that employee's id (IDOR probe). The request is denied / scoped out (403/404 or empty), and no Tenant B LOP data is created or disclosed.

## 2. Related Requirements
- User Story: US-LV-011
- Non-Functional Requirements: NFR-2
- Business Rules: tenant isolation (Test Hint §11)
- Cross-ref: TC-LV-ISO-041..043

## 3. Preconditions
- Tenant "acme" with HR Officer "Asha" (has `Leave.Manage`).
- Tenant "globex" with employee "Kofi".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Actor | Asha (acme HR) | authorized in acme only |
| Target | Kofi (globex employee id) | cross-tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Asha (acme), call `POST /api/v1/leaves/assign-lop` with Kofi's (globex) employeeId | Denied — the EF global query filter scopes the employee lookup to acme, so Kofi is not found under Asha's tenant; result is 403/404, no LOP row created in globex. |
| 2 | As Asha, call `GET /api/v1/leaves/lop-summary?employeeId={Kofi}` | No globex LOP data is returned (empty/404); cross-tenant disclosure is blocked. |
| 3 | Verify globex side | Kofi has no new LOP entry; globex tenant is untouched. |
| 4 | Positive control | Asha assigns LOP to an acme employee successfully — confirms the block is tenant-scoping, not a broken endpoint. |

## 6. Postconditions
- Cross-tenant employeeId on LOP endpoints is blocked; no Tenant B LOP data is created or read.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
