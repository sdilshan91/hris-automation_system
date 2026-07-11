---
id: US-CHR-007
module: Core HR
priority: Should Have
persona: Tenant Admin / HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 4
---

# US-CHR-007: Manage Office Locations

## 1. Description
**As a** Tenant Admin or HR Officer,
**I want to** create, view, edit, and deactivate office locations and branches,
**So that** employees can be assigned to physical locations, and location-specific policies (holiday calendars, shifts, attendance rules) can be applied.

## 2. Preconditions
- The user is authenticated with Tenant Admin or HR Officer role within their tenant.
- Tenant context is resolved from the subdomain.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A Tenant Admin navigates to the Locations management page | They click "Add Location" | A form appears with fields: Location Name (required), Address (street, city, state/province, country, postal code), Time Zone (required), Phone, and Status. |
| AC-2 | The admin enters a valid, unique location name and submits | The form is saved | A new location record is created with `tenant_id` from session context, and the location appears in the list and becomes available in employee assignment dropdowns and holiday calendar configuration. |
| AC-3 | The admin attempts to deactivate a location that has active employees assigned | They click "Deactivate" | The system warns: "This location has X active employees. Reassign them before deactivating." and blocks deactivation. |
| AC-4 | An HR Officer edits a location's time zone | They save the change | The updated time zone is used for attendance calculations, shift scheduling, and holiday calendar display for employees at that location. The change is recorded in the audit log. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL support CRUD operations on locations scoped to the current tenant.
- FR-2: The system SHALL enforce unique location names within a tenant.
- FR-3: The system SHALL capture structured address fields (street, city, state/province, country from a standard list, postal code).
- FR-4: The system SHALL require a time zone (IANA format, e.g., "Asia/Colombo") per location.
- FR-5: The system SHALL prevent deactivation of locations with active employee assignments.
- FR-6: The system SHALL use soft delete for locations.
- FR-7: The system SHALL display employee count per location.
- FR-8: All location data SHALL be tenant-isolated via RLS and EF Core global query filters.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Location CRUD API response time SHALL be <= 400 ms for reads, <= 800 ms for writes (P95).
- NFR-2: All location data SHALL be tenant-isolated via RLS and EF Core global query filters.
- NFR-3: The management page SHALL be fully responsive (360px to 4K).
- NFR-4: Audit log entries SHALL be created for all location create, update, and deactivate operations.

## 6. Business Rules
- BR-1: Location names are unique within a tenant but can repeat across tenants.
- BR-2: Each employee can be assigned to one primary location.
- BR-3: The time zone of a location drives attendance clock-in/out time calculations and shift boundaries for employees at that location.
- BR-4: Holiday calendars can be scoped to specific locations.
- BR-5: Deactivated locations cannot be assigned to new employees but remain visible on existing records.
- BR-6: A tenant can operate with zero locations defined (location assignment on employees is optional).

## 7. Data Requirements
**Location table schema:**
| Column | Type | Required | Notes |
|--------|------|----------|-------|
| location_id | uuid (PK) | Auto | |
| tenant_id | uuid (FK) | Auto | Set from session |
| name | varchar(150) | Yes | Unique per tenant |
| address_line1 | varchar(250) | No | |
| address_line2 | varchar(250) | No | |
| city | varchar(100) | No | |
| state_province | varchar(100) | No | |
| country | varchar(100) | No | From standard list |
| postal_code | varchar(20) | No | |
| time_zone | varchar(50) | Yes | IANA time zone identifier |
| phone | varchar(20) | No | |
| is_active | boolean | Yes | Default: true |
| created_at / updated_at | timestamptz | Auto | |
| created_by / updated_by | uuid | Auto | |
| is_deleted | boolean | Auto | Default: false |

## 8. UI/UX Notes (Notion-like, cards-based)
- Locations page: card-based table with columns for Name, City, Country, Time Zone, Employee Count, Status.
- "Add Location" button (top-right) opens a slide-over panel or modal card.
- Address fields grouped in a collapsible "Address" section within the form.
- Time zone selector: searchable dropdown with common zones highlighted at the top.
- Country selector: searchable dropdown with flag icons.
- Each location row shows employee count as a clickable badge linking to the directory filtered by that location.
- On mobile: table collapses to a card list with stacked address lines.
- Subtle card styling: `rounded-xl shadow-sm bg-white`.

## 9. Dependencies
- US-CHR-001: Employees reference locations for assignment.
- US-CHR-003: Employee directory filters by location.
- Leave module (future): Holiday calendars are scoped to locations.
- Attendance module (future): Shift times and clock-in validation use location time zone.

## 10. Assumptions & Constraints
- Time zone list is sourced from the IANA Time Zone Database (free, open source).
- Country list is sourced from a standard ISO 3166 dataset.
- Geocoding (lat/long from address) is not required in Phase 1 but the schema can be extended.
- Only free/open-source libraries are used.

## 11. Test Hints
- **Create location:** Create a new location with all fields; verify it appears in the list and in employee assignment dropdowns.
- **Duplicate name:** Attempt duplicate name in same tenant; expect error. Different tenant accepts same name.
- **Deactivate with employees:** Assign an employee to a location; attempt deactivation; expect warning/block.
- **Tenant isolation:** Create locations in Tenant A; query from Tenant B; verify zero results.
- **Time zone:** Create a location with "America/New_York"; verify it's stored and displayed correctly.
- **Audit trail:** Edit a location's address; verify audit_log entry.
- **Responsive:** View locations page at 360px; verify card list layout.
