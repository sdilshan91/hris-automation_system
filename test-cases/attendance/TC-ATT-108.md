---
id: TC-ATT-108
user_story: US-ATT-008
module: Attendance
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-ATT-108: Chronic lateness escalation -- 5 lates/month crosses the configured chronic threshold and triggers an HR escalation seam (FR-7; notification dispatch DEFERRED on US-NTF)

## 1. Test Objective
Verify FR-7: when an employee's monthly late count crosses the tenant-configured `chronic_threshold`, the system raises a chronic-lateness escalation targeted at HR. The escalation SEAM (recipient = HR, tenant-scoped, payload references the employee + monthly late total) is verified now; end-to-end in-app/email delivery is DEFERRED on the Notification System (US-NTF), consistent with prior Attendance HR-alert seams (US-ATT-006 TC-ATT-071).

## 2. Related Requirements
- User Story: US-ATT-008
- Functional Requirements: FR-7 (configurable chronic-lateness threshold -> HR escalation)
- Data: late_policy.chronic_threshold (S7)
- Dependency: Notification System (US-NTF) for delivery

## 3. Preconditions
- Tenant "acme"; late_policy with `chronic_threshold = 5`, period = MONTHLY, is_active = true.
- Employee "Asha" on a 09:00 SINGLE shift, 15-min grace, with 4 late arrivals already recorded this month.
- An HR Officer recipient resolvable for escalation routing.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| chronic_threshold | 5 | lates/month for HR escalation |
| existing lates this month | 4 | below threshold |
| 5th late clock-in | 09:25 (past grace) | crosses threshold |
| expected escalation recipient | HR (tenant-scoped) | FR-7 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With 4 lates recorded, verify no chronic escalation has fired | No HR escalation event exists yet (4 < 5). |
| 2 | Record Asha's 5th late arrival of the month (clock-in past grace) | The 5th late is flagged is_late; the monthly late count reaches 5, crossing chronic_threshold. |
| 3 | Inspect the escalation seam | A chronic-lateness escalation is queued/logged with recipient = HR (tenant acme), payload referencing Asha's employee_id + monthly late count (5). The seam is tenant-scoped (no cross-tenant HR recipient). |
| 4 | Verify delivery is DEFERRED | End-to-end in-app/email delivery + HR badge assertions are DEFERRED on US-NTF; only the dispatch seam is asserted now. |
| 5 | Verify no duplicate escalation | A 6th late in the same month does not re-fire the chronic escalation for the same threshold crossing (single escalation per crossing) -- record observed behaviour if de-dup is not yet implemented. |

## 6. Postconditions
- A tenant-scoped chronic-lateness escalation seam targeting HR exists for Asha; no end-to-end notification asserted (DEFERRED on US-NTF).

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Notification dispatch DEFERRED on US-NTF (FR-7).** Mirrors US-ATT-006 TC-ATT-071 (HR weekly-cap alert) and US-ATT-003/004 notification seams. The chronic-lateness escalation recipient/payload/tenant-scope are verified now; in-app/email delivery is DEFERRED until US-NTF. **Reported to caller.**
- **Re-fire/de-dup semantics** (Step 5) are a story ambiguity -- FR-7 does not state whether each additional late past the threshold re-escalates. **Reported to caller.**
