---
id: TC-LV-ISO-037
user_story: US-LV-010
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-037: An employee in Tenant A cannot cancel a leave request in Tenant B (cross-tenant data visibility / action) (NFR-2, Test Hint)

## 1. Test Objective
Verify the cancellation flow is tenant-isolated: an authenticated employee in Tenant A cannot cancel (or even resolve) a leave request that belongs to Tenant B, even by passing Tenant B's request id directly. The request resolves to nothing under Tenant A's filter (404/403) and Tenant B's request is untouched (NFR-2, Test Hint).

## 2. Related Requirements
- User Story: US-LV-010
- Non-Functional Requirements: NFR-2
- Test Hint: "Employee in Tenant A cannot cancel a leave request in Tenant B."

## 3. Preconditions
- Tenant "acme" (employee Jane; an APPROVED future request R-acme).
- Tenant "globex" (employee Kofi; an APPROVED future request R-globex).
- Jane is authenticated under `acme.yourhrm.com`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| R-acme | Jane (acme), Approved future | acme-scoped |
| R-globex | Kofi (globex), Approved future | must be unreachable from acme |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jane (acme), call `POST /api/v1/leaves/{R-globex}/cancel` with a reason | Resolves to nothing under acme's tenant filter -> 404 (or 403); no cancellation applied. |
| 2 | Inspect R-globex (under globex) | Unchanged -- still Approved; no `cancelled_at`, no reversal ledger entry, no Cancelled history row. |
| 3 | Inspect Kofi's balance (globex) | Unchanged -- Jane's cross-tenant attempt did not restore globex's balance. |
| 4 | Confirm same-tenant cancel still works | Jane cancelling her own R-acme succeeds normally -- isolation does not block legitimate same-tenant cancellation. |

## 6. Postconditions
- Cross-tenant cancellation is impossible; Tenant B's request, ledger, and balance are untouched by a Tenant A actor.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
