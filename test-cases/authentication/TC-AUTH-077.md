---
id: TC-AUTH-077
user_story: US-AUTH-009
module: Authentication
priority: high
type: performance
status: draft
created: 2026-06-11
---

# TC-AUTH-077: Session list P95 <= 200 ms and last_active_at update overhead <= 2 ms

## 1. Test Objective
Verify that (a) the session list endpoints (`GET /api/v1/auth/me/sessions` and `GET /api/v1/tenant/users/{id}/sessions`) return results within 200 ms at P95 under normal load (NFR-3), and (b) the `last_active_at` debounced update adds no more than 2 ms overhead to regular API requests (NFR-1).

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-4, AC-6
- Functional Requirements: FR-4, FR-6, FR-7
- Non-Functional Requirements: NFR-1, NFR-2, NFR-3

## 3. Preconditions
- Tenant "acme" is in `active` state with realistic data (multiple users, multiple sessions per user).
- Performance testing tools (e.g., k6, JMeter, or custom load harness) are available.
- The `refresh_token` table has an index on `(user_id, tenant_id, revoked_at)` per NFR-2.
- The test environment approximates production-like latency (not localhost-only).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Self endpoint | GET /api/v1/auth/me/sessions | User's own sessions |
| Admin endpoint | GET /api/v1/tenant/users/{id}/sessions | Admin view |
| Request volume | 200 requests per endpoint | P95 sample |
| P95 SLA | <= 200 ms | NFR-3 |
| Overhead SLA | <= 2 ms | NFR-1 |
| User sessions | 5-10 per user | Realistic load |
| Debounce interval | ~1 minute | FR-4 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Seed the tenant with 50 users, each having 3-10 active sessions. | Realistic data volume in the `refresh_token` table. |
| 2 | As a regular user, execute 200 sequential requests to `GET /api/v1/auth/me/sessions`. Record response times. | P95 response time is <= 200 ms. |
| 3 | As admin, execute 200 sequential requests to `GET /api/v1/tenant/users/{id}/sessions` for various users. Record response times. | P95 response time is <= 200 ms. |
| 4 | **Overhead measurement:** Execute a baseline API request (e.g., `GET /api/v1/auth/me`) 500 times. Record average response time. | Baseline established. |
| 5 | Enable `last_active_at` tracking (if toggleable) and repeat the same 500 requests. | Average response time increase is <= 2 ms compared to baseline. |
| 6 | Verify that `last_active_at` is NOT updated on every request (debounced per FR-4). | When two requests are made within 1 minute, `last_active_at` is updated at most once. |
| 7 | Verify the debounce uses an async write. | The API response returns before the `last_active_at` DB write completes (no synchronous blocking). |
| 8 | Verify the DB index exists: `(user_id, tenant_id, revoked_at)` on the `refresh_token` table. | Index is confirmed via `EXPLAIN ANALYZE` on the session count query. |
| 9 | Execute the concurrent session count query (`SELECT COUNT(*) ... WHERE user_id = X AND tenant_id = Y AND revoked_at IS NULL AND ...`) and verify it uses the index. | Query plan shows index scan, not sequential scan. |

## 6. Postconditions
- Performance metrics are within the SLA thresholds.
- The debounce mechanism is confirmed to reduce write frequency.
- DB indexes support performant session queries.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
