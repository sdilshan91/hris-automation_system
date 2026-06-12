---
id: TC-CHR-226
user_story: US-CHR-009
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-226: Idempotency -- duplicate request with same Idempotency-Key produces only one status transition (NFR-3)

## 1. Test Objective
Verify that sending the same status change request twice with the same `Idempotency-Key` header results in only one status transition being recorded. The second request should return a successful response (not an error) but must not create a duplicate employment history entry. This validates NFR-3.

## 2. Related Requirements
- User Story: US-CHR-009
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Carol Davis" (`emp-005-uuid`) exists with status `active`.
- No prior status changes exist for this employee.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Carol Davis (emp-005-uuid) | Status: active |
| New Status | suspended | Valid transition |
| Reason | Temporary suspension | Required |
| Effective Date | 2026-06-12 | Today |
| Idempotency-Key | `idem-key-abc-123` | Unique key for this request |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/tenant/employees/emp-005-uuid/status` with headers `{ "Idempotency-Key": "idem-key-abc-123" }` and body `{ "newStatus": "suspended", "reason": "Temporary suspension", "effectiveDate": "2026-06-12" }`. | Response status is 200 OK. Employee status changed to "suspended". |
| 2 | Query the employment history table for emp-005-uuid. | Exactly ONE status_change record exists: previous=active, new=suspended. |
| 3 | Send the EXACT same request again with the same `Idempotency-Key: idem-key-abc-123` header and same body. | Response status is 200 OK (idempotent success, not an error). |
| 4 | Query the employment history table again. | Still exactly ONE status_change record. No duplicate entry was created. |
| 5 | Send a DIFFERENT status change request with a NEW Idempotency-Key (`idem-key-def-456`) to transition from suspended back to active. | Response status is 200 OK. A second status_change record is created (suspended -> active). |
| 6 | Verify the employment history now has exactly 2 entries. | Two entries: (1) active -> suspended, (2) suspended -> active. No duplicates. |

## 6. Postconditions
- Employment history contains exactly the expected number of entries (no duplicates from retried requests).
- The idempotency key mechanism prevented duplicate side effects.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
