---
id: TC-LV-ISO-018
user_story: US-LV-005
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-018: API rejects approve/reject requests without a valid tenant context

## 1. Test Objective
Verify that the approve and reject endpoints require a resolved tenant context: a request whose subdomain/`X-Tenant-Subdomain` resolves to no tenant (or is missing) is rejected and does not action any request.

## 2. Related Requirements
- User Story: US-LV-005
- Non-Functional Requirements: NFR-3
- Related: US-AUTH-007 (tenant resolution from subdomain)

## 3. Preconditions
- A valid authenticated user exists, but the tenant context cannot be resolved.
- A pending request R exists in some tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| X-Tenant-Subdomain | absent / unknown (e.g., "doesnotexist") | No tenant resolves |
| Request R | Pending | Target |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST /api/v1/leaves/{R}/approve` with no tenant subdomain header and a non-tenant host | The request is rejected because no tenant context is resolved (e.g., 400/404); R is not approved. |
| 2 | `POST /api/v1/leaves/{R}/reject` with an unknown subdomain that resolves to no tenant | Rejected; no state change. |
| 3 | Confirm the global query filter behavior | With no resolved tenant, the request row is not retrievable for actioning; no ledger/history/audit entry is created. |
| 4 | Repeat with the correct tenant subdomain | The action succeeds (positive control), confirming the rejection was due solely to missing tenant context. |

## 6. Postconditions
- Requests without a resolved tenant context cannot action any leave request.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
