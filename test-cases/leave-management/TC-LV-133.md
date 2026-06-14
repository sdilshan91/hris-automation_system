---
id: TC-LV-133
user_story: US-LV-007
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-133: Location-scoped holiday excludes a leave day only for that location's employees (AC-2, BR-2, Test Hint)

## 1. Test Objective
Verify location filtering of holiday exclusion: a holiday scoped to "New York" reduces the leave day count for a New York employee but does NOT affect a London employee on the same dates (Test Hint §11, BR-2, FR-6). Tenant-wide (null location) holidays affect everyone.

## 2. Related Requirements
- User Story: US-LV-007
- Acceptance Criteria: AC-2
- Business Rules: BR-2
- Functional Requirements: FR-6

## 3. Preconditions
- Tenant "acme" with locations "New York" (loc-NY) and "London" (loc-LDN).
- Employee "Nina" assigned to New York; employee "Liam" assigned to London; both with Leave.Apply and sufficient balance.
- 5-day work week.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| NY-only public holiday | 2026-09-09 (Wed), Public, loc-NY | location-scoped |
| Leave range | 2026-09-07 (Mon) .. 2026-09-11 (Fri) | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Nina (New York) applies for Mon-Fri 2026-09-07..09-11 | `totalDays` = 4 -- the NY-only Wednesday holiday is excluded for her. |
| 2 | Liam (London) applies for the same Mon-Fri range | `totalDays` = 5 -- the NY-only holiday does NOT affect London. |
| 3 | Add a tenant-wide (null location) public holiday on Thu 2026-09-10 and re-run | Nina = 3, Liam = 4 -- the tenant-wide holiday affects both; the NY-only one still affects only Nina. |
| 4 | Confirm provider scoping | `IHolidayProvider` returns tenant-wide holidays for everyone and location-specific holidays only when the employee's location matches. |

## 6. Postconditions
- Holiday exclusion respects per-location scope; tenant-wide holidays apply to all.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
