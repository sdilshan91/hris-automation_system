---
id: TC-LV-ISO-009
user_story: US-LV-003
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-009: Employee in Tenant A cannot submit or view leave via Tenant B's context

## 1. Test Objective
Verify that leave requests are fully tenant-isolated: an employee authenticated in Tenant A cannot submit a leave request that lands in Tenant B, cannot view Tenant B's leave requests, and cannot reference Tenant B's leave types/employees in a submission. (Test Hint: verify employee in Tenant A cannot submit leave via Tenant B's API.)

## 2. Related Requirements
- User Story: US-LV-003
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with employee "Jane Smith" (Leave.Apply) and an active "Annual Leave" type.
- Tenant "globex" exists with employee "Bob Stone" and its own "Annual Leave" type.
- Jane is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Jane Smith authenticated here |
| Tenant B | globex | Bob Stone, separate leave types |
| acme leave type id | LT-acme-annual | Valid in acme |
| globex leave type id | LT-globex-annual | Valid only in globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jane (acme), submit `POST /api/v1/leaves` with `X-Tenant-Subdomain: globex` but Jane's acme JWT | Tenant context resolves to the authenticated tenant (acme); the request cannot be stamped into globex. Either rejected (403/400) or created strictly in acme -- never in globex. |
| 2 | As Jane (acme), submit a leave request referencing globex's leave type id (LT-globex-annual) | Server returns 400/404 -- the leave type is not visible in acme's context (EF global query filter). No request created. |
| 3 | As Jane (acme), call `GET /api/v1/leaves` | Only acme's leave requests for Jane are returned; zero globex requests. |
| 4 | As Jane (acme), attempt `GET /api/v1/leaves/{globex_request_id}` using a known globex request UUID | Response 404 Not Found (filtered by tenant). |
| 5 | Switch to globex context (Bob Stone) and verify Jane's acme requests are not visible | Globex sees only globex requests; no acme data leaks. |
| 6 | Verify the `TenantInterceptor` stamped `tenant_id` from the resolved context, not from any client-supplied value | Every created `leave_request.tenant_id` equals the authenticated tenant's id. |

## 6. Postconditions
- No leave request created by an acme user is ever persisted under globex.
- Cross-tenant reads of leave requests and leave types return no data.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
