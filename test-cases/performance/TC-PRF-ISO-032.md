---
id: TC-PRF-ISO-032
user_story: US-PRF-008
module: Performance Management
priority: high
type: security
status: pass
created: 2026-06-16
---

# TC-PRF-ISO-032: Tenant-scoped PIP Hangfire jobs (checkpoint reminders / end-date / overdue / ack-timeout) + checkpoint-attachment storage + notifications + audit + report artifacts (NFR-2)

## 1. Test Objective
Verify NFR-2 across the PIP's asynchronous + side-effect surfaces: the Hangfire jobs (PIP start, checkpoint reminders, end-date reminder, overdue-checkpoint alerts, 5-business-day acknowledgement timeout), the checkpoint file-attachment storage paths, the PIP notifications (initiation / checkpoint outcome / escalation), the immutable audit/history entries, and the PIP report artifacts are ALL tenant-scoped -- each runs in / is keyed by the owning tenant and never touches another tenant's data or recipients.

> Note: any aggregate/list cache for PIPs is CONDITIONAL on a cache layer existing (S10) -- if PIP lists are computed on demand today, this asserts tenant-filtered queries with no shared/global key, and documents a tenant-scoped cache key (tenantId, …) as the extension point (consistent with the module's deferred-Redis convention).

## 2. Related Requirements
- User Story: US-PRF-008
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-3 (Hangfire jobs), FR-4 (attachments), FR-5 (audit/history), FR-7 (report)
- Business Rules: BR-4 (ack-timeout job)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" each have Active PIPs with checkpoints, attachments, and scheduled Hangfire jobs.
- Hangfire + the Notification System (S25) are available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | PIPs + jobs + attachments |
| Tenant B | globex | PIPs + jobs + attachments |
| Job types | start / checkpoint-reminder / end / overdue / ack-timeout | FR-3, BR-4 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Trigger acme's PIP Hangfire jobs (reminders / overdue / end / ack-timeout) | Each runs in acme tenant context and reads/notifies only acme PIPs + recipients (employee/manager/HR/mentor); never globex (NFR-2). |
| 2 | Inspect checkpoint-attachment storage | acme attachments are stored under an acme-scoped path/prefix; globex attachments under a globex-scoped path; neither tenant's path can resolve the other's files. |
| 3 | Inspect PIP notifications (initiation / checkpoint outcome / escalation) | Each notification is scoped to the owning tenant's recipients; no cross-tenant recipient receives a PIP notification. |
| 4 | Inspect audit/history entries | Every PIP audit/history entry carries the owning tenant_id; an acme query never returns globex history (and vice versa). |
| 5 | Generate the PIP report as acme | The report artifact contains only acme data + acme branding; no globex PIP data appears; the artifact is tenant-scoped. |
| 6 | Any PIP list/aggregate cache | Cache keys (if a cache exists) include tenant_id; no shared/global key; else on-demand queries are tenant-filtered (S10 conditional). |

## 6. Postconditions
- All PIP Hangfire jobs, attachment storage, notifications, audit/history, and report artifacts are tenant-scoped; no cross-tenant async/side-effect leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
