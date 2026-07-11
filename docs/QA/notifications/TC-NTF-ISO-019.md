---
id: TC-NTF-ISO-019
user_story: US-NTF-005
module: Notifications & Audit
priority: critical
type: security
status: fail
created: 2026-06-17
---

# TC-NTF-ISO-019: Cross-tenant audit-row ID access -> 404 (not 403); missing tenant context rejected

## 1. Test Objective
Verify that requesting a specific audit record's detail by an ID belonging to another tenant returns
404 (existence not disclosed), and that a request with no resolvable tenant context is rejected. This
protects the detail-panel / export endpoints introduced/used by the viewer from IDOR.

## 2. Related Requirements
- User Story: US-NTF-005
- Acceptance Criteria: AC-5 (tenant isolation), AC-3 (detail view)
- Functional Requirements: FR-4 (detail view), FR-5 (export)
- Non-Functional: NFR-3 (isolation; RLS deferred -> EF filter)

## 3. Preconditions
- Tenant A audit row id `rA` and Tenant B audit row id `rB` exist.
- `adminA` authenticated in Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A row | rA | adminA may view |
| Tenant B row | rB | adminA must NOT view |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `adminA`, open detail for rA | Detail panel renders rA (Tenant A) |
| 2 | As `adminA`, request detail for rB (Tenant B id, e.g. via direct API/URL) | 404 Not Found -- NOT 403 (existence of rB is not disclosed) |
| 3 | As `adminA`, attempt to export filtered by an injected Tenant B scope | No Tenant B rows are exported; the EF tenant filter constrains the export to Tenant A |
| 4 | Issue an audit list/detail request with no resolvable tenant context | Request rejected (no rows / 4xx); the viewer never returns un-tenant-scoped data |
| 5 | Confirm mechanism | The 404 + empty-scope behavior is enforced by the EF global query filter, not caller input |

## 6. Postconditions
- Cross-tenant record access is indistinguishable from non-existent (404); no leakage via detail/export.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
