---
id: TC-CHR-174
user_story: US-CHR-007
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-174: Edit location time zone -- saved correctly and audit log entry recorded

## 1. Test Objective
Verify that an HR Officer can edit a location's time zone, save the change, and that the updated time zone is persisted and reflected in the UI. Additionally verify that the change is recorded in the audit log with before/after values. This validates AC-4 and NFR-4.

## 2. Related Requirements
- User Story: US-CHR-007
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1, FR-4
- Non-Functional Requirements: NFR-4
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Location "New York Office" exists with time zone "America/New_York".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Location Name | New York Office | Existing location |
| Original Time Zone | America/New_York | Current value |
| New Time Zone | America/Chicago | Updated value |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Locations management page | Locations list loads with "New York Office" visible. |
| 2 | Click the edit action on "New York Office" | Edit form/panel opens with current values pre-populated. Time Zone shows "America/New_York". |
| 3 | Open the Time Zone searchable dropdown | Dropdown displays IANA time zones. "America/New_York" is the current selection. |
| 4 | Search for "Chicago" in the time zone dropdown | "America/Chicago" appears in the filtered results. |
| 5 | Select "America/Chicago" | Time zone field updates to "America/Chicago". |
| 6 | Click "Save" | Form submits via `PUT /api/v1/tenant/locations/{id}`. |
| 7 | Verify success feedback | Success toast notification appears (e.g., "Location updated successfully"). |
| 8 | Verify the location list reflects the updated time zone | "New York Office" row/card now shows Time Zone = "America/Chicago". |
| 9 | Re-open the edit form for "New York Office" | Time Zone field shows "America/Chicago" as the persisted value. |
| 10 | Query the audit log for this location | An audit_log entry exists with: action = "update", entity = "location", entity_id = location ID, changed field = "time_zone", before = "America/New_York", after = "America/Chicago". |

## 6. Postconditions
- Location "New York Office" has time_zone = "America/Chicago" in the database.
- An audit_log entry records the time zone change with before/after values.
- The updated time zone is available for attendance calculations and shift scheduling for employees at this location.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
