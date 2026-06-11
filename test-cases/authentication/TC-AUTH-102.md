---
id: TC-AUTH-102
user_story: US-AUTH-010
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-102: Lockout policy bounds -- maxFailedAttempts and lockoutDurationMinutes reject out-of-range values

## 1. Test Objective
Verify that the lockout policy configuration enforces bounds (BR-5): `maxFailedAttempts` must be between 3 and 10 (inclusive), and `lockoutDurationMinutes` must be between 5 and 60 (inclusive). Values outside these ranges are rejected with a validation error.

## 2. Related Requirements
- User Story: US-AUTH-010
- Business Rules: BR-5
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant admin `admin@acme.com` is authenticated with permission to update tenant security settings.
- Current policy: `maxFailedAttempts = 5`, `lockoutDurationMinutes = 15`.

## 4. Test Data
| Field | Value | Expected Result | Notes |
|-------|-------|-----------------|-------|
| maxFailedAttempts | 2 | Rejected | Below min (3) |
| maxFailedAttempts | 0 | Rejected | Below min |
| maxFailedAttempts | -1 | Rejected | Negative |
| maxFailedAttempts | 11 | Rejected | Above max (10) |
| maxFailedAttempts | 100 | Rejected | Far above max |
| maxFailedAttempts | 3 | Accepted | Lower bound |
| maxFailedAttempts | 10 | Accepted | Upper bound |
| maxFailedAttempts | 7 | Accepted | Mid-range |
| lockoutDurationMinutes | 4 | Rejected | Below min (5) |
| lockoutDurationMinutes | 0 | Rejected | Below min |
| lockoutDurationMinutes | -5 | Rejected | Negative |
| lockoutDurationMinutes | 61 | Rejected | Above max (60) |
| lockoutDurationMinutes | 1440 | Rejected | Far above max |
| lockoutDurationMinutes | 5 | Accepted | Lower bound |
| lockoutDurationMinutes | 60 | Accepted | Upper bound |
| lockoutDurationMinutes | 30 | Accepted | Mid-range |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `PUT /api/v1/tenant/auth-settings` with `maxFailedAttempts = 2`. | HTTP 400/422 with validation error indicating the value must be between 3 and 10. |
| 2 | Send `PUT` with `maxFailedAttempts = 0`. | HTTP 400/422 with validation error. |
| 3 | Send `PUT` with `maxFailedAttempts = -1`. | HTTP 400/422 with validation error. |
| 4 | Send `PUT` with `maxFailedAttempts = 11`. | HTTP 400/422 with validation error. |
| 5 | Send `PUT` with `maxFailedAttempts = 3` (lower bound). | HTTP 200 OK; policy updated. |
| 6 | Verify the saved `maxFailedAttempts` is 3. | Value persisted correctly. |
| 7 | Send `PUT` with `maxFailedAttempts = 10` (upper bound). | HTTP 200 OK; policy updated. |
| 8 | Send `PUT` with `lockoutDurationMinutes = 4`. | HTTP 400/422 with validation error indicating the value must be between 5 and 60. |
| 9 | Send `PUT` with `lockoutDurationMinutes = 0`. | HTTP 400/422 with validation error. |
| 10 | Send `PUT` with `lockoutDurationMinutes = -5`. | HTTP 400/422 with validation error. |
| 11 | Send `PUT` with `lockoutDurationMinutes = 61`. | HTTP 400/422 with validation error. |
| 12 | Send `PUT` with `lockoutDurationMinutes = 5` (lower bound). | HTTP 200 OK; policy updated. |
| 13 | Verify the saved `lockoutDurationMinutes` is 5. | Value persisted correctly. |
| 14 | Send `PUT` with `lockoutDurationMinutes = 60` (upper bound). | HTTP 200 OK; policy updated. |
| 15 | Verify the unchanged original policy is not corrupted by the rejected requests. | Current policy reflects only the last successful update values. |

## 6. Postconditions
- Out-of-range values are rejected; in-range values (including boundary values) are accepted.
- Policy integrity is maintained after rejected updates.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
