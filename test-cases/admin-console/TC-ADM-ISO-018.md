---
id: TC-ADM-ISO-018
user_story: US-ADM-007
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-018: Cross-tenant workflow ID injection on mutating endpoints -> 404 (not 403)

## 1. Test Objective
Verify that direct cross-tenant ID injection on workflow definition endpoints (edit, archive/restore, delete, version-read) is rejected with HTTP 404 — existence non-disclosure, per the module convention (assert 404, not 403). A Tenant A admin who supplies a Tenant B workflow `workflow_id` cannot edit, archive, version, or delete it; the EF query filter scopes the row out so it resolves as not-found.

## 2. Related Requirements
- User Story: US-ADM-007
- Acceptance Criteria: AC-3/AC-4 (mutating paths)
- Functional Requirements: FR-3/FR-6/FR-7
- Business Rules: BR-7 (tenant isolation)

## 3. Preconditions
- Tenant Beta owns WF-Beta-1 (a real, known `workflow_id`).
- Dana is `TenantAdmin` of Tenant Alpha (authenticated in Alpha's context).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| target id | WF-Beta-1.workflow_id | belongs to Beta |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana (Alpha), PUT/edit WF-Beta-1 by id | 404 (NOT 403, NOT 200) — row scoped out; no version created on Beta's workflow. |
| 2 | As Dana, archive/restore WF-Beta-1 by id | 404; Beta's workflow state unchanged. |
| 3 | As Dana, DELETE WF-Beta-1 by id | 404; Beta's workflow still exists. |
| 4 | As Dana, GET WF-Beta-1 version history by id | 404. |
| 5 | Confirm from Beta's side | WF-Beta-1 is intact and unmodified (no edit/archive/delete leaked across the boundary). |

## 6. Postconditions
- No cross-tenant mutation or disclosure occurred; Beta's workflow untouched.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
