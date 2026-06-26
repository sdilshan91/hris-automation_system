---
id: TC-ADM-ISO-029
user_story: US-ADM-010
module: Admin Console
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-ADM-ISO-029: Export endpoints require valid context; Tenant-Admin foreign tenant_id ignored, download cross-tenant blocked

## 1. Test Objective
Verify AC-5 / FR-1 isolation contract: the export initiation + download endpoints require a valid tenant/auth context; a Tenant Admin's client-supplied foreign `tenant_id` is ignored (export scoped to the resolved tenant), and a Tenant Admin cannot download another tenant's export bundle (cross-tenant export_id injection rejected — 404, per the module convention of non-disclosure).

## 2. Related Requirements
- User Story: US-ADM-010
- Acceptance Criteria: AC-5 (client tenant_id ignored; security event on cross-tenant attempt)
- Functional Requirements: FR-1 (tenant_id implicit for Tenant Admin), FR-7 (download)
- Business Rules: BR-1 (own-tenant only)

## 3. Preconditions
- Tenant Admin "Tara" authenticated at `acme.yourhrm.com` (A).
- Tenant Beta (B) has its own Completed export with a known `export_id`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| beta_export_id | B's Completed export id | for cross-tenant download attempt |
| injected tenant_id | Beta's tenant_id | for initiation attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Initiation with no/invalid auth context | Rejected (401/unauthenticated); no ExportRequest created. |
| 2 | As Tara, initiate with body `tenant_id` = Beta's id | Injected id IGNORED; ExportRequest created under Acme (see TC-ADM-010-15); never under Beta. |
| 3 | As Tara, request download of Beta's `export_id` | Rejected — 404 (not 403), existence not disclosed; bundle not served. |
| 4 | Confirm the cross-tenant attempt is treated as a security event | A security/audit signal is recorded per AC-5 (no data returned). |
| 5 | As Tara, request download of Acme's own Completed export_id | Served normally (own-tenant path works). |

## 6. Postconditions
- No cross-tenant export or download succeeded; own-tenant path intact.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
