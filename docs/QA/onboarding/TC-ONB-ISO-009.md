---
id: TC-ONB-ISO-009
user_story: US-ONB-003
module: Onboarding / Offboarding
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-ONB-ISO-009: Missing/invalid tenant context + cross-tenant ID injection -> 404

## 1. Test Objective
Verify NFR-2 / FR-7: requests with no resolvable tenant context are rejected, and a request that injects another tenant's task-instance id returns 404 (not 403) so the existence of cross-tenant data is not disclosed. No completion, file access, or notification side effect occurs.

## 2. Related Requirements
- User Story: US-ONB-003
- Acceptance Criteria: AC-2, AC-3
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-2
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: Cross-tenant ID injection asserts 404, not 403 (existence not disclosed), consistent with the rest of the platform. Tenant resolution is via subdomain (`TenantResolutionMiddleware`) / dev `X-Tenant-Subdomain` header.

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) exist; globex has a task instance TK-GLOBEX-1.
- An employee authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| foreign task id | TK-GLOBEX-1 | belongs to globex |
| tenant context | acme (valid) / none (invalid) | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call the checklist/completion API with no resolvable tenant context (missing subdomain/header) | Rejected; no onboarding data returned (NFR-2). |
| 2 | As the acme employee, `GET .../tasks/{TK-GLOBEX-1}` | 404 Not Found — globex's task is invisible; existence not disclosed (NFR-2, FR-7). |
| 3 | As the acme employee, `POST .../tasks/{TK-GLOBEX-1}/complete` | 404 Not Found; no completion, no file change, no notification (FR-7). |
| 4 | Attempt to override tenant via payload/header to globex while authenticated as acme | Ignored; the resolved/session tenant governs; no cross-tenant access. |

## 6. Postconditions
- No-tenant requests rejected; cross-tenant id access returns 404 with zero side effects.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
