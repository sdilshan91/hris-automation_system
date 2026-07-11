---
id: TC-NTF-ISO-014
user_story: US-NTF-004
module: Notifications & Audit
priority: critical
type: security
status: fail
created: 2026-06-17
---

# TC-NTF-ISO-014: Cross-tenant audit-row ID access -> 404 (not 403); missing tenant context rejected

## 1. Test Objective
Verify that requesting a specific Tenant B audit record by its ID while authenticated in Tenant A
yields a 404 (existence not disclosed -- not 403), and that an audit-log request with no resolvable
tenant context is rejected. Confirms AC-5 / NFR-2 against direct ID injection and missing context.

## 2. Related Requirements
- User Story: US-NTF-004
- Acceptance Criteria: AC-5 (Tenant B records invisible to Tenant A)
- Non-Functional: NFR-2 (tenant isolation; RLS deferred -> EF filter)
- Functional Requirements: FR-8 (tenant_id from session, never client-trusted)

## 3. Preconditions
- A known Tenant B audit_log id `bRowId` exists.
- `adminA` is authenticated in Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| cross-tenant audit id | bRowId | belongs to Tenant B |
| forged tenant in body/query | Tenant B | attempted override |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `adminA`, GET the audit record by `bRowId` (a Tenant B row) | HTTP 404 -- existence NOT disclosed (404, not 403); the row is invisible due to the EF tenant filter |
| 2 | As `adminA`, attempt to override tenant via a query param / body field set to Tenant B | The override is ignored; tenant_id comes from the session; result is still Tenant A-scoped (404 for the Tenant B row) |
| 3 | Issue an audit-log request with NO resolvable tenant context (no/invalid subdomain + no X-Tenant-Subdomain) | Request is rejected (no tenant resolved) -- it does NOT fall back to returning all tenants' rows |
| 4 | Issue an unauthenticated audit-log request | Rejected with 401; no audit data returned |

## 6. Postconditions
- Cross-tenant ID access returns 404; missing/forged tenant context never widens visibility.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
