---
id: TC-CHR-ISO-049
user_story: US-CHR-013
module: Core HR
priority: high
type: security
status: automated
created: 2026-07-15
---

# TC-CHR-ISO-049: Multi-tenant isolation — FTE / WorkArrangement edits in Tenant A never touch Tenant B (NFR-1)

## 1. Test Objective
Verify US-CHR-013 NFR-1 and Critical Rule #1: `Employee.Fte` and `Employee.WorkArrangement` live on the already tenant-isolated `employee` row, so an edit performed in Tenant A's context can never read or mutate a Tenant B employee's FTE/work-arrangement. Targets **real Postgres** (EF global query filter / RLS), not InMemory.

## 2. Related Requirements
- User Story: US-CHR-013
- NFR-1 (RLS + query filters isolate FTE/work-arrangement)
- Critical Rule #1

## 3. Preconditions
- Tenant A employee `empA` and Tenant B employee `empB`, both with known Fte/WorkArrangement.
- Two-tenant Postgres integration setup.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| empA | Tenant A, Fte 1.0 | actor's own |
| empB | Tenant B, Fte 0.8, Remote | victim |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In Tenant A's context, attempt to PATCH `empB`'s Fte/WorkArrangement by id. | 404-not-found (empB invisible under Tenant A filter); no mutation. |
| 2 | In Tenant A's context, GET `empB` by id. | 404; empB's FTE/work-arrangement never returned to Tenant A. |
| 3 | From Tenant B, re-read `empB`. | Fte 0.8 / Remote unchanged — no cross-tenant write occurred. |

## 6. Postconditions
- FTE/work-arrangement remain strictly tenant-isolated.

## 6a. Automation
Bound 2026-08-06 by `HRM.Tests/Integration/Http/RlsOnAuthFlowsApiTests.cs` →
`Another_tenants_employee_is_invisible_and_unmutable_under_RLS_TC_CHR_ISO_049`
(`[Trait("TC", "TC-CHR-ISO-049")]`).

Implemented on the RLS-ON HTTP harness (`RlsOnApiTestFactory`) rather than as a plain integration test,
because this TC explicitly targets real Postgres isolation: the harness runs the app as the NOBYPASSRLS
`hrm_app` role with `ENABLE + FORCE ROW LEVEL SECURITY` applied, so both the EF query filter AND the RLS
policy are exercised through the genuine HTTP pipeline. The victim row is seeded on the privileged
connection so the arrangement itself is not subject to the isolation under test.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
