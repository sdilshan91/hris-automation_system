---
id: TC-AUTH-089
user_story: US-AUTH-010
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-089: Progressive lockout doubles duration after repeated lockout cycles

## 1. Test Objective
Verify that progressive lockout is supported: after repeated lockout cycles (e.g., 3 within 24 hours), the lockout duration doubles each time. Verify the first lockout uses the base duration, the second lockout doubles it, and the third doubles it again.

## 2. Related Requirements
- User Story: US-AUTH-010
- Functional Requirements: FR-9
- Data Requirements: `progressive_lockout_enabled` tenant setting

## 3. Preconditions
- Tenant "acme" has lockout policy: `maxFailedAttempts = 5`, `lockoutDurationMinutes = 15`, `progressive_lockout_enabled = true`.
- User `alice@acme.com` has `failed_login_count = 0`, `locked_until = null`, and no prior lockout cycles within the last 24 hours.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Active user |
| Max failed attempts | 5 | Threshold |
| Base lockout duration | 15 minutes | First lockout |
| Expected 2nd lockout | 30 minutes | Doubled |
| Expected 3rd lockout | 60 minutes | Doubled again |
| Progressive lockout | enabled | Tenant setting |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **Cycle 1:** Fail login 5 times consecutively. | Account locked; `locked_until` is approximately `now() + 15 minutes`. |
| 2 | Record `locked_until` value as `L1`. Verify `L1` is approximately 15 minutes in the future. | First lockout duration is the base 15 minutes. |
| 3 | Advance time past `L1` (or manually set `locked_until` to the past for test efficiency). | Lockout has expired. |
| 4 | Log in successfully to clear the lockout state. | HTTP 200; `failed_login_count = 0`, `locked_until = null`. |
| 5 | **Cycle 2:** Fail login 5 times consecutively again. | Account locked again. |
| 6 | Record `locked_until` as `L2`. Verify `L2` is approximately `now() + 30 minutes` (doubled from base). | Second lockout duration is 30 minutes. |
| 7 | Advance time past `L2`. | Lockout has expired. |
| 8 | Log in successfully. | HTTP 200; counters cleared. |
| 9 | **Cycle 3:** Fail login 5 times consecutively. | Account locked. |
| 10 | Record `locked_until` as `L3`. Verify `L3` is approximately `now() + 60 minutes` (doubled again). | Third lockout duration is 60 minutes. |
| 11 | Verify that each lockout cycle generates its own `account_locked` audit event with the correct `lockout_until` duration. | Three distinct audit events with increasing durations: 15, 30, 60 minutes. |

## 6. Postconditions
- After 3 lockout cycles, the lockout duration has progressively doubled from 15 to 30 to 60 minutes.
- Three `account_locked` audit events are recorded with the respective durations.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
