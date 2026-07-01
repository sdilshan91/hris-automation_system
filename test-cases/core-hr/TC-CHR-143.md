---
id: TC-CHR-143
user_story: US-CHR-003
module: Core HR
priority: critical
type: performance
status: blocked
created: 2026-06-12
exec_note: "STILL BLOCKED on the dominant arm (FE TTI<=2.5s P95, Chrome-DevTools-only, blocked by BUG-097/099). But the S2/50k run 2026-07-01 DID measure this TC's step-5 API arm at scale and it now BREACHES: the employee LIST read `GET /api/v1/tenant/employees?pageSize=20` p95 **477.72ms** @50VU on the **50,000-employee** perf tenant vs the step-5 <=400ms read SLA — FAIL on the API-read arm (was 212ms at 5k). See BUG-123 (which also cites this 477ms list regression). The FE-TTI acceptance arm remains unmeasurable this pass, so the TC stays blocked overall, but note the underlying read is no longer within 400ms at 50k. Evidence: perf/results/50k-hot.json (employees p95 477.72ms). [Prior P3b 5k: list read 212ms; NOT MEASURABLE by k6 note was about the FE arm only.]"

# TC-CHR-143: Directory page load within 2.5 seconds P95 at 5,000 employees (NFR-1)

## 1. Test Objective
Verify that the Employee Directory page (including API response, rendering, and interactive state) loads within 2.5 seconds at P95 for a tenant with up to 5,000 employees. This validates NFR-1.

## 2. Related Requirements
- User Story: US-CHR-003
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "perf-test" exists with status `active`.
- HR Officer is authenticated in "perf-test".
- 5,000 employee records exist in the "perf-test" tenant with varied departments, statuses, and job titles.
- Performance testing environment is stable (no other load).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant | perf-test | Large tenant |
| Employee count | 5,000 | NFR-1 threshold |
| Page size | 20 | Default |
| Iterations | 100 | For P95 calculation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clear browser cache and navigate to the Employee Directory | Page begins loading. |
| 2 | Measure Time to Interactive (TTI) | TTI is recorded from navigation start to the moment all skeleton placeholders are replaced and pagination controls are interactive. |
| 3 | Repeat step 1-2 for 100 iterations (automated) | Collect 100 TTI measurements. |
| 4 | Calculate P95 of TTI measurements | P95 is less than or equal to 2,500ms. |
| 5 | Measure API response time for `GET /api/v1/tenant/employees/directory?page=1&pageSize=20` | P95 API response time is less than or equal to 400ms (read SLA). |
| 6 | Verify no timeouts or errors in 100 iterations | Zero 500 errors, zero timeouts. |

## 6. Postconditions
- Performance metrics are recorded for baseline tracking.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Re-exec 2026-06-30 (deep FE-render pass, CDP):** STILL BLOCKED — triple-gated. Metric is **cold-load TTI P95 of the Employee Directory at 5,000 employees** (step 2: "navigation start to … skeletons replaced + pagination interactive"). (a) Cold-load TTI needs a full navigation that logs you out (**BUG-097**); (b) the directory list itself crashes on render — `EmployeeListComponent_Template … reading 'length'` of undefined, list body never paints (**BUG-099**) — so there is no "skeletons replaced/interactive" moment to time; (c) acme has 34 employees, not the 5k scale this TC requires. No measurable arm until BUG-097 + BUG-099 fixed and a 5k seed exists.
