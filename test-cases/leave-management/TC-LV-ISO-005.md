---
id: TC-LV-ISO-005
user_story: US-LV-002
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-005: Tenant A cannot see Tenant B's entitlement rules or overrides

## 1. Test Objective
Verify that entitlement rule and override data is fully tenant-isolated: a user authenticated in Tenant A cannot list, retrieve, or modify any entitlement rules or per-employee overrides belonging to Tenant B. This tests EF Core global query filters on the `leave_entitlement_rule` and `leave_entitlement_override` tables.

## 2. Related Requirements
- User Story: US-LV-002
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with entitlement rules: Annual Leave + Engineering = 25 days, Sick Leave default = 7 days.
- Tenant "globex" exists with entitlement rules: Annual Leave + Sales = 22 days, Annual Leave default = 18 days.
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has 2 entitlement rules |
| Tenant B | globex | Has 2 entitlement rules |
| Auth Context | acme | User authenticated in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "acme" tenant | JWT contains `tenant_id` for acme. |
| 2 | Send `GET /api/v1/leave-entitlement-rules` | Response returns only acme's rules (Annual Leave + Engineering, Sick Leave default). Zero globex rules. |
| 3 | Verify globex's "Annual Leave + Sales = 22 days" rule is NOT in the response | No cross-tenant entitlement rules visible. |
| 4 | Attempt `GET /api/v1/leave-entitlement-rules/{globex_rule_id}` using the UUID of a globex rule | Response is 404 Not Found (EF global query filter excludes it). |
| 5 | Attempt `PUT /api/v1/leave-entitlement-rules/{globex_rule_id}` with modified data | Response is 404 Not Found. Cannot modify cross-tenant rules. |
| 6 | Attempt `DELETE /api/v1/leave-entitlement-rules/{globex_rule_id}` | Response is 404 Not Found. Cannot delete cross-tenant rules. |
| 7 | Create a per-employee override in acme; then attempt to retrieve a globex override by ID | Override created in acme (201). Globex override retrieval returns 404. |
| 8 | Switch to "globex" tenant context and verify | `GET /api/v1/leave-entitlement-rules` returns only globex's rules. No acme rules visible. |
| 9 | Verify at database level | Direct SQL `SELECT * FROM leave_entitlement_rule WHERE tenant_id = acme_id` returns only acme's rules; `SELECT * FROM leave_entitlement_rule WHERE tenant_id = globex_id` returns only globex's rules. |

## 6. Postconditions
- No cross-tenant data exposure occurred for entitlement rules or overrides.
- EF Core global query filters correctly scope all entitlement queries by tenant_id.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
