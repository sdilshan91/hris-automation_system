---
id: TC-CHR-031
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-031: Audit log entries created for department create, update, and deactivate

## 1. Test Objective
Verify that audit log entries are created for all department state-changing operations (create, update, deactivate) per NFR-5, with correct metadata including user_id, tenant_id, action type, and before/after snapshots.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- Audit logging infrastructure is operational.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | Audit Test Dept | Created for this test |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create department "Audit Test Dept" | Department created successfully. |
| 2 | Query audit log for the latest entry related to this department | Audit record exists with: `action: department_created`, `entity_type: department`, `entity_id` matching the new department, `tenant_id`, `user_id`, `timestamp`, and `after` snapshot containing the new department data. |
| 3 | Edit "Audit Test Dept": change name to "Audit Test Updated" | Update succeeds. |
| 4 | Query audit log for the update entry | Audit record exists with: `action: department_updated`, `before` snapshot (old name), `after` snapshot (new name), `user_id`, `tenant_id`, `timestamp`. |
| 5 | Deactivate "Audit Test Updated" (zero employees) | Deactivation succeeds. |
| 6 | Query audit log for the deactivation entry | Audit record exists with: `action: department_deactivated`, `before` snapshot (`is_active: true`), `after` snapshot (`is_active: false`), `user_id`, `tenant_id`, `timestamp`. |
| 7 | Verify all three audit entries belong to the same `tenant_id` | Audit entries are tenant-scoped. |

## 6. Postconditions
- Three audit log entries exist for the department lifecycle (create, update, deactivate).
- Each entry contains complete metadata and before/after snapshots.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
