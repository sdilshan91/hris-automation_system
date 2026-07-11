---
id: TC-PRF-002-B1
user_story: US-PRF-002
module: Performance Management
priority: medium
type: functional
status: draft
created: 2026-07-08
---

# TC-PRF-002-B1: Self-assessment attachment DELETE — owner removes an attachment before submit; non-owner/other-tenant denied; blocked after submit (BUG-243 follow-up)

**Status:** DRAFT — BLOCKED: endpoint not yet implemented (BUG-243 follow-up)

> The Angular `self-assessment` service calls a DELETE-attachment action, but the backend exposes only upload / list / download for self-assessment attachments — there is no DELETE route. This stub documents the intended verification. See BUG-243 / BUG-244.

## 1. Test Objective
Verify a to-be-built `DELETE .../performance/self-assessments/{selfAssessmentId}/attachments/{attachmentId}` that lets the **owning employee** remove one of their own self-assessment attachments **while the assessment is still Draft (pre-submit)**: 200, the attachment row is deleted AND the stored file/blob is removed (no orphan). A non-owner and any other-tenant caller get 404 (attachment invisible). After the self-assessment is Submitted (BR-3 lock), deletion is rejected (403/409). Tenant-scoped.

## 2. Related Requirements
- User Story: US-PRF-002
- Acceptance Criteria: AC-B1 (self-assessment attachment DELETE, pre-submit only)
- Functional Requirements: FR-1/FR-2 (self-assessment editing pre-submit), BR-3 (submitted assessment is locked)
- Defect: BUG-243 (parent; missing-endpoint half = BUG-244)

## 3. Preconditions
- Endpoint implemented (removes this BLOCK).
- Tenant "acme" Active; employee "Asha Patel" (`Performance.Read.Self`) has a Draft self-assessment for the active cycle with 2 uploaded attachments.
- A second acme employee (non-owner) and an other-tenant employee available for negative cases.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Self-assessment | Draft | owned by Asha, active cycle |
| Attachment A | {attachmentId} | to be deleted |
| Attachment B | second file | remains |
| Non-owner | Ravi (acme) | must be denied |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As owner Asha (Draft), `DELETE .../self-assessments/{id}/attachments/{attachmentA}` | 200; attachment A row gone; the stored file/blob is removed (no orphaned storage); attachment B still listed. |
| 2 | Re-fetch the attachment list | Only attachment B remains for acme/Asha. |
| 3 | As non-owner Ravi, DELETE Asha's attachment B | 404 (not visible / not owned); no deletion. |
| 4 | As an other-tenant ("beta") employee, DELETE the acme attachmentId | 404 (cross-tenant; invisible). |
| 5 | Asha submits the self-assessment, then attempts to DELETE an attachment | Rejected 403/409 "submitted assessment is locked" (BR-3); attachment retained. |
| 6 | DELETE a non-existent/garbage attachmentId as owner | Coded 404; no partial effect. |

## 6. Postconditions
- Pre-submit, an owner can delete their own attachments (row + file removed); post-submit attachments are immutable; no cross-tenant or non-owner deletion possible.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
