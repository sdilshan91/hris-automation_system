---
id: TC-CHR-185
user_story: US-CHR-007
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-185: Audit log entries created for create, update, and deactivate operations

## 1. Test Objective
Verify that audit_log entries are created for all location create, update, and deactivate operations as required by NFR-4. Each audit entry should record the user, timestamp, action, entity type, entity ID, and changed fields where applicable.

## 2. Related Requirements
- User Story: US-CHR-007
- Non-Functional Requirements: NFR-4
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context (user ID known for audit verification).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | User ID = "admin-uuid-123" |
| Location Name (create) | Audit Test Location | For create audit |
| Address Update | City: "Colombo" -> "Kandy" | For update audit |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create a new location "Audit Test Location" with Time Zone "Asia/Colombo" | Location created successfully. |
| 2 | Query the audit_log for the create event | An entry exists with: action = "create", entity_type = "location", entity_id = new location ID, performed_by = "admin-uuid-123", timestamp is recent, tenant_id matches "acme". |
| 3 | Edit the location: change City from empty to "Colombo" | Update saved successfully. |
| 4 | Query the audit_log for the update event | An entry exists with: action = "update", entity_type = "location", entity_id = location ID, changed fields include "city" (before: null/empty, after: "Colombo"). |
| 5 | Edit the location again: change City from "Colombo" to "Kandy" | Update saved successfully. |
| 6 | Query the audit_log for the second update event | A new entry exists with: action = "update", changed field "city" (before: "Colombo", after: "Kandy"). The previous audit entry is preserved (not overwritten). |
| 7 | Deactivate the location (ensure 0 employees first) | Deactivation succeeds. |
| 8 | Query the audit_log for the deactivate event | An entry exists with: action = "deactivate", entity_type = "location", entity_id = location ID. |
| 9 | Verify total audit entries for this location | At least 4 entries exist: 1 create + 2 updates + 1 deactivate, in chronological order. |

## 6. Postconditions
- All location lifecycle events are recorded in the audit_log.
- Audit entries are immutable and chronologically ordered.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
