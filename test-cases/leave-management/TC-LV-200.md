---
id: TC-LV-200
user_story: US-LV-010
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-200: Another employee (same tenant) cannot cancel someone else's leave -- 403 / not-found, no IDOR (BR-1)

## 1. Test Objective
Verify that an employee cannot cancel a leave request that belongs to a different employee within the same tenant: a direct-object-reference attack (guessing/passing another employee's request id) is denied with 403 (or 404 to avoid leaking existence), with no state change (BR-1, NFR-2).

## 2. Related Requirements
- User Story: US-LV-010
- Business Rules: BR-1
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme".
- Employee "Jane Smith" has an APPROVED future request R-jane.
- Employee "Mark Otieno" (same tenant, NOT Jane's manager, NOT the requester) is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Actor | Mark Otieno | unrelated employee |
| Target | R-jane (Jane's request id) | another employee's leave |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Mark, call `POST /api/v1/leaves/{R-jane}/cancel` with a reason | Denied with HTTP 403 (or 404 to avoid disclosing existence); the cancel does not apply. |
| 2 | Inspect R-jane | Status unchanged; no `cancelled_at`, no reversal ledger entry, no Cancelled history row. |
| 3 | Verify ownership enforcement | The handler resolves the acting employee from `ICurrentUser.UserId` and confirms the request's `employee_id` matches before allowing cancellation (no IDOR). |
| 4 | Re-read Jane's balance | Unchanged (Mark's attempt did not restore Jane's balance). |

## 6. Postconditions
- Cross-employee cancellation is blocked; R-jane is untouched; ownership is enforced server-side.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
