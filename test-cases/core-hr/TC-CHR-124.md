---
id: TC-CHR-124
user_story: US-CHR-002
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-124: HR Officer changes department -- employment history timeline entry created and reporting structure updated

## 1. Test Objective
Verify that when an HR Officer changes an employee's department, the system records the change in the employment history timeline with the correct effective date, and updates the reporting structure (reporting manager). This validates AC-6, FR-6, BR-4.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-6
- Functional Requirements: FR-6
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" is in department "Engineering" (manager: "Tom").
- Department "Product" exists and is active (manager: "Lisa").

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Employee ID | {jane_doe_id} | Target |
| Old Department | Engineering | Manager: Tom |
| New Department | Product | Manager: Lisa |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's profile | Employment Details shows department "Engineering", reporting manager "Tom". |
| 2 | Click Edit on Employment Details | Card transitions to edit mode. |
| 3 | Change department from "Engineering" to "Product" | Department dropdown updated. |
| 4 | Click Save | PATCH succeeds (200 OK). Department updated to "Product". |
| 5 | Verify reporting structure | Reporting manager field is now "Lisa" (Product department manager). |
| 6 | Verify employment history timeline | New entry: "Department changed from Engineering to Product", effective date = today, with date marker on the vertical timeline. |
| 7 | Verify audit log | Entry with before: `{ "department_id": "engineering_id" }`, after: `{ "department_id": "product_id" }`. |

## 6. Postconditions
- Employee is in "Product" department.
- Reporting manager updated to "Lisa".
- Employment history entry appended.
- Audit log recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
