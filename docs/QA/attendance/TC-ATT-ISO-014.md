---
id: TC-ATT-ISO-014
user_story: US-ATT-011
module: Attendance
priority: critical
type: security
status: draft
created: 2026-07-15
---

# TC-ATT-ISO-014: Multi-tenant isolation — a cross-tenant Location.DefaultShiftId never resolves (AC-1 / BR-1)

## 1. Test Objective
Verify US-ATT-011 AC-1 / BR-1 / NFR-2 and spec §7.1: setting `Location.DefaultShiftId` to a **Shift belonging to another tenant** is never accepted and never resolves — the foreign shift id is invisible under the EF global query filter / RLS, so the write is rejected and the Location tier stays empty. Targets **real Postgres** behaviour (InMemory masks the query filter / FK).

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-1
- Business Rule: BR-1 (cross-tenant reference never resolves)
- NFR-2 (RLS + query filters enforce isolation on `DefaultShiftId`)
- Critical Rule #1 (tenant isolation non-negotiable)

## 3. Preconditions
- Tenant A with a Location; Tenant B with an active Shift `shiftB`.
- Two-tenant Postgres integration setup (not InMemory).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | Location `locA` | target of the write |
| Tenant B | Shift `shiftB` (active) | foreign FK target |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In Tenant A's context, set `locA.DefaultShiftId = shiftB.Id`. | Rejected — `shiftB` is not found under Tenant A's query filter (validation/404-not-403 style; never a silent accept). |
| 2 | Re-read `locA`. | `DefaultShiftId` remains null; no cross-tenant FK persisted. |
| 3 | Resolve the working-day set for a Tenant A employee at `locA`. | Falls through to Tenant A's tenant/code default — Tenant B's shift never contributes. |

## 6. Postconditions
- No cross-tenant shift is ever wired or resolved; isolation holds at the query layer.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
