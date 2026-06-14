---
id: TC-LV-205
user_story: US-LV-010
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-205: Cancel API contract -- POST /api/v1/leaves/{id}/cancel body, response shape, and not-found handling (FR-1, FR-6)

## 1. Test Objective
Verify the cancellation endpoint contract: `POST /api/v1/leaves/{id}/cancel` accepts a `reason` body, returns the updated request (status Cancelled, `cancelled_at`, `cancellation_reason`) wrapped in the standard `ApiResponse<T>` envelope on success, returns 404 for a non-existent/unknown request id within the tenant, and records the approval-history entry (FR-1, FR-6).

## 2. Related Requirements
- User Story: US-LV-010
- Functional Requirements: FR-1, FR-2, FR-6
- Data Requirements: Section 7

## 3. Preconditions
- Tenant "acme".
- Employee "Jane Smith" has a PENDING request R.
- A random non-existent request id `{fakeId}` is available for the not-found case.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | POST /api/v1/leaves/{id}/cancel | FR-1 |
| Body | `{ reason: "..." }` | optional for pending, required for approved |
| Envelope | ApiResponse<ILeaveActionResult> | tolerates bare body |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST /api/v1/leaves/{R}/cancel` with a reason | 200; response carries `requestId`, `status = Cancelled`, and (for approved) the restored `currentBalance`; wrapped in `ApiResponse<T>` (`.data`). |
| 2 | Inspect the persisted request | `status = Cancelled`, `cancelled_at` set, `cancellation_reason` set per Section 7 data requirements. |
| 3 | `POST /api/v1/leaves/{fakeId}/cancel` (unknown id) | 404 Not Found (no leak of other tenants' ids); no state change. |
| 4 | Send a malformed body (e.g. non-string reason) | 400 with a validation error; request unchanged. |
| 5 | Verify approval-history | An `action = Cancelled` row is recorded (FR-6). |

## 6. Postconditions
- The endpoint honors the documented contract: success envelope, correct data fields, 404 for unknown ids, validation on bad input.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
