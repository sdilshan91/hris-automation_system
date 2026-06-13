---
id: TC-LV-ISO-011
user_story: US-LV-003
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-011: EF global query filters block cross-tenant access to leave_request rows

## 1. Test Objective
Verify that the read-isolation layer (EF Core global query filters on `leave_request`, equivalent to the story's RLS requirement) prevents any query in Tenant A's context from returning Tenant B's leave request rows, including via overlap checks, list, and detail queries.

## 2. Related Requirements
- User Story: US-LV-003
- Non-Functional Requirements: NFR-4
- Data Requirements: Section 7 (leave_request table)

## 3. Preconditions
- Tenant "acme" has leave requests R1, R2 for its employees.
- Tenant "globex" has leave requests R3, R4 for its employees.
- A user with `Leave.Apply` is authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme requests | R1, R2 | Visible only in acme |
| globex requests | R3, R4 | Visible only in globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In acme context, list leave requests via the application query path | Returns only R1, R2. R3/R4 are excluded by the global query filter. |
| 2 | In acme context, run the overlap-detection check for a date range that R3 (globex) covers | Overlap check ignores R3 -- a globex request never blocks an acme submission. |
| 3 | In acme context, attempt to load R3 by UUID | Returns null/404; the filtered query excludes it. |
| 4 | Confirm at the raw-DB level using a tenant-scoped query | `SELECT * FROM leave_request WHERE tenant_id = acme_id` returns only R1, R2; `WHERE tenant_id = globex_id` returns only R3, R4. |
| 5 | Verify `IgnoreQueryFilters()` is not used on any leave_request read in the application path | Application leave queries are always tenant-filtered. |

## 6. Postconditions
- No cross-tenant leave_request rows are returned in any query path.
- Overlap detection is scoped strictly to the resolved tenant.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
