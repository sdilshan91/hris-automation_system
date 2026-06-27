---
id: TC-NTF-ISO-001
user_story: US-NTF-001
module: Notifications & Audit
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-NTF-ISO-001: User B in Tenant B does NOT receive Tenant A's notification (cross-tenant SignalR isolation)

## 1. Test Objective
Verify that a notification raised for User A in Tenant A is never delivered to User B in Tenant B.
SignalR group naming (`t:{tenantId}:user:{userId}`) plus tenant-scoped persistence prevent any
cross-tenant real-time or read leakage. Implements the AC-6 / BR-5 tenant isolation hint.

## 2. Related Requirements
- User Story: US-NTF-001
- Acceptance Criteria: AC-6
- Business Rules: BR-1 (tenant-scoped), BR-5 (cross-tenant group names rejected at hub)
- Non-Functional: NFR-2 (tenant isolation via EF query filters; Postgres RLS deferred)

## 3. Preconditions
- Tenant A has user `userA`; Tenant B has user `userB`.
- Both are authenticated and connected to `/hubs/notifications`, joined to their own tenant/user groups.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| userA group | t:TA:userA | Tenant A |
| userB group | t:TB:userB | Tenant B |
| notification | leave_approved for userA | raised in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Raise a notification for `userA` (Tenant A) | Pushed to group `t:TA:user:userA`; `userA`'s badge increments |
| 2 | Observe `userB`'s (Tenant B) client | Receives nothing — no badge change, no panel item |
| 3 | As `userB`, list notifications via the API | Tenant A's notification is NOT present; only Tenant B rows returned |
| 4 | As `userB`, attempt to fetch Tenant A's notification by id (IDOR) | 404 — not found in Tenant B scope (existence not disclosed) |
| 5 | Inspect persisted rows | The notification row has tenant_id = Tenant A only |

## 6. Postconditions
- No cross-tenant delivery or read access; isolation intact.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
