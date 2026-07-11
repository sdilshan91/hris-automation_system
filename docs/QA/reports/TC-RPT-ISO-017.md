---
id: TC-RPT-ISO-017
user_story: US-RPT-005
module: Reports & Analytics
priority: critical
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: BUG-003 cross-tenant read leak CONFIRMED — isoa-admin token + X-Tenant-Subdomain:isob returns isob data (employee list ISO-2b1b-*, headcount/dashboard/leave report run under isob context). Systemic, already filed (ISSUE-193 / BUG-003 class)."
created: 2026-06-17
---

# TC-RPT-ISO-017: Dashboards are independent across tenants -- Tenant A vs Tenant B show only own data, no cross-tenant leakage (AC-5, FR-8, BR-1)

## 1. Test Objective
Verify that `GET /api/v1/dashboard/widgets` is tenant-scoped end to end: an HR Officer in Tenant A and an HR
Officer in Tenant B, with deliberately different seeded data, each see ONLY their own tenant's metrics in
every widget. No headcount, leave, recruitment, attendance, or onboarding value from one tenant ever appears
in the other's dashboard. Validates AC-5, FR-8, BR-1.

## 2. Related Requirements
- User Story: US-RPT-005
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8 (scoped by tenant_id from session)
- Business Rules: BR-1 (role-based, tenant-scoped widgets)

## 3. Preconditions
- Tenant A: 50 employees, 5 pending leave, 3 open positions. Tenant B: 12 employees, 1 pending leave, 0 open positions.
- `hrA` authenticated in Tenant A; `hrB` authenticated in Tenant B.

## 4. Test Data
| Field | Tenant A | Tenant B |
|-------|----------|----------|
| headcount | 50 | 12 |
| pending_leave | 5 | 1 |
| open_positions | 3 | 0 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, `GET /api/v1/dashboard/widgets` | `headcount==50`, `pending_leave==5`, `open_positions==3` (Tenant A only) |
| 2 | As `hrB`, `GET /api/v1/dashboard/widgets` | `headcount==12`, `pending_leave==1`, `open_positions==0` (Tenant B only) |
| 3 | Compare the two responses | NO Tenant B value appears in `hrA`'s dashboard and vice versa; widget item lists (joiners, birthdays) are disjoint by tenant |
| 4 | Confirm `generatedAt`/`greetingName` are per-user | each reflects the correct user; no bleed |
| 5 | Confirm all per-module aggregations are scoped | leave/attendance/recruitment/onboarding widgets all reflect only the caller's tenant (FR-8) |

## 6. Postconditions
- Each tenant's dashboard shows strictly its own data; no cross-tenant leakage.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
