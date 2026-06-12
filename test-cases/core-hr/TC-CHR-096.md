---
id: TC-CHR-096
user_story: US-CHR-001
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-096: Employee form page load within 2.5 seconds (NFR-4)

## 1. Test Objective
Verify that the employee creation form (Add Employee wizard) loads completely within 2.5 seconds, including all dependent data (departments dropdown, job titles dropdown, employment types, custom fields configuration).

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-4
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated.
- The tenant has 50+ departments, 30+ job titles, and 5 custom fields configured.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Departments | 50+ | Realistic count |
| Job titles | 30+ | Realistic count |
| Custom fields | 5 | Moderate configuration |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee module | Employee list loads. |
| 2 | Click "Add Employee" and start a performance timer | Timer starts. |
| 3 | Wait until the wizard is fully rendered (all dropdowns populated, custom fields loaded, profile photo zone visible) | Timer stops when rendering is complete. |
| 4 | Verify the total load time is <= 2.5 seconds | SLA met. |
| 5 | Repeat on a 3G-throttled connection (simulating slow network) | Page should still load within a reasonable time (degrade gracefully, show loading indicators). |

## 6. Postconditions
- The employee creation form loads within the performance SLA.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
