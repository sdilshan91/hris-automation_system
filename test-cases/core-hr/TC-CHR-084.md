---
id: TC-CHR-084
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-084: Dynamic custom fields rendering per tenant configuration (FR-9)

## 1. Test Objective
Verify that the employee creation form dynamically renders custom fields configured by the Tenant Admin (FR-9), that different tenants can have different custom field configurations, and that custom field values are schema-validated against the tenant's configuration.

## 2. Related Requirements
- User Story: US-CHR-001
- Functional Requirements: FR-9
- Acceptance Criteria: AC-6
- Dependencies: US-CHR-012 (Custom field configuration)

## 3. Preconditions
- Tenant "acme" has 3 custom fields configured: "Blood Type" (dropdown), "LinkedIn URL" (text, URL-validated), "T-Shirt Size" (dropdown).
- Tenant "globex" has 1 custom field configured: "Badge Number" (text, numeric).
- HR Officer users exist in both tenants.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A (acme) | 3 custom fields | Blood Type, LinkedIn URL, T-Shirt Size |
| Tenant B (globex) | 1 custom field | Badge Number |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in Tenant A ("acme") and open Add Employee form | Wizard opens. |
| 2 | Navigate to the section where custom fields appear | 3 custom fields are rendered: "Blood Type" (dropdown with A, B, AB, O), "LinkedIn URL" (text input), "T-Shirt Size" (dropdown with S, M, L, XL). |
| 3 | Enter an invalid LinkedIn URL (e.g., "not-a-url") | Schema validation error is displayed inline. |
| 4 | Authenticate as HR Officer in Tenant B ("globex") and open Add Employee form | Wizard opens. |
| 5 | Navigate to the custom fields section | Only 1 custom field is rendered: "Badge Number" (text input). Blood Type, LinkedIn URL, T-Shirt Size are NOT shown. |
| 6 | Enter a non-numeric value for Badge Number | Schema validation error if the field is configured as numeric. |

## 6. Postconditions
- Each tenant's form renders only its configured custom fields.
- Custom field validation follows the tenant's schema configuration.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
