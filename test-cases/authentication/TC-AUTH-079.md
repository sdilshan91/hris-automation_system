---
id: TC-AUTH-079
user_story: US-AUTH-009
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-079: Hangfire background job cleans up expired and revoked refresh tokens

## 1. Test Objective
Verify that the Hangfire background job (FR-10) periodically identifies and removes expired and revoked refresh tokens that are older than the configurable retention period (default 30 days), and that active tokens are not affected by the cleanup.

## 2. Related Requirements
- User Story: US-AUTH-009
- Functional Requirements: FR-10
- Dependencies: Hangfire, PostgreSQL

## 3. Preconditions
- Hangfire is configured and running.
- The token cleanup job is registered as a recurring job.
- The `refresh_token` table contains a mix of:
  - Active tokens (non-revoked, non-expired)
  - Recently revoked tokens (< 30 days old)
  - Old revoked tokens (> 30 days old)
  - Recently expired tokens (< 30 days old)
  - Old expired tokens (> 30 days old)

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Retention period | 30 days (default) | Configurable |
| Active token A | revoked_at = NULL, expires > now | Should NOT be deleted |
| Recent revoked B | revoked_at = 10 days ago | Should NOT be deleted (within retention) |
| Old revoked C | revoked_at = 45 days ago | Should be deleted |
| Recent expired D | expires_at = 10 days ago, revoked_at = NULL | Should NOT be deleted (within retention) |
| Old expired E | expires_at = 45 days ago, revoked_at = NULL | Should be deleted |
| Old revoked active-looking F | revoked_at = 35 days ago, issued_at = 36 days ago | Should be deleted |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Seed the `refresh_token` table with tokens A through F as described in test data. | All 6 tokens exist in the table. |
| 2 | Trigger the Hangfire token cleanup job manually (or wait for the next scheduled run). | Job executes successfully. |
| 3 | Verify active token A is still present. | Token A exists with `revoked_at IS NULL` and valid expiry. |
| 4 | Verify recently revoked token B is still present. | Token B exists (within 30-day retention). |
| 5 | Verify old revoked token C is deleted. | Token C is no longer in the table. |
| 6 | Verify recently expired token D is still present. | Token D exists (within 30-day retention). |
| 7 | Verify old expired token E is deleted. | Token E is no longer in the table. |
| 8 | Verify old revoked token F is deleted. | Token F is no longer in the table. |
| 9 | Verify the job logged its execution. | Log entry shows: number of tokens cleaned, execution time, next scheduled run. |
| 10 | **Custom retention period:** Change retention to 15 days. Re-trigger the job. | Token B (revoked 10 days ago) is still retained. Token D (expired 10 days ago) is still retained. |
| 11 | Seed a token revoked 20 days ago. Re-trigger with 15-day retention. | The 20-day-old revoked token is deleted. |
| 12 | Verify the cleanup job is idempotent. | Running the job twice in succession produces the same result (no errors, no double-deletions). |

## 6. Postconditions
- Only tokens older than the retention period that are revoked or expired are removed.
- Active tokens are never affected.
- The cleanup job runs successfully and logs its activity.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
