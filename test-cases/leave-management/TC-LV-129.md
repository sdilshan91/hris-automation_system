---
id: TC-LV-129
user_story: US-LV-007
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-129: Add a holiday with name, date, type, and location (happy path)

## 1. Test Objective
Verify that an HR Officer / Tenant Admin can create a holiday with name, date, type (public/restricted/optional), optional location, and description; the holiday is persisted, tenant-scoped, and immediately readable by employees via the list endpoint (AC-1, FR-1, FR-2).

## 2. Related Requirements
- User Story: US-LV-007
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" active; user "Priya (HR Officer)" authenticated with `Holiday.Create` and `Holiday.View`.
- An employee "Sam" (Holiday.View) exists in tenant "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| name | "New Year's Day" | varchar(100) |
| date | 2026-01-01 | DateOnly |
| type | Public | HolidayType |
| locationId | null | tenant-wide |
| description | "Bank holiday" | optional text |
| isRecurring | false | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | POST `/api/v1/holidays` with the test data | 201 Created; response carries the new holiday with `id`, tenant-stamped `tenantId`, and echoed fields. |
| 2 | GET `/api/v1/holidays?year=2026` as Priya | The new holiday appears in the list with correct name/date/type/location. |
| 3 | GET `/api/v1/holidays?year=2026` as employee Sam (Holiday.View) | Sam sees the tenant-wide holiday (visible to all employees per AC-1). |
| 4 | Inspect the persisted row | `TenantId` is stamped from the session (TenantInterceptor), `is_active=true`, `is_deleted=false`. |

## 6. Postconditions
- One active, tenant-scoped holiday exists for tenant "acme" and is visible to employees.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
