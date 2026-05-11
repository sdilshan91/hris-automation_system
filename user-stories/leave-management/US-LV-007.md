---
id: US-LV-007
module: Leave Management
priority: Must Have
persona: HR Officer / Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 4
---

# US-LV-007: Holiday Calendar Management Per Tenant

## 1. Description
**As an** HR Officer or Tenant Admin,
**I want to** manage a holiday calendar specific to my organization (and optionally by location),
**So that** public holidays are automatically excluded from leave day calculations and employees are aware of upcoming holidays.

## 2. Preconditions
- User is authenticated with `Leave.Configure` or `Tenant.Admin` permission.
- Tenant has been provisioned and basic setup is complete.
- Locations have been configured in Core HR if location-specific holidays are needed.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer is on the Holiday Calendar page | They add a new holiday with name, date, type (public/restricted/optional), and applicable locations | The holiday is saved, scoped to the tenant, and visible to all employees (or location-filtered employees) |
| AC-2 | A holiday exists in the calendar | An employee applies for leave spanning that date | The holiday is automatically excluded from the leave day count (e.g., 5 calendar days spanning 1 holiday = 4 leave days) |
| AC-3 | HR Officer imports holidays | They upload a CSV file with holiday name, date, and type columns | Holidays are bulk-created with validation; duplicates (same date) are flagged for review |
| AC-4 | HR Officer views the holiday calendar for the current year | The calendar page loads | A visual calendar view (month/year) displays all holidays with color-coded types, and a list view is also available |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: CRUD operations for holidays scoped to `tenant_id`.
- FR-2: Holiday fields: `name (varchar(100))`, `date (date)`, `type (varchar(20))` [public, restricted, optional], `location_id (uuid FK, nullable)`, `description (text, nullable)`, `is_recurring (boolean)`.
- FR-3: Recurring holidays: Option to mark a holiday as annually recurring; the system auto-generates entries for the next year via Hangfire job.
- FR-4: CSV import endpoint: `POST /api/v1/holidays/import` accepting multipart CSV upload.
- FR-5: Holiday seeding during tenant onboarding (Step 4) with a country-based template.
- FR-6: Integration with leave day calculation: `GET /api/v1/holidays?from={date}&to={date}` used internally by the leave application service.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Holiday list API for a given year must respond within 200ms (P95); data cached in Redis with invalidation on write.
- NFR-2: All holiday data tenant-isolated via EF Core filters + PostgreSQL RLS.
- NFR-3: CSV import must handle up to 100 rows within 5 seconds.
- NFR-4: Calendar view must be responsive and functional on mobile devices.

## 6. Business Rules
- BR-1: Holiday dates must be unique within a tenant and location combination (no duplicate dates for the same location).
- BR-2: Public holidays apply to all employees in the tenant (or location); restricted holidays may require employees to apply.
- BR-3: Optional holidays may count against a separate "optional holiday" leave type if configured.
- BR-4: Holidays cannot be deleted if they fall within a finalized payroll period; they can only be deactivated.
- BR-5: Recurring holidays auto-generate for the next calendar year 30 days before year-end (Hangfire scheduled job).

## 7. Data Requirements
- **Table:** `holiday`
- **Key columns:** `holiday_id (uuid PK)`, `tenant_id (uuid FK)`, `name (varchar(100))`, `date (date)`, `type (varchar(20))`, `location_id (uuid FK, nullable)`, `description (text)`, `is_recurring (boolean)`, `is_active (boolean)`, `is_deleted (boolean)`, audit columns.
- **Index:** `holiday(tenant_id, date)` for efficient range queries.
- **RLS:** Standard tenant isolation policies.

## 8. UI/UX Notes (Notion-like)
- Dual view: Calendar view (interactive month grid with holiday markers) and List view (Notion-like table with inline editing).
- Toggle between views via segmented control at the top.
- Holiday type color coding: Public = blue, Restricted = orange, Optional = green.
- Add holiday via inline row creation (Notion-style) or a slide-over panel.
- CSV import via drag-and-drop file zone with preview table and validation messages before confirm.
- Year navigation with arrow buttons and year picker.
- Mobile: Calendar view collapses to a compact list-by-month view.

## 9. Dependencies
- **US-CORE-***: Location master data for location-specific holidays.
- **US-LV-003**: Leave application uses holiday data for day calculation.
- **US-TENANT-***: Tenant onboarding wizard Step 4 seeds initial holidays.
- **Hangfire**: For recurring holiday generation job.
- **Redis**: For caching holiday data.

## 10. Assumptions & Constraints
- Holiday templates for seeding are maintained as static JSON files in the application (not a third-party API).
- Only free/open-source calendar UI components are permitted.
- The system supports one holiday calendar per tenant (with optional location filtering), not multiple independent calendars.

## 11. Test Hints
- Test leave day exclusion: Create a holiday on Wednesday; apply for Mon-Fri leave; verify total = 4 days.
- Test location filtering: Create a holiday for "New York" location only; verify it does not affect employees in "London".
- Test CSV import: Upload a valid CSV with 20 holidays; verify all are created. Upload with duplicates; verify validation errors.
- Test recurring: Mark a holiday as recurring; trigger the Hangfire job; verify next year's entry is created.
- Test tenant isolation: Holidays in Tenant A must not appear in Tenant B.
- Test duplicate prevention: Attempt to create two holidays on the same date for the same location; verify error.
