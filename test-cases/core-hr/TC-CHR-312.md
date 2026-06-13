---
id: TC-CHR-312
user_story: US-CHR-012
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-13
---

# TC-CHR-312: JSONB query by custom field value within 500ms at 10,000 employees with GIN index

## 1. Test Objective
Verify that querying employees by a custom field value using JSONB operators completes within 500ms for a dataset of 10,000 employees, aided by the GIN index on the `custom_fields` column. This validates NFR-3 and FR-11.

**Type: Observational / Performance test.**

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-11

## 3. Preconditions
- Tenant "acme" exists with 10,000 employee records.
- A custom field "Department Code" (text) exists.
- All 10,000 employees have a `custom_fields` JSONB value containing `department_code` with varying values.
- A GIN index exists on the `custom_fields` column of the employees table.
- Tenant Admin or HR Officer is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee count | 10,000 | Within single tenant |
| Custom field | Department Code | Text type |
| Query value | "ENG-001" | Approximately 200 employees match |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Execute a JSONB query via the advanced filter API: employees where `custom_fields->>'department_code' = 'ENG-001'`. | Results returned with matching employees. |
| 2 | Record the response time for 20 sequential executions of the query. | P95 response time <= 500ms. |
| 3 | Verify the query plan uses the GIN index (via `EXPLAIN ANALYZE` on the raw SQL). | The query plan shows an index scan on the GIN index, not a sequential scan. |

## 6. Postconditions
- Query performance is within SLA. GIN index is utilized.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
