---
id: TC-CHR-313
user_story: US-CHR-012
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-313: Custom field definition changes are audited

## 1. Test Objective
Verify that all changes to custom field definitions (create, update, deactivate, reactivate, reorder) are recorded in the audit log with the acting user, timestamp, and before/after details. This validates NFR-5.

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Tenant Admin is authenticated.
- A custom field "T-Shirt Size" exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | admin@acme.test | Tenant Admin performing changes |
| Custom Field | T-Shirt Size | Existing field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create a new custom field "Project Code" (text, optional). | Audit log entry created: action "custom_field_created", entity "T-Shirt Size -> Project Code", actor = admin@acme.test, timestamp, details include field type, entity type, required status. |
| 2 | Update the field name from "Project Code" to "Internal Project Code". | Audit log entry: action "custom_field_updated", before = {field_name: "Project Code"}, after = {field_name: "Internal Project Code"}. |
| 3 | Deactivate the custom field "T-Shirt Size". | Audit log entry: action "custom_field_deactivated", field_name = "T-Shirt Size", is_active before = true, after = false. |
| 4 | Reactivate the custom field "T-Shirt Size". | Audit log entry: action "custom_field_reactivated", is_active before = false, after = true. |
| 5 | Reorder custom fields. | Audit log entry: action "custom_fields_reordered", with before/after display_order values. |
| 6 | Query audit logs for custom field changes. | All 5 entries are present with correct actor, timestamps, and before/after details. |

## 6. Postconditions
- All custom field definition changes have corresponding audit log entries.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
