---
id: TC-CHR-073
user_story: US-CHR-001
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-073: Profile photo tenant-isolated storage path (AC-4)

## 1. Test Objective
Verify that profile photos for employees in different tenants are stored in tenant-isolated object storage paths, and that Tenant A cannot access Tenant B's profile photos via signed URLs or direct path enumeration.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Two tenants exist: "acme" (Tenant A) and "globex" (Tenant B), both with status `active`.
- HR Officer users exist in both tenants.
- Object storage is available and configured.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme.yourhrm.com | Active tenant |
| Tenant B | globex.yourhrm.com | Active tenant |
| Tenant A photo | acme-photo.jpg | Uploaded for Tenant A employee |
| Tenant B photo | globex-photo.jpg | Uploaded for Tenant B employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create an employee in Tenant A with profile photo "acme-photo.jpg" | Photo stored at `{tenantA_id}/core-hr/{employeeA_id}/profile/acme-photo.jpg`. |
| 2 | Create an employee in Tenant B with profile photo "globex-photo.jpg" | Photo stored at `{tenantB_id}/core-hr/{employeeB_id}/profile/globex-photo.jpg`. |
| 3 | Verify storage paths have different tenant prefixes | tenantA_id != tenantB_id in the path prefix. |
| 4 | From Tenant A context, attempt to access Tenant B's photo signed URL | Access is denied (403 Forbidden or URL is invalid/expired for different tenant context). |
| 5 | From Tenant A context, attempt to enumerate Tenant B's storage path directly | Access is denied; path is not resolvable from Tenant A context. |
| 6 | Verify Tenant A's signed URL only works within Tenant A's session | Signed URL includes tenant scoping and cannot be reused cross-tenant. |

## 6. Postconditions
- Each tenant's photos are stored under tenant-specific prefixes.
- Cross-tenant photo access is impossible.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
