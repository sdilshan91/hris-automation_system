---
id: TC-PAY-ISO-016
user_story: US-PAY-004
module: Payroll
priority: high
type: security
status: pass
created: 2026-06-15
---

# TC-PAY-ISO-016: Blob storage layout, ZIP staging, and any download/preview cache are tenant-scoped -- no cross-tenant leak of PDF bytes or paths

## 1. Test Objective
Verify AC-4 / NFR-6: the at-rest and in-transit artifacts of payslip delivery are tenant-partitioned end to end. Specifically: (a) the blob layout root-partitions by `{tenantId}` so no two tenants share a folder; (b) any temporary ZIP staging file for "Download All" is written under the requesting tenant's prefix (or a per-tenant temp scope) and cleaned up, never readable cross-tenant; (c) any cache/CDN key or signed-URL used to serve a PDF/ZIP is tenant-scoped (keyed with tenant_id), so a cached/signed artifact for Tenant A is never served to Tenant B. If no cache/CDN/signed-URL layer exists today, the cache steps are CONDITIONAL and assert that no shared/global key is used and serving goes straight through the tenant-filtered API.

## 2. Related Requirements
- User Story: US-PAY-004
- Acceptance Criteria: AC-4
- Non-Functional Requirements: NFR-6 (path validation), NFR-2 (size)
- Functional Requirements: FR-5 (storage layout), FR-6 (single + ZIP download)

## 3. Preconditions
- Tenant "acme" (A) and "globex" (B), each with generated payslips for their own runs.
- HR users in each tenant; a Download-All performed in each.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| A blob root | {acmeTenantId}/payroll/... | partitioned |
| B blob root | {globexTenantId}/payroll/... | partitioned |
| ZIP staging | per-tenant temp scope | not shared |
| Cache/URL key | includes tenant_id | CONDITIONAL on cache/CDN |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Generate payslips in both acme and globex; inspect the blob layout. | Each tenant's PDFs live ONLY under its own `{tenantId}/payroll/...` root; no shared folder; no file of one tenant under another's prefix. |
| 2 | Run "Download All" in acme, then in globex. | Each ZIP contains ONLY that tenant's payslips; the staging file is written under the requesting tenant's scope and cleaned up; no globex slip appears in acme's ZIP or vice versa. |
| 3 | If a cache/CDN/signed-URL serves PDFs/ZIPs, inspect the key/URL. | Key/path includes `tenant_id` (e.g. `tenant:{tenantId}:payslip:{runId}:{employeeId}`); a cached/signed artifact for acme cannot be fetched under globex's key. (CONDITIONAL -- if no cache today, assert direct tenant-filtered serving with no shared key.) |
| 4 | As globex, attempt to fetch a download/preview using acme's cache key / signed URL. | Denied/not found; the tenant-scoped key prevents reuse across tenants. |
| 5 | Verify temp/staging cleanup. | No residual cross-tenant-readable temp ZIP left after download completes/expires. |
| 6 | Re-run and confirm no bleed in either direction. | acme and globex artifacts remain fully disjoint at rest, in staging, and in cache. |

## 6. Postconditions
- Blob layout, ZIP staging, and any cache/URL keying are tenant-scoped; no cross-tenant PDF/path/byte leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
