---
id: TC-LV-122
user_story: US-LV-006
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-122: Self-scope -- an employee cannot view another employee's balance, ledger, or upcoming leaves

## 1. Test Objective
Verify that the my-balance / my-ledger / my-upcoming endpoints resolve the employee strictly from the authenticated identity and ignore any attempt to request another employee's data, so one employee cannot read a colleague's balance even within the same tenant (NFR-3, FR-1, FR-3, FR-4).

## 2. Related Requirements
- User Story: US-LV-006
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-1, FR-3, FR-4
- Test Hint: Employee in Tenant A must see only their own balance data.

## 3. Preconditions
- Tenant "acme" active; employees "Nina Patel" and "Omar Diaz" both have balances/ledger data.
- Nina is authenticated; Omar's employeeId is known to the test.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Authenticated | Nina Patel | -- |
| Target | Omar Diaz | Same tenant, different employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Nina, call `GET /api/v1/leaves/my-balance` | Returns Nina's balances only; resolved from her identity (`ICurrentUser` -> Employee.UserId), not a client-supplied id. |
| 2 | As Nina, attempt to pass Omar's employeeId via any parameter/header on my-balance/my-ledger/my-upcoming | The parameter is ignored; the response still contains Nina's own data exclusively (no Omar rows). |
| 3 | As Nina, call `my-ledger?leaveTypeId={x}` and compare against Omar's known ledger | None of Omar's ledger entries appear. |
| 4 | Verify there is no "by employeeId" employee-facing balance endpoint reachable by Nina | Any manager/HR balance lookups require the appropriate permission and are not accessible to a plain Employee. |

## 6. Postconditions
- Employee can only view their own balance/ledger/upcoming data.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
