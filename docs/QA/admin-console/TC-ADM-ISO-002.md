---
id: TC-ADM-ISO-002
user_story: US-ADM-001
module: Admin Console
priority: critical
type: security
status: fail
exec_note: >-
  2026-06-30 API: no header->400, unknown subdomain->404, tenant-JWT POST /system/tenants->403 (these correct). BUT step2 mismatch (isoa JWT + header isob) NOT rejected -> 200 + isob employee-by-id LEAKED. = existing BUG-003. IDOR-by-foreign-header unblocked.
created: 2026-06-16
---

# TC-ADM-ISO-002: API rejects requests without a valid tenant/system context + blocks cross-tenant IDOR

## 1. Test Objective
Verify that tenant-scoped APIs for the newly provisioned tenant reject requests with missing, invalid, or mismatched tenant context, and that cross-tenant IDOR is blocked. The admin provisioning API requires the system-tenant context (SystemAdmin); tenant-data APIs require the resolved tenant context. A request whose subdomain resolution / JWT tenant claim does not match the targeted resource is denied.

## 2. Related Requirements
- User Story: US-ADM-001
- Acceptance Criteria: AC-6
- Business Rules: BR-1
- Cross-cutting: mandatory multi-tenant isolation

## 3. Preconditions
- Tenant A (`alpha`) freshly provisioned; Tenant B (`beta`) pre-exists with a known resource id.
- The tenant resolution middleware runs from the request subdomain (dev: `X-Tenant-Subdomain` header).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing tenant context | request to a tenant API with no resolvable subdomain | should be rejected |
| Mismatched context | alpha JWT + `X-Tenant-Subdomain: beta` | claim/subdomain mismatch |
| Cross-tenant id | beta resource id requested under alpha context | IDOR probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call a tenant-data API with no resolvable tenant (unknown/blank subdomain) | Rejected (tenant cannot be resolved); no data returned. |
| 2 | Call a tenant API with an alpha JWT but `X-Tenant-Subdomain: beta` (mismatch) | Rejected — the JWT tenant claim and resolved tenant must agree; no beta data returned. |
| 3 | As alpha, request a beta resource by id (IDOR) | 404 Not Found (per Test Hints, not 403) — the query filter hides beta's row; existence is not disclosed. |
| 4 | Call `POST /api/v1/admin/tenants` with a tenant-scoped (non-system) JWT | Rejected (403) — provisioning requires the system-tenant SystemAdmin context (BR-1). |

## 6. Postconditions
- All missing/invalid/mismatched-context and cross-tenant id requests are denied; no leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
