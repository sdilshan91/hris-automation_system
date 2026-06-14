---
id: TC-LV-ISO-025
user_story: US-LV-007
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-025: Holidays in Tenant A are not visible in Tenant B (Test Hint)

## 1. Test Objective
Verify cross-tenant data isolation for holidays: holidays created in Tenant A never appear in Tenant B's list, calendar, range reads, or leave-day exclusion (Test Hint §11, NFR-2).

## 2. Related Requirements
- User Story: US-LV-007
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" (HR "Priya") and Tenant "globex" (HR "Dana"), each authenticated in their own subdomain context.
- "acme" has a holiday on 2026-04-14; "globex" has a different holiday on 2026-04-21.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme holiday | 2026-04-14 | tenant A |
| globex holiday | 2026-04-21 | tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana (globex), GET `/api/v1/holidays?year=2026` | Returns only globex's 2026-04-21 holiday; acme's 2026-04-14 is absent. |
| 2 | As Priya (acme), GET `/api/v1/holidays?year=2026` | Returns only acme's 2026-04-14 holiday; globex's is absent. |
| 3 | As Dana, GET `/api/v1/holidays/{acmeHolidayId}` (acme's id) | 404 Not Found -- the row is invisible across tenants. |
| 4 | Run a leave-day calc in globex over a range covering 2026-04-14 | acme's holiday does NOT reduce a globex employee's leave day count. |

## 6. Postconditions
- Holiday data is strictly tenant-isolated for reads, lookups, and leave calculation.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
