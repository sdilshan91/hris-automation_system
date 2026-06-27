---
id: TC-ONB-ISO-002
user_story: US-ONB-001
module: Onboarding / Offboarding
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-ONB-ISO-002: API rejects missing/invalid tenant context + cross-tenant ID injection (IDOR)

## 1. Test Objective
Verify AC-5 and FR-5: the onboarding template API requires a valid resolved tenant context and never trusts a client-supplied tenant identifier. Requests with no/invalid tenant context are rejected; a Tenant A user requesting a Tenant B `template_id` directly (IDOR probe) receives 404 (existence not disclosed), not 403; and a `tenant_id` injected in the body or query is ignored in favor of the session context.

## 2. Related Requirements
- User Story: US-ONB-001
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5
- Cross-cutting: mandatory multi-tenant isolation, IDOR

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) exist.
- A template `T_B` exists in `globex`; its `template_id` is known to the tester.
- A valid acme-scoped JWT is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Foreign template id | T_B (globex) | IDOR target |
| Injected tenant_id | globex's id in body/query | must be ignored |
| Tenant context | missing / unresolved | reject |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/onboarding/templates` with no resolvable tenant context (no subdomain / no `X-Tenant-Subdomain`) | Request rejected (no tenant-scoped data returned); not served against a default/all tenant. |
| 2 | As an acme user, `GET /api/v1/onboarding/templates/{T_B}` (a globex id) | Response 404 Not Found — existence is NOT disclosed (404, not 403), since the EF query filter scopes the lookup to acme. |
| 3 | As an acme user, `POST /api/v1/onboarding/templates` with `tenant_id = globex` injected in the body | The injected `tenant_id` is IGNORED; the template is created under acme (FR-5: tenant_id from session, never user input). |
| 4 | As an acme user, attempt to deactivate/clone `T_B` by id | 404 Not Found; no globex template is modified or cloned. |

## 6. Postconditions
- No cross-tenant access via missing context, ID injection, or body-supplied tenant_id; existence not disclosed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
