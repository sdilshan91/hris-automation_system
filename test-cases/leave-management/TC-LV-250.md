---
id: TC-LV-250
user_story: US-LV-012
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-250: Authorization — a user without Leave.Reports/HR permission is denied (403)

## 1. Test Objective
Verify §2 / BR-2: the report and analytics endpoints require `Leave.Reports` (or `HR.Officer`); a user lacking the permission is denied with HTTP 403 and no report data is returned.

## 2. Related Requirements
- User Story: US-LV-012
- Preconditions: §2 (Leave.Reports / HR.Officer)
- Business Rules: BR-2
- Cross-ref: US-AUTH-* (permission enforcement)

## 3. Preconditions
- Tenant "acme"; an authenticated user WITHOUT `Leave.Reports`/`HR.Officer` (e.g. a plain Employee for the HR-only reports).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoints | `/api/v1/leaves/reports/{type}`, `/api/v1/leaves/analytics/{chart}` | FR-6, FR-7 |
| Permission | missing Leave.Reports | denied |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/reports/balance-summary` as the unauthorized user | 403 Forbidden; no report data disclosed; handler not reached for data. |
| 2 | Call `GET /api/v1/leaves/analytics/utilization` as the same user | 403 Forbidden. |
| 3 | Attempt the export endpoint | 403 Forbidden; no file generated. |
| 4 | Repeat with a user holding `Leave.Reports` (positive control) | The endpoints authorize and return data — confirms the 403 is authorization-based, not a generic failure. |

## 6. Postconditions
- Report/analytics/export endpoints enforce Leave.Reports/HR.Officer; unauthorized users get 403.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
