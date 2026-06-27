---
id: TC-NTF-ISO-018
user_story: US-NTF-005
module: Notifications & Audit
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-NTF-ISO-018: Actor autocomplete + actor filter are tenant-scoped; cross-tenant actor_user_id yields no foreign rows

## 1. Test Objective
Verify the new actor-autocomplete and actor-filter deltas do not become a cross-tenant leak vector:
the autocomplete suggests only current-tenant users, and supplying another tenant's actor_user_id in
the filter (e.g. by hand-editing the URL/request) returns ZERO rows rather than that tenant's audit
records.

## 2. Related Requirements
- User Story: US-NTF-005
- Acceptance Criteria: AC-5 (tenant isolation), AC-2 (actor filter)
- Functional Requirements: FR-2 (actor autocomplete), FR-3 (URL-based filter state)
- Non-Functional: NFR-3 (tenant isolation; RLS deferred -> EF filter)

## 3. Preconditions
- Tenant A user "John Doe" and Tenant B user "John Diaz" exist.
- Tenant B has audit rows authored by John Diaz.
- `adminA` authenticated in Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A actor | John Doe (id: uA) | autocomplete-suggestable to adminA |
| Tenant B actor | John Diaz (id: uB) | must NOT be suggestable; uB injected into filter |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `adminA`, type "john" in the actor autocomplete | Only Tenant A users (John Doe) suggested; John Diaz (Tenant B) never appears |
| 2 | Hand-edit the URL to set actor_user_id = uB (Tenant B's user) and load | The audit list returns ZERO rows (the EF tenant filter excludes Tenant B rows); Tenant B's audit records are NOT disclosed |
| 3 | Confirm no error oracle leak | The empty result does not differ in a way that confirms uB exists in another tenant |
| 4 | As `adminA`, use uA (valid Tenant A actor) | Tenant A rows for John Doe returned normally |
| 5 | Confirm autocomplete endpoint authz | The autocomplete lookup endpoint requires Audit.View and is tenant-scoped (no enumeration of foreign-tenant users) |

## 6. Postconditions
- Actor autocomplete + filter cannot surface or query another tenant's actors or rows.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
