---
id: TC-ADM-ISO-030
user_story: US-ADM-010
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-030: EF query filter scopes export queries; ExportRequest + storage path tenant-stamped

## 1. Test Objective
Verify the active isolation mechanism for the export pipeline: each per-entity export query reads through the EF Core global query filter bound to the resolved tenant (read isolation), and the `ExportRequest` write + the storage path are tenant-stamped via `TenantInterceptor` (write isolation) — so an export is structurally bound to one tenant at both read and write layers.

## 2. Related Requirements
- User Story: US-ADM-010
- Acceptance Criteria: AC-5 (complete data isolation)
- Functional Requirements: FR-2 (per-entity query filtered by tenant_id), FR-6 (storage path `{tenantId}/exports/{id}/`)
- (Platform) EF global query filter (read) + TenantInterceptor (write); PostgreSQL RLS is DEFERRED — see TC-ADM-ISO-031.

## 3. Preconditions
- Two tenants A and B with data; export pipeline available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| read isolation | EF global query filter | per-entity export queries |
| write isolation | TenantInterceptor | ExportRequest.TenantId stamped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run an export under Tenant A's resolved context | Every per-entity query returns only A's rows (global query filter applied; no `IgnoreQueryFilters()` on the export path). |
| 2 | Inspect the created ExportRequest row | `TenantId` = A (auto-stamped by TenantInterceptor on insert). |
| 3 | Inspect the bundle storage path | `{aTenantId}/exports/{export_id}/export_bundle.zip` — A-scoped, never B. |
| 4 | Attempt to resolve/download an ExportRequest belonging to B under A's context | Filtered out / not found (B's ExportRequest invisible to A). |

## 6. Postconditions
- No state change; read + write isolation confirmed on the export path.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
