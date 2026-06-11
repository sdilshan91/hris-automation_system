---
id: TC-AUTH-104
user_story: US-AUTH-010
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-104: Lockout check overhead is within 2ms (NFR-1 performance)

## 1. Test Objective
Verify that the lockout check (`locked_until` column lookup) adds no more than 2ms overhead to the login flow (NFR-1). This confirms the check uses an indexed column and does not introduce noticeable latency.

## 2. Related Requirements
- User Story: US-AUTH-010
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- The `users.locked_until` column is indexed in PostgreSQL.
- A baseline login response time has been established (login without lockout check, if possible, or with a known overhead budget).
- The API is running under typical load conditions.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Active, unlocked user |
| Correct password | S3cure!Pass2026 | For baseline measurement |
| Sample size | 100 requests | Statistical validity |
| Overhead threshold | 2 ms | NFR-1 limit |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the `users` table has an index on `locked_until` (or that the column is part of the primary key lookup path). | Index exists in the PostgreSQL schema. |
| 2 | Send 100 `POST /api/v1/auth/login` requests with correct credentials (normal login with lockout check). Record response times. | Median and P95 response times captured. |
| 3 | Measure the database query time for `SELECT locked_until FROM users WHERE id = @id` using `EXPLAIN ANALYZE`. | Query execution time is <= 2ms (indexed seek). |
| 4 | Compare the login response time with an equivalent endpoint that does not perform the lockout check (if available as a baseline). | The overhead attributable to the lockout check is <= 2ms. |
| 5 | If a direct comparison is not possible, verify the `EXPLAIN ANALYZE` output confirms an index scan (not a sequential scan). | The execution plan shows `Index Scan` or `Index Only Scan`. |

## 6. Postconditions
- The lockout check overhead is confirmed to be within the 2ms budget.
- The database query uses an efficient index-based lookup.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
