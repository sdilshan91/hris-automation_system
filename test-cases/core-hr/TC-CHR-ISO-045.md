---
id: TC-CHR-ISO-045
user_story: US-CHR-012
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-CHR-ISO-045: Tenant A custom fields not visible to Tenant B

## 1. Test Objective
Verify that custom field definitions and their values on employee records are strictly tenant-isolated. A Tenant Admin in Tenant A cannot see, query, or use custom fields defined by Tenant B. Custom fields defined in Tenant A do not appear on Tenant B's employee forms. This validates NFR-2.

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-2
- Business Rules: BR-2
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both exist with status `active`.
- Tenant Admin users exist in both tenants.
- Tenant "acme" has custom fields: "T-Shirt Size" (dropdown), "Project Code" (text).
- Tenant "globex" has custom field: "Office Floor" (number).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A Subdomain | acme.yourhrm.com | Has T-Shirt Size, Project Code |
| Tenant B Subdomain | globex.yourhrm.com | Has Office Floor |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin in Tenant A. Query `GET /api/v1/tenant/custom-fields?entityType=Employee`. | Returns 2 fields: T-Shirt Size, Project Code. No "Office Floor" from Tenant B. |
| 2 | Authenticate as Tenant Admin in Tenant B. Query `GET /api/v1/tenant/custom-fields?entityType=Employee`. | Returns 1 field: Office Floor. No "T-Shirt Size" or "Project Code" from Tenant A. |
| 3 | From Tenant B, navigate to the employee creation form. | The Custom Fields section shows only "Office Floor". No Tenant A fields are visible. |
| 4 | From Tenant A, navigate to the employee creation form. | The Custom Fields section shows "T-Shirt Size" and "Project Code". No Tenant B fields visible. |
| 5 | From Tenant B context, attempt to access a Tenant A custom field by UUID: `GET /api/v1/tenant/custom-fields/{acme-field-id}`. | HTTP 404 Not Found (the UUID does not exist within Tenant B's scope). |

## 6. Postconditions
- No cross-tenant visibility of custom field definitions or values.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
