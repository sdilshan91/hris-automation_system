---
id: TC-CHR-143
user_story: US-CHR-003
module: Core HR
priority: critical
type: performance
status: draft
created: 2026-06-12
---

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
