---
id: TC-PRF-ISO-008
user_story: US-PRF-002
module: Performance Management
priority: high
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-008: Attachment storage paths, auto-save drafts, and reminder/submission notifications are tenant-scoped (NFR-2, NFR-3, NFR-4)

## 1. Test Objective
Verify NFR-2 across the new side-effect surfaces of US-PRF-002: evidence-file storage paths are tenant-scoped and not cross-tenant retrievable (NFR-4), auto-saved drafts (NFR-3) persist under the owning tenant only, and self-assessment notifications -- the manager submission notice and the Hangfire deadline reminder -- are delivered only within the owning tenant (no cross-tenant leak).

## 2. Related Requirements
- User Story: US-PRF-002
- Non-Functional Requirements: NFR-2, NFR-3, NFR-4
- Functional Requirements: FR-7 (notifications)

## 3. Preconditions
- acme and globex each have an employee with goals in an active cycle and a manager.
- Both tenants have evidence files uploaded; both have non-submitters near a deadline.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Storage path pattern | `tenant/{tenantId}/performance/self-assessment/...` | must include tenant id |
| Auto-save draft | acme draft | must stay acme-only |
| Submission notice | acme manager | acme-only |
| Reminder | acme non-submitter | acme-only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload an evidence file in acme; inspect its storage path | Path includes acme's tenant id; not in a shared/global folder. A globex user cannot fetch it by path/URL (NFR-4). |
| 2 | Auto-save an acme draft (NFR-3) and inspect persistence | The draft row carries tenant_id=acme; it is invisible to globex queries/sessions and to globex API reads. |
| 3 | Submit an acme self-assessment; check who is notified | Only acme's manager receives the submission notification; no globex user is notified (FR-7). |
| 4 | Run the deadline reminder job for acme | Only acme non-submitters are reminded; the job arg carries acme's tenant_id; globex employees receive nothing from the acme run. |
| 5 | Repeat the reminder run for globex | Only globex non-submitters are reminded; acme employees receive nothing. (Any cache/queue key backing drafts or notifications is tenant-scoped -- CONDITIONAL if computed on demand today, asserted as tenant-filtered with no shared/global key.) |

## 6. Postconditions
- Attachments, auto-saved drafts, and all self-assessment notifications/reminders are strictly tenant-scoped; no cross-tenant retrieval or leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
