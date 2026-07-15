---
id: TC-CHR-326
user_story: US-CHR-013
module: Core HR
priority: high
type: integration
status: automated
created: 2026-07-15
---

# TC-CHR-326: Employee.Fte accepts 0.5 and prorates leave entitlement to half (AC-1 happy path)

## 1. Test Objective
Verify US-CHR-013 AC-1 / FR-1 / FR-3 / BR-2: an HR Officer can set `Employee.Fte = 0.5` on create/edit, the value persists (default 1.0, 2 dp), and it is wired into `LeaveEntitlementEngine.CalculateProRata` so a 0.5-FTE employee receives **exactly half** the full-year entitlement (replacing the previously hardcoded `1.0`).

## 2. Related Requirements
- User Story: US-CHR-013
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-3
- Business Rule: BR-2
- Cross-reference: US-LV-002 BR-2 / AC-K1 (pro-rata consumes FTE)

## 3. Preconditions
- A full-year employee eligible for a leave type with a known full entitlement (e.g. 20 days/year).
- Actor holds HR Officer / HR Manager / Tenant Admin.
- Postgres-backed context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Fte | 0.50 | half-time |
| Full-year entitlement | 20.00 days | at Fte 1.0 |
| Expected pro-rata | 10.00 days | 20 × 0.5 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create/edit the employee with `Fte = 0.5`; save. | 200/201; employee echoes `fte = 0.50`. |
| 2 | Compute/allocate leave entitlement for a full year at Fte 0.5. | Entitlement == **10.00** days (exactly half). |
| 3 | Repeat with a control employee at `Fte = 1.0`. | Entitlement == 20.00 days (unchanged baseline). |

## 6. Postconditions
- FTE persists on the employee and proportionally scales leave entitlement.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite):**
  - `FteProrationTests.HalfFteEmployee_GetsExactlyHalfTheEntitlement_ComputeEffectivePath` (0.5 FTE → 20 days becomes exactly 10)
  - `FteProrationTests.BatchPath_HonoursFte_PerEmployee` + `FteProrationTests.AccrualPath_HonoursFte_WhenCreditingTheLedger` — **all THREE `CalculateProRata` call sites** are driven independently; a fix at one with two left hardcoded fails here
  - `FteProrationTests.FullTimeEmployee_EntitlementIsUnchanged` + `..._EmployeeWrittenWithoutAnFte_DefaultsTo1_00_AndIsUnchanged` (the no-regression controls)
  - `FteProrationTests.Fte_RoundTripsAtNumeric3_2_AndProratesExactly` (real Postgres — the numeric(3,2) column scale)
- **Mutation-verified:** reverting ONE of the three sites to `fte: 1.0m` reddens only that site's arm.
- **Closes US-LV-002 AC-K1** — the engine always accepted `fte`; all three callers hardcoded 1.0.
- Backing suite trait: `[Trait("TC", "TC-CHR-326")]`.
