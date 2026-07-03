---
id: TC-ADM-ISO-015
user_story: US-ADM-006
module: Admin Console
priority: critical
type: security
status: pass
exec_note: "2026-07-03 Read-equivalent isolation probe (write arm safety-barred): cross-tenant GET on this resource (acme JWT + techoneglobal header) => 403 cross_tenant_denied; same-tenant GET => 200. Guard blocks cross-tenant access to the mutating resource. No leak."
created: 2026-06-17
---

# TC-ADM-ISO-015: Branding file storage is tenant-scoped — Tenant B cannot reach Tenant A's branding path

## 1. Test Objective
Verify BR-6 and the file-storage isolation Test Hint: branding files are stored under the tenant-scoped prefix `{tenantId}/branding/`, so Tenant A's logo/favicon live under A's prefix and Tenant B's under B's. Tenant B cannot read, overwrite, or enumerate Tenant A's branding files; a request from B's context for an A-prefixed path returns access-denied/404. Each tenant's `branding.*_url` resolves only within its own prefix.

## 2. Related Requirements
- User Story: US-ADM-006
- Acceptance Criteria: AC-2, AC-5
- Functional Requirements: FR-2
- Business Rules: BR-6 (tenant-scoped branding files)
- Test Hints: "Attempt to access Tenant A's logo URL from Tenant B's context; verify access denied."

## 3. Preconditions
- Acme (A) has uploaded a logo + favicon (stored under `{Acme}/branding/`); Beta (B) has its own (under `{Beta}/branding/`).
- Admins for each tenant authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| A path | `{Acme-tenantId}/branding/logo.png` | A only |
| B path | `{Beta-tenantId}/branding/logo.png` | B only |
| attacker | Beta admin / Beta context | must not reach A path |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm A's logo is stored under `{Acme}/branding/` and B's under `{Beta}/branding/` | Distinct tenant prefixes; no shared/root path. |
| 2 | As Beta admin, request Acme's branding path / URL | Access denied / 404; A's file not served to B. |
| 3 | As Beta admin, attempt to upload/overwrite to an Acme-prefixed path | Rejected; write is forced into Beta's own prefix (TenantInterceptor / path derived from `ITenantContext`, not client input). |
| 4 | As Beta admin, attempt to enumerate the branding store | Sees only Beta's prefix; cannot list Acme's files. |
| 5 | `GET` branding for each tenant | Each tenant's `branding.*_url` resolves only within its own prefix; no cross-prefix URL returned. |

## 6. Postconditions
- Branding files strictly partitioned by tenant prefix; no cross-tenant read/write/enumerate.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
