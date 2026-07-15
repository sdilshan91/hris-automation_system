---
id: TC-ATT-ISO-015
user_story: US-ATT-011
module: Attendance
priority: critical
type: security
status: draft
created: 2026-07-15
---

# TC-ATT-ISO-015: Multi-tenant isolation — a cross-tenant AttendanceSettings override LocationId never resolves (AC-3 / BR-1)

## 1. Test Objective
Verify US-ATT-011 AC-3 / BR-1 / NFR-2 and spec §7.1: an `AttendanceSettings` override whose `LocationId` points at **another tenant's Location** is never accepted and never resolves; a Tenant A employee never receives Tenant B's policy override. Targets **real Postgres** (query filter / RLS), not InMemory.

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-3
- Business Rule: BR-1
- NFR-2 (isolation on the override `LocationId`)
- Critical Rule #1

## 3. Preconditions
- Tenant A with `AttendanceSettings` and an employee; Tenant B with a Location `locB`.
- Two-tenant Postgres integration setup.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A employee | at Tenant A location | policy consumer |
| Tenant B | Location `locB` | foreign override target |
| Override attempt | `LocationId = locB.Id` in Tenant A | must be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In Tenant A's context, create an `AttendanceSettings` override with `LocationId = locB.Id`. | Rejected — `locB` is not found under Tenant A's query filter; override not persisted. |
| 2 | Resolve effective `AttendanceSettings` for the Tenant A employee. | Tenant A default policy only; Tenant B's Location never contributes an override. |
| 3 | From Tenant B, confirm `locB` still has no Tenant-A override row referencing it. | Isolation intact both directions. |

## 6. Postconditions
- No cross-tenant override is created or resolved.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
