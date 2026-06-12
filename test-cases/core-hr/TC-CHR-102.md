---
id: TC-CHR-102
user_story: US-CHR-001
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-102: Emergency contact recommended but not mandatory on creation (BR-5)

## 1. Test Objective
Verify that at least one emergency contact is recommended but not mandatory during initial employee creation (BR-5). The form should allow submission without emergency contact data, but may display a warning or recommendation.

## 2. Related Requirements
- User Story: US-CHR-001
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Employee 1 | John Doe | No emergency contact |
| Employee 2 | Jane Smith | With emergency contact filled |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create employee "John Doe" with all mandatory fields but leave the Emergency Contact section entirely empty | Employee created successfully (201 Created). |
| 2 | Verify a soft warning or recommendation is shown (not a hard validation error) | Optional: a banner or note recommending the user add emergency contact information (not blocking). |
| 3 | Create employee "Jane Smith" with all mandatory fields plus emergency contact: Name = "Jim Smith", Relationship = "Spouse", Phone = "+1234567890" | Employee created successfully with emergency contact data persisted. |
| 4 | Verify "John Doe" has no emergency contact in the database | Emergency contact fields are null or empty. |
| 5 | Verify "Jane Smith" has emergency contact data | Emergency contact is persisted correctly. |

## 6. Postconditions
- Both employees are valid records regardless of emergency contact presence.
- Emergency contact is optional on creation but recommended.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
