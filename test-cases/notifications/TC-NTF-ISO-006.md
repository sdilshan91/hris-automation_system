---
id: TC-NTF-ISO-006
user_story: US-NTF-002
module: Notifications & Audit
priority: critical
type: security
status: fail
created: 2026-06-17
---

# TC-NTF-ISO-006: Missing tenant context rejected; cross-tenant template ID injection -> 404

## 1. Test Objective
Verify that template-management API requests without a resolvable tenant context are rejected, and that
a client cannot reach another tenant's template by injecting its template_id or a forged tenant value —
the server derives tenant_id from the session, never from the client.

## 2. Related Requirements
- User Story: US-NTF-002
- Acceptance Criteria: AC-5 (cross-tenant invisibility)
- Functional Requirements: FR-10 (tenant_id set from session context), FR-1, FR-9
- Non-Functional: NFR-2 (tenant isolation; RLS deferred -> EF filters)

## 3. Preconditions
- Tenant A override and Tenant B exist. `adminA` authenticated in Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A template_id | TPL-A-001 | Tenant A's override |
| forged body field | tenant_id = Tenant B | client-supplied, must be ignored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call a template API with no resolvable tenant context (no subdomain / no X-Tenant-Subdomain) | Request rejected (tenant resolution fails) — no template data returned |
| 2 | As `adminA`, GET another tenant's template by id (e.g. a Tenant B template_id) | 404 Not Found (out of Tenant A scope; existence not disclosed) |
| 3 | As `adminA`, POST/PUT a save with a body that includes `tenant_id` = Tenant B | The server IGNORES the client tenant_id and stamps tenant_id = Tenant A from the session (FR-10); no Tenant B row created |
| 4 | As `adminA`, attempt reset-to-default targeting a Tenant B template_id | 404 Not Found; no Tenant B override affected |
| 5 | Inspect persisted rows after step 3 | Any created/updated override is owned by Tenant A only |

## 6. Postconditions
- No template access without tenant context; cross-tenant id/tenant injection yields 404 / is ignored.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
