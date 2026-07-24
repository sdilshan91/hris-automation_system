---
id: TC-AUTH-118
user_story: US-AUTH-012
module: Authentication
priority: high
type: functional
status: draft
created: 2026-07-24
---

# TC-AUTH-118: Per-entry validation of Entra `tid` (GUID) and email domains -- malformed entries rejected

## 1. Test Objective
Verify AC-3 / FR-4: each `allowed_entra_tenant_ids` value must be a well-formed GUID and each `allowed_email_domains` value must be a syntactically valid domain. A malformed `tid` (non-GUID) and a malformed domain are rejected **per entry** (the error points at the offending value), and the save does not partially persist a mixed valid/invalid list.

## 2. Related Requirements
- User Story: US-AUTH-012
- Acceptance Criteria: AC-3
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" plan has `Sso = true`; `admin-a@acme.com` is a tenant admin.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| valid tid | 7c9e6679-7425-40de-944b-e07fc1f90ae7 | Well-formed GUID |
| malformed tid | not-a-guid-123 | Non-GUID -> reject |
| valid domain | acme.com | RFC-valid |
| malformed domain | acme..com | Double dot -> reject |
| malformed domain 2 | http://acme.com | URL, not a bare domain -> reject |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In the SSO card, add `tid = not-a-guid-123`. | Inline per-entry error on that chip/row ("Enter a valid directory (tenant) ID / GUID"); submit blocked while invalid. |
| 2 | Add domain `acme..com` and `http://acme.com`. | Inline per-entry errors on each invalid domain row. |
| 3 | Send `PUT /api/v1/tenant/auth-settings` with a mixed list: valid `tid` + `not-a-guid-123`, valid `acme.com` + `acme..com`. | HTTP 400 Bad Request; validation errors identify the specific invalid entries (`not-a-guid-123`, `acme..com`), not a generic failure. |
| 4 | Re-read `GET /api/v1/tenant/auth-settings`. | Neither the valid nor invalid entries from the rejected write are persisted (no partial save). |
| 5 | Resubmit with only the valid `tid` and `acme.com`. | HTTP 200 OK; the two valid entries persist. |

## 6. Postconditions
- Only well-formed `tid`/domain values are ever stored; malformed input is rejected atomically.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
