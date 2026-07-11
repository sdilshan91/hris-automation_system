---
id: TC-NTF-ISO-009
user_story: US-NTF-003
module: Notifications & Audit
priority: critical
type: security
status: fail
created: 2026-06-17
---

# TC-NTF-ISO-009: Same user's preferences in Tenant X are independent from Tenant Y

## 1. Test Objective
Verify that a cross-tenant user (the same person who is a member of both Tenant X and Tenant Y) has a
completely independent preference set per tenant membership. Changes made in Tenant X do not appear or
take effect in Tenant Y, and vice versa.

## 2. Related Requirements
- User Story: US-NTF-003
- Acceptance Criteria: AC-5 (preferences in Tenant A independent from Tenant B; each membership has its own set)
- Non-Functional: NFR-2 (tenant isolation via EF Core global query filters; Postgres RLS deferred)
- Business Rules: BR-4 (preferences per tenant membership; cross-tenant users have independent preferences)

## 3. Preconditions
- The same physical user `userU` is a member of both Tenant X and Tenant Y.
- In Tenant X, `userU` has "Leave Updates" Email = OFF; in Tenant Y, defaults are untouched (Email = ON).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| user | userU | member of X and Y |
| Tenant X: Leave Updates Email | false | customized in X |
| Tenant Y: Leave Updates Email | true | default in Y |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `userU` in Tenant X, confirm "Leave Updates" Email = OFF | Tenant X preferences show Email OFF for Leave Updates |
| 2 | Switch/log into Tenant Y as `userU` and open preferences | Tenant Y shows "Leave Updates" Email = ON (Tenant X's OFF does NOT carry over) |
| 3 | Trigger a leave approval for `userU` in Tenant Y | Email IS sent (Tenant Y pref enabled) -- independent from Tenant X |
| 4 | Change a preference in Tenant Y | Tenant X preferences for `userU` are unaffected |
| 5 | Inspect persisted preference rows | Tenant X and Tenant Y each have their own rows for `userU` carrying their respective tenant_id; no shared/overwritten record |

## 6. Postconditions
- `userU`'s preferences are isolated per tenant membership; no cross-tenant carryover.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
