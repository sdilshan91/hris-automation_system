---
id: TC-LV-ISO-010
user_story: US-LV-003
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-010: API rejects leave requests without a valid tenant context

## 1. Test Objective
Verify that the leave submission and read endpoints require a resolvable tenant context and reject requests where the tenant cannot be resolved or is inactive, preventing tenant-less data access.

## 2. Related Requirements
- User Story: US-LV-003
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists and is active.
- An employee with `Leave.Apply` exists in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing subdomain | (no X-Tenant-Subdomain, non-tenant host) | Unresolvable tenant |
| Unknown subdomain | doesnotexist.yourhrm.com | No matching tenant |
| Inactive tenant | suspendedco.yourhrm.com | Tenant exists but inactive |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/leaves` with a valid body but no resolvable tenant (missing subdomain/header) | Request is rejected (400/404 tenant not resolved). No request created. |
| 2 | Send `POST /api/v1/leaves` with an unknown subdomain `doesnotexist` | Tenant resolution fails; request rejected. No request created. |
| 3 | Send `POST /api/v1/leaves` targeting an inactive/suspended tenant | Request rejected (tenant not active). No request created. |
| 4 | Send `GET /api/v1/leaves` without a resolvable tenant | Rejected; no leave data returned. |
| 5 | Send the same `POST` with a valid acme subdomain and token | 201 Created -- confirms rejections were due to tenant context only. |

## 6. Postconditions
- No leave request is created or read without a valid, active tenant context.
- Tenant resolution middleware gates all leave endpoints.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
