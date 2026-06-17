---
id: TC-NTF-ISO-002
user_story: US-NTF-001
module: Notifications & Audit
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-NTF-ISO-002: Missing tenant context + cross-tenant ID/group injection rejected (-> 404 / hub reject)

## 1. Test Objective
Verify the notification API rejects requests lacking a valid tenant context, and that attempts to
inject a foreign tenant's group name or notification id are rejected — at the hub (BR-5) and at the
REST layer (404, not 403). The tenant is always derived from the authenticated session, never trusted
from client input.

## 2. Related Requirements
- User Story: US-NTF-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-2 (server-derived groups), FR-3 (tenant-scoped persistence)
- Business Rules: BR-5 (cross-tenant group names rejected at hub level)

## 3. Preconditions
- `userB` (Tenant B) authenticated. A notification `N-A` belongs to Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| foreign group | t:TA:user:userA | Tenant A group, injected by userB |
| foreign notification id | N-A | Tenant A row |
| spoofed tenant header/claim | tenant_id=TA in body/header | client-supplied, must be ignored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call any notification REST endpoint with NO resolvable tenant context | Rejected (401/400); no data returned |
| 2 | As `userB`, invoke a hub method (if any) requesting to join group `t:TA:user:userA` | Hub rejects the cross-tenant group join (BR-5); userB is never added to a Tenant A group |
| 3 | As `userB`, GET / mark-read notification id `N-A` | 404 (existence not disclosed), not 403 |
| 4 | As `userB`, send a request body/header asserting `tenant_id=TA` | Ignored; tenant resolved from session (Tenant B); no Tenant A access granted |
| 5 | Verify hub-side group membership for `userB` after attempts | Only `t:TB:user:userB` (+ role group); no Tenant A group present |

## 6. Postconditions
- No tenant-context-less access; injection attempts blocked; tenant is session-derived.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
