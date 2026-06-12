---
id: TC-CHR-ISO-037
user_story: US-CHR-010
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-037: Tenant A's imported employees are not visible to Tenant B

## 1. Test Objective
Verify that employees imported via bulk import in Tenant A are completely invisible when querying from Tenant B's context. The EF Core global query filter and RLS enforce tenant isolation on all imported records.

## 2. Related Requirements
- User Story: US-CHR-010
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-6
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both exist with status `active`.
- HR Officer users exist in both tenants.
- Departments and job titles required by the import exist in Tenant "acme".
- No employees with the import emails exist in either tenant before the test.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A Subdomain | acme.yourhrm.com | Import performed here |
| Tenant B Subdomain | globex.yourhrm.com | Should see zero imported employees |
| File Name | isolation_test.csv | 5 valid rows |
| Emails | iso1@acme.test through iso5@acme.test | Unique emails |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in Tenant A ("acme"). Upload `isolation_test.csv` and import. | 5 employees created in Tenant A. |
| 2 | Verify all 5 employees exist in Tenant A's directory (GET /api/v1/tenant/employees). | All 5 are listed with correct tenant_id = acme UUID. |
| 3 | Authenticate as HR Officer in Tenant B ("globex"). Query GET /api/v1/tenant/employees. | Tenant B's employee list does NOT contain any of the 5 imported employees. |
| 4 | From Tenant B, search for one of the imported emails (e.g., `iso1@acme.test`). | Zero results returned. |
| 5 | From Tenant B, attempt to access a specific imported employee by ID (if the UUID is known): GET /api/v1/tenant/employees/{acme-employee-uuid}. | 404 Not Found (the global query filter excludes it). |

## 6. Postconditions
- Tenant A has 5 new employees. Tenant B has no access to them.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
