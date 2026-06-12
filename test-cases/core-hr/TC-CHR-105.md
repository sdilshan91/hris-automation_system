---
id: TC-CHR-105
user_story: US-CHR-002
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-105: HR Officer edits a profile section -- save succeeds with audit trail (happy path)

## 1. Test Objective
Verify that an HR Officer can click "Edit" on a profile section, modify fields, save successfully with optimistic concurrency via `xmin`, and that the audit log records before/after JSONB snapshots. This validates AC-2, FR-2, FR-4, FR-5.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2, FR-4, FR-5
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant.
- Employee "Jane Doe" (EMP-0042) exists with phone "555-0100" and address "123 Main St".
- The current `xmin` value of the employee record is known.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full read/write |
| Employee ID | {jane_doe_id} | UUID |
| Original Phone | 555-0100 | Before edit |
| New Phone | 555-0199 | After edit |
| Original Address | 123 Main St | Before edit |
| New Address | 456 Oak Ave, Suite 200 | After edit |
| xmin | {current_xmin} | Concurrency token |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to employee profile page for Jane Doe | Profile loads with all sections visible. |
| 2 | Click the pencil edit icon on the "Contact" section card | Card transitions from read-only to editable inputs with a smooth fade transition (200ms). Save and Cancel buttons appear at the bottom of the card. |
| 3 | Change Phone from "555-0100" to "555-0199" | Input field accepts the new value. |
| 4 | Change Address from "123 Main St" to "456 Oak Ave, Suite 200" | Input field accepts the new value. |
| 5 | Click "Save" | Loading indicator appears; Save button is disabled to prevent double-submit. |
| 6 | Observe the API call `PATCH /api/v1/tenant/employees/{jane_doe_id}` | Request body includes `{ "phone": "555-0199", "address": "456 Oak Ave, Suite 200", "xmin": "{current_xmin}" }`. Response status is 200 OK. Response includes updated `xmin` value. |
| 7 | Verify success toast is displayed | Toast message reads "Profile updated successfully" (or similar). |
| 8 | Verify the card returns to read-only mode | Phone shows "555-0199", Address shows "456 Oak Ave, Suite 200". Edit icon reappears. |
| 9 | Query the audit_log table for the employee_id | An audit entry exists with `action: employee_profile_updated`, `entity_id` matching the employee UUID, `before` JSONB containing `"phone": "555-0100"`, `after` JSONB containing `"phone": "555-0199"`, and both `before` and `after` snapshots include address changes. `user_id` matches the HR Officer. `tenant_id` matches acme. |
| 10 | Verify `updated_at` and `updated_by` columns on the employee record | Both are updated to reflect the current user and timestamp. |

## 6. Postconditions
- Employee record has updated phone and address.
- `xmin` has advanced to a new value.
- Audit log entry with before/after snapshot exists.
- `updated_at` and `updated_by` are set.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
