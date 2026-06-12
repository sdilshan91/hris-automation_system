---
id: TC-CHR-182
user_story: US-CHR-007
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-182: IANA time zone identifier stored and displayed correctly

## 1. Test Objective
Verify that the system correctly stores and displays IANA time zone identifiers (e.g., "America/New_York", "Asia/Colombo", "Europe/London"). The time zone value must be persisted in the database in IANA format, displayed in the UI consistently, and validated against known IANA zones. This validates FR-4 and BR-3.

## 2. Related Requirements
- User Story: US-CHR-007
- Functional Requirements: FR-4
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Location Name | Time Zone (IANA) | Notes |
|---------------|-----------------|-------|
| New York Office | America/New_York | Common US zone |
| London Branch | Europe/London | GMT/BST zone |
| Tokyo Office | Asia/Tokyo | JST +9 |
| Sydney Branch | Australia/Sydney | AEST/AEDT zone |
| Mumbai Office | Asia/Kolkata | IST +5:30 (half-hour offset) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create location "New York Office" with time zone "America/New_York" | Location created. API response shows `time_zone: "America/New_York"`. |
| 2 | Create location "London Branch" with time zone "Europe/London" | Location created. API response shows `time_zone: "Europe/London"`. |
| 3 | Create location "Tokyo Office" with time zone "Asia/Tokyo" | Location created. API response shows `time_zone: "Asia/Tokyo"`. |
| 4 | Create location "Sydney Branch" with time zone "Australia/Sydney" | Location created. API response shows `time_zone: "Australia/Sydney"`. |
| 5 | Create location "Mumbai Office" with time zone "Asia/Kolkata" | Location created. API response shows `time_zone: "Asia/Kolkata"`. |
| 6 | Navigate to the Locations list page | All 5 locations are listed with their correct IANA time zone identifiers displayed. |
| 7 | Verify the time zone column displays the IANA identifier, not an offset or abbreviation | The list shows "America/New_York" (not "EST", "UTC-5", or "Eastern Standard Time"). |
| 8 | Query the database directly for the location records | The `time_zone` column contains the exact IANA strings: "America/New_York", "Europe/London", "Asia/Tokyo", "Australia/Sydney", "Asia/Kolkata". |
| 9 | Attempt to select an invalid time zone (e.g., submit "InvalidTZ/Nowhere" via direct API call) | API returns 400/422 validation error. The time zone is validated against the IANA database. |

## 6. Postconditions
- All 5 locations have correctly stored IANA time zone identifiers.
- No location was created with an invalid time zone.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
