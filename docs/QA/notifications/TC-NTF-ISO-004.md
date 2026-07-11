---
id: TC-NTF-ISO-004
user_story: US-NTF-001
module: Notifications & Audit
priority: high
type: security
status: pass
created: 2026-06-17
---

# TC-NTF-ISO-004: SignalR groups, backplane channels, and any unread-count cache are tenant-scoped

## 1. Test Objective
Verify every isolation-sensitive key is tenant-scoped: SignalR group names embed tenant_id
(`t:{tenantId}:user:{userId}`, `t:{tenantId}:role:{role}`), Redis backplane fan-out never crosses
tenants, and any cached unread-count key is tenant+user keyed (e.g.,
`notifications:unread:{tenant_id}:{user_id}`). If no cache is wired yet, assert the equivalent
always-tenant-filtered property and flag the target key shape.

## 2. Related Requirements
- User Story: US-NTF-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-2 (group naming), FR-5 (unread count), FR-10 (Redis backplane)
- Non-Functional: NFR-2 (tenant isolation)

## 3. Preconditions
- Tenant A user `userA` and Tenant B user `userB` both connected; Redis backplane available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| group shape | t:{tenantId}:user:{userId} | tenant prefix mandatory |
| role group shape | t:{tenantId}:role:{role} | tenant prefix mandatory |
| unread cache key shape | notifications:unread:{tenant_id}:{user_id} | target shape if cached |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Inspect SignalR group names assigned to `userA` and `userB` | Both carry their own tenant prefix; no shared/un-prefixed group exists |
| 2 | Two users with the SAME userId across Tenant A and Tenant B connect | They land in DISTINCT groups (`t:TA:user:X` vs `t:TB:user:X`); no collision |
| 3 | Publish a notification to `t:TA:user:userA` via the Redis backplane | Only Tenant A subscribers receive it; Tenant B subscribers do not |
| 4 | Read the unread-count cache key for `userA` (if cache wired) | Key is `notifications:unread:{TA}:{userA}` — tenant+user scoped; no global/per-user-only key |
| 5 | If no cache is wired | Assert unread count is always computed with the tenant filter applied; flag `notifications:unread:{tenant_id}:{user_id}` as the target cache key shape |

## 6. Postconditions
- All group/channel/cache keys tenant-scoped; no cross-tenant collision or fan-out.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
