---
id: TC-PRF-ISO-024
user_story: US-PRF-006
module: Performance Management
priority: high
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-024: Hangfire auto-close jobs + sign-off/dispute/auto-close notifications + audit + PDF export are tenant-scoped (NFR-2)

## 1. Test Objective
Verify NFR-2 across the asynchronous + cross-cutting surfaces of the sign-off workflow: the Hangfire auto-close job (BR-3) runs in the originating tenant's context and only touches that tenant's pending reviews; sign-off / dispute / auto-close notifications are addressed only to that tenant's recipients; audit entries (FR-7) are tenant-scoped; the PDF export (FR-6) reads only the calling tenant's review; and any results/notes cache keys are tenant-scoped. No job, notification, audit entry, cache key, or export crosses tenants.

> Note: notification/PDF DELIVERY is CONDITIONAL on the Notification System (S25) and the PDF library being wired; the enqueue + tenant-scoping of the job/notification/audit/cache seams are asserted regardless.

## 2. Related Requirements
- User Story: US-PRF-006
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-5 (HR escalation), FR-6 (PDF export), FR-7 (audit)
- Business Rules: BR-3 (auto-close)

## 3. Preconditions
- Tenants "acme" and "globex" each have pending-sign-off reviews; each has its own HR recipient.
- Hangfire is running; audit + (optional) notification/PDF seams are wired.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auto-close window | short (test) | BR-3 per-tenant config |
| Recipients | acme HR / globex HR | per-tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Let acme + globex each have an overdue pending-sign-off review; run the auto-close job | The job processes each tenant's overdue reviews IN that tenant's context; acme's job never closes a globex review and vice versa (NFR-2/BR-3). |
| 2 | Inspect auto-close notifications | acme's "No Response" notice goes only to acme HR; globex's only to globex HR; no cross-tenant recipient (S25 enqueue asserted). |
| 3 | Trigger a sign-off + a dispute in acme; inspect notifications | Sign-off/dispute notifications target only acme manager/HR; addressed by tenant-scoped recipient resolution. |
| 4 | Inspect audit entries for all sign-off/dispute/auto-close actions | Each audit entry carries tenant_id of its originating tenant; querying acme's audit log returns zero globex sign-off entries (FR-7/NFR-2). |
| 5 | Export an acme signed review's PDF; attempt the same id from globex | acme export returns acme's document; the globex-context export of acme's reviewId returns 404 (tenant-scoped, FR-6). |
| 6 | If a results/notes cache (S10) is used | Cache keys embed tenant id (no shared/global key); a globex read never returns an acme-cached document. |

## 6. Postconditions
- Auto-close jobs, notifications, audit entries, exports, and caches for the sign-off workflow are strictly tenant-scoped; nothing crosses tenants.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
