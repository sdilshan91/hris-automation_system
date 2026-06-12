---
id: TC-CHR-108
user_story: US-CHR-002
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-108: Concurrency conflict -- second save with stale xmin rejected

## 1. Test Objective
Verify that when two HR Officers open the same employee profile and both edit simultaneously, the second officer's save is rejected because the `xmin` token is stale. The first change must not be overwritten. This validates AC-3, FR-4.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-3
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Two HR Officers ("Alice" and "Bob") are authenticated in the "acme" tenant.
- Employee "Jane Doe" exists with phone "555-0100" and current `xmin` value X.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| HR Officer A | Alice | First to save |
| HR Officer B | Bob | Second to save (stale xmin) |
| Employee ID | {jane_doe_id} | Target profile |
| Original Phone | 555-0100 | Before any edit |
| Alice's Phone | 555-0111 | Alice's edit |
| Bob's Phone | 555-0222 | Bob's edit (should fail) |
| xmin at load | X | Both officers load this value |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Alice opens Jane Doe's profile in Browser Tab 1 | Profile loads with phone "555-0100" and xmin X. |
| 2 | Bob opens Jane Doe's profile in Browser Tab 2 | Profile loads with phone "555-0100" and same xmin X. |
| 3 | Alice clicks Edit on the Contact section, changes phone to "555-0111", and clicks Save | PATCH request sent with `xmin: X`. Response is 200 OK. Phone is updated to "555-0111". xmin advances to Y. |
| 4 | Bob clicks Edit on the Contact section, changes phone to "555-0222", and clicks Save | PATCH request sent with `xmin: X` (stale). Response is 409 Conflict. |
| 5 | Verify the error message shown to Bob | Message reads: "This record was modified by another user. Please refresh and try again." |
| 6 | Verify the database state | Phone is "555-0111" (Alice's change). Bob's change was NOT applied. xmin is Y. |
| 7 | Bob refreshes the page | Profile loads with phone "555-0111" and xmin Y. Bob can now edit with the current xmin. |
| 8 | Verify audit log | Only Alice's change is recorded. No entry for Bob's rejected attempt. |

## 6. Postconditions
- Employee phone is "555-0111" (Alice's change persists).
- Bob's change was rejected without data loss.
- Audit log reflects only Alice's successful edit.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
