---
id: TC-AUTH-078
user_story: US-AUTH-009
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-078: Audit trail records all session management event types

## 1. Test Objective
Verify that every session management event type defined in the data requirements is correctly logged in the tenant audit log with appropriate metadata: `session_revoked_by_admin`, `session_revoked_by_user`, `session_expired_idle`, `session_expired_absolute`, `concurrent_session_denied`, and `concurrent_session_oldest_revoked`.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-1, AC-2, AC-3, AC-5, AC-6
- Functional Requirements: FR-9
- Data Requirements: Section 7 (Audit records)

## 3. Preconditions
- Tenant "acme" is in `active` state with session policies configured.
- Admin user `admin@acme.com` and regular user `john@acme.com` are available.
- The tenant audit log is accessible via `GET /api/v1/tenant/audit-log` or direct DB query.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant | acme | Session policy configured |
| Admin | admin@acme.com | Tenant Admin |
| User | john@acme.com | Employee |
| Expected event types | 6 distinct types | Per Section 7 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **Trigger `concurrent_session_denied`:** Set `maxConcurrentSessions = 1`, `strategy = "deny_new"`. User has 1 session. Attempt 2nd login. | Login denied. |
| 2 | Query audit log for `concurrent_session_denied` event. | Record found with: `event_type`, `user_id`, `tenant_id`, `ip_address`, `user_agent`, `active_session_count`, `strategy = "deny_new"`, `timestamp`. |
| 3 | **Trigger `concurrent_session_oldest_revoked`:** Change strategy to `"revoke_oldest"`. Attempt 2nd login again. | Login succeeds; oldest session is revoked. |
| 4 | Query audit log for `concurrent_session_oldest_revoked` event. | Record found with: `event_type`, `user_id`, `tenant_id`, `revoked_session_id`, `new_session_id`, `timestamp`. |
| 5 | **Trigger `session_expired_idle`:** Set `idleTimeoutMinutes = 2`. Wait 3 minutes. Attempt refresh. | HTTP 401; idle expired. |
| 6 | Query audit log for `session_expired_idle` event. | Record found with: `event_type`, `user_id`, `tenant_id`, `session_id`, `idle_duration_seconds`, `timestamp`. |
| 7 | **Trigger `session_expired_absolute`:** Set `absoluteTimeoutHours = 1`. Create session with `issued_at` > 1 hour ago (via DB manipulation for testing). Attempt refresh. | HTTP 401; absolute expired. |
| 8 | Query audit log for `session_expired_absolute` event. | Record found with: `event_type`, `user_id`, `tenant_id`, `session_id`, `session_duration_hours`, `timestamp`. |
| 9 | **Trigger `session_revoked_by_admin`:** Admin revokes a specific user session. | Session revoked. |
| 10 | Query audit log for `session_revoked_by_admin` event. | Record found with: `event_type`, `admin_user_id`, `target_user_id`, `revoked_session_id`, `tenant_id`, `timestamp`. |
| 11 | **Trigger `session_revoked_by_user`:** User revokes one of their own non-current sessions. | Session revoked. |
| 12 | Query audit log for `session_revoked_by_user` event. | Record found with: `event_type`, `user_id`, `revoked_session_id`, `tenant_id`, `timestamp`. |
| 13 | Verify all 6 event types are present in the audit log. | 6 distinct `event_type` values are confirmed. |
| 14 | Verify audit records are tenant-scoped. | All audit records belong to the "acme" tenant. |

## 6. Postconditions
- All 6 session management audit event types are verified in the audit log.
- Each event contains the correct metadata fields.
- Audit records are properly tenant-scoped.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
