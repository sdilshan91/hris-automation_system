---
id: TC-ATT-077
user_story: US-ATT-006
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-077: Self-approval prevention -- a manager's own overtime routes to their supervisor/HR, never self-approvable (security)

## 1. Test Objective
Verify BR-8: managers cannot approve their own overtime. A manager's own auto-detected overtime is absent from their actionable approval queue and any attempt to self-approve via the API is refused; the record routes to the manager's supervisor (or HR if none).

## 2. Related Requirements
- User Story: US-ATT-006
- Functional Requirements: FR-5 (routing for approval)
- Business Rules: BR-8 (managers cannot approve their own overtime; route to supervisor or HR)

## 3. Preconditions
- Tenant "acme". Manager "Ben" reports to supervisor "Sara". Ben has a PENDING overtime_record of his own (from his own clock-out).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager (subject) | Ben | reports to Sara |
| Ben's own overtime | PENDING record | |
| Supervisor | Sara | should be the approver |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Ben, `GET /overtime/pending` | Ben's OWN overtime record is NOT in his actionable queue (BR-8). |
| 2 | As Ben, `POST /overtime/{benOwnRecordId}/approve` | Refused (403/validation) -- a manager cannot self-approve, enforced server-side (not just hidden in the UI). |
| 3 | As Sara (Ben's supervisor), `GET /overtime/pending` | Ben's overtime record IS in Sara's queue (routed up). |
| 4 | As Sara, approve Ben's record | 200; status APPROVED -- the supervisor is the valid approver. |
| 5 | Manager with no supervisor | The record routes to HR (or the configured fallback) rather than being self-approvable. |

## 6. Postconditions
- A manager's own overtime is never self-approvable; it is actionable only by the supervisor/HR; the invariant is enforced server-side.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Mirrors the regularization self-approval invariant (US-ATT-004 TC-ATT-042 / BR-6). The routing to supervisor/HR depends on the reporting structure (US-CHR-011); the multi-level routing engine (US-ADM-007) is DEFERRED, but the deny-self-approval + route-to-supervisor default is verified live. **Reported to caller.**
