---
id: TC-AUTH-106
user_story: US-AUTH-010
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-106: Progressive lockout disabled -- duration remains constant across cycles

## 1. Test Objective
Verify that when `progressive_lockout_enabled = false`, repeated lockout cycles use the same base lockout duration each time (no doubling). This confirms the progressive feature is opt-in and the default behavior is a constant duration.

## 2. Related Requirements
- User Story: US-AUTH-010
- Functional Requirements: FR-9
- Data Requirements: `progressive_lockout_enabled` tenant setting

## 3. Preconditions
- Tenant "acme" has lockout policy: `maxFailedAttempts = 5`, `lockoutDurationMinutes = 15`, `progressive_lockout_enabled = false`.
- User `alice@acme.com` has `failed_login_count = 0`, `locked_until = null`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Active user |
| Max failed attempts | 5 | Threshold |
| Base lockout duration | 15 minutes | Constant |
| Progressive lockout | disabled | Tenant setting |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **Cycle 1:** Fail 5 times. | Account locked; `locked_until` is approximately `now() + 15 minutes`. |
| 2 | Advance time past lockout. Log in successfully. | Counters cleared. |
| 3 | **Cycle 2:** Fail 5 times. | Account locked again; `locked_until` is approximately `now() + 15 minutes` (same duration, NOT 30). |
| 4 | Advance time past lockout. Log in successfully. | Counters cleared. |
| 5 | **Cycle 3:** Fail 5 times. | Account locked; `locked_until` is approximately `now() + 15 minutes` (still 15, NOT 60). |
| 6 | Verify all three lockout durations are approximately equal (15 minutes each). | No progressive doubling occurs when the feature is disabled. |

## 6. Postconditions
- Lockout duration remains constant at 15 minutes regardless of how many lockout cycles have occurred.
- Progressive lockout is confirmed to be opt-in behavior.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
