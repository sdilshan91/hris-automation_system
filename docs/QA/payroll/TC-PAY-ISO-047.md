---
id: TC-PAY-ISO-047
user_story: US-PAY-012
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PAY-ISO-047: Audit-write isolation -- audit_log rows are server-tenant-stamped (TenantInterceptor); a Tenant A operation never writes a Tenant B audit entry; injected tenant_id/actor in the request body is ignored

## 1. Test Objective
Verify AC-5 and FR-2/FR-3/FR-8: every `audit_log` entry is stamped with the writing tenant's tenant_id and the server-derived actor -- not values supplied by the client. A payroll write performed in Tenant A always produces an audit entry under tenant A; a forged request that injects a foreign `tenant_id` or `actor_user_id` in the body has those fields ignored, so no operation can write an audit entry attributed to another tenant.

## 2. Related Requirements
- User Story: US-PAY-012
- Acceptance Criteria: AC-5
- Functional Requirements: FR-2, FR-3, FR-8
- Business Rules: BR-1 (every write logs), BR-7 (system actor not null)
- Non-Functional Requirements: NFR-4 (immutable)

## 3. Preconditions
- Tenants "acme" (A) and "globex" (B) Active; a salary component in A.
- HR Officer authenticated in A; ability to inject body fields.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Operation | update A's component | triggers audit write |
| Injected tenant_id | B (globex) | must be ignored |
| Injected actor_user_id | a globex user | must be ignored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As A, update a salary component normally. | The resulting audit_log row has tenant_id=A (server-stamped via TenantInterceptor) and actor_user_id = the A user from the JWT; not client-supplied. |
| 2 | As A, repeat the update but inject `tenant_id=B` in the request body/payload. | The injected tenant_id is IGNORED; the audit row is still written under tenant A; no row appears under B. |
| 3 | As A, inject `actor_user_id` = a globex user id. | The injected actor is ignored; actor_user_id resolves to the authenticated A user (or the SYSTEM actor for job-driven writes, BR-7). |
| 4 | As B, query the audit trail. | None of the entries from steps 1-3 appear for B -- A's operations never wrote a B-attributed audit entry. |
| 5 | Verify the entries are append-only + immutable. | The written rows cannot subsequently be altered/deleted via the API (NFR-4, cross-ref TC-PAY-012-07). |
| 6 | (RLS note) Confirm the mechanism. | tenant_id is stamped by TenantInterceptor + read-filtered by EF global query filters; Postgres RLS on audit_log noted as an extension point (AC-5/FR-3). |

## 6. Postconditions
- All audit rows are server-tenant-stamped under the writing tenant; injected tenant_id/actor ignored; no cross-tenant audit write.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
