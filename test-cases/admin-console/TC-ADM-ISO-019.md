---
id: TC-ADM-ISO-019
user_story: US-ADM-007
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-019: Mutating workflow endpoints require valid tenant context + TenantAdmin authz; writes are tenant-stamped

## 1. Test Objective
Verify the write-isolation guarantees for workflow definition management: (a) mutating endpoints reject requests with no resolved tenant context; (b) they require `TenantAdmin`/`TenantOwner` authz (BR-1); (c) newly created `WorkflowDefinition`/`WorkflowStep` rows are auto-stamped with the resolved `TenantId` by the `TenantInterceptor`, so a payload cannot create a workflow for a foreign tenant.

## 2. Related Requirements
- User Story: US-ADM-007
- Acceptance Criteria: AC-2/AC-3 (create/edit)
- Functional Requirements: FR-7 (tenant-scoped via ITenantContext), FR-1
- Business Rules: BR-1, BR-7

## 3. Preconditions
- Dana is `TenantAdmin` of Acme.
- A request can be issued with a missing/unresolved tenant context (e.g. no resolvable subdomain / missing `X-Tenant-Subdomain` dev header).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| spoofed tenant_id in body | <Tenant Beta id> | attempt to mis-stamp |
| valid WF payload | entity=Leave, 1 valid step | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | POST create workflow with NO resolved tenant context | Rejected (no tenant context) — not created; tenant resolution gate blocks the write. |
| 2 | As Dana, POST create workflow but inject a `tenant_id = Beta` in the body | Created workflow is stamped to ACME (the resolved tenant), NOT Beta — `TenantInterceptor` ignores any client-supplied tenant id. |
| 3 | Verify the new row's TenantId | Equals Acme's tenant id; the workflow appears only in Acme's list, never Beta's. |
| 4 | Repeat create as a non-admin role (Manager) | 403 (BR-1) regardless of tenant context. |

## 6. Postconditions
- Writes are always stamped to the resolved tenant; no context = no write; non-admin = 403.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
