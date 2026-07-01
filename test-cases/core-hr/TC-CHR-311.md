---
id: TC-CHR-311
user_story: US-CHR-012
module: Core HR
priority: high
type: performance
status: blocked
created: 2026-06-13
exec_note: "P3b k6 2026-06-30: WRITE ARM NOT MEASURED — kept blocked. Read arm met: custom-fields list (GET /tenant/custom-fields) p95 57–70ms @50VU on 5k perf tenant < 400ms SLA. But the TC also requires POST/PUT/reorder writes <=800ms P95, and write-load was not measured this pass. Re-run after scripting the custom-field write flow."

# TC-CHR-311: Custom field configuration API response times within SLA

## 1. Test Objective
Verify that custom field configuration API endpoints meet the P95 response time SLAs: read operations within 400ms and write operations within 800ms. This validates NFR-1.

**Type: Observational / Performance test.**

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with 20 custom fields defined for the Employee entity.
- Tenant Admin is authenticated.
- The system is under typical load (not stress-tested).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom fields count | 20 | Professional plan capacity |
| Measurement | P95 latency | Across 100 sequential requests |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Execute 100 sequential `GET /api/v1/tenant/custom-fields?entityType=Employee` requests. Record response times. | P95 response time <= 400ms. |
| 2 | Execute 100 sequential `POST /api/v1/tenant/custom-fields` requests (creating and then deleting fields to stay within limits). Record response times. | P95 response time <= 800ms. |
| 3 | Execute 100 sequential `PUT /api/v1/tenant/custom-fields/{id}` requests (updating field names). Record response times. | P95 response time <= 800ms. |
| 4 | Execute 100 sequential reorder operations. Record response times. | P95 response time <= 800ms. |

## 6. Postconditions
- Performance metrics recorded. Any SLA breach is flagged for investigation.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30:** STILL BLOCKED — performance/API-SLA TC (response-time / JSONB-GIN query / form-render budget). Needs an instrumented perf/load harness, not a browser-render check. Custom-fields UI is also non-functional (BUG-100).
