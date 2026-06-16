---
id: TC-ADM-ISO-006
user_story: US-ADM-002
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-ADM-ISO-006: Monitoring endpoints reject non-system tenant context (no tenant may read platform aggregates)

## 1. Test Objective
Verify AC-5 / BR-1 isolation on the request boundary: the monitoring endpoints are system-context only. A request carrying a regular tenant context (a tenant-scoped JWT, or a `*.yourhrm.com` tenant subdomain rather than `admin.*`) must be rejected — it must NOT receive cross-tenant platform aggregates, and a tenant must not be able to read another tenant's monitoring detail by ID injection.

## 2. Related Requirements
- User Story: US-ADM-002
- Acceptance Criteria: AC-5
- Business Rules: BR-1
- Cross-cutting: mandatory multi-tenant isolation; tenant-resolution + authorize boundary

## 3. Preconditions
- A tenant-scoped JWT for tenant "acme" and a separate tenant "globex" exist.
- Monitoring endpoints (`/api/v1/admin/monitoring/overview`, `.../tenants/{id}`) exist and require system context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant JWT | acme | non-system context |
| Target host | acme.yourhrm.com (tenant) vs admin.yourhrm.com (system) | resolution probe |
| Injected ID | globex tenant_id | IDOR probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With the acme tenant JWT, call `GET /api/v1/admin/monitoring/overview` | Rejected 403 — no platform aggregates returned to a tenant context. |
| 2 | Send the same request via the `acme.yourhrm.com` tenant subdomain instead of `admin.*` | Tenant resolution yields a tenant (not system) context; the monitoring endpoint is denied 403 — monitoring is admin/system-host only. |
| 3 | With no/invalid bearer, call any monitoring endpoint | 401 Unauthorized. |
| 4 | With the acme JWT, call `.../monitoring/tenants/{globexId}` (IDOR) | Denied — 403 (non-system caller) / 404 (existence not disclosed); globex's monitoring detail is NOT returned. |
| 5 | Confirm no aggregate leakage on any rejected call | None of the rejected responses contain cross-tenant counts, health, or job data. |

## 6. Postconditions
- Monitoring is reachable only from system context; tenant contexts are blocked with no aggregate leakage and no cross-tenant detail access.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
