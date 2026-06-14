---
id: TC-LV-252
user_story: US-LV-012
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-14
---

# TC-LV-252: Input sanitization on report filters + cross-tenant/cross-team IDOR on employeeId/departmentId

## 1. Test Objective
Verify the report query parameters (employee search `q`, date range, filter IDs) are safe against XSS and SQL injection, and that supplying another tenant's or another team's `employeeId`/`departmentId` does not leak data (no IDOR), the filter being intersected with the caller's tenant + role scope.

## 2. Related Requirements
- User Story: US-LV-012
- Non-Functional Requirements: NFR-2, NFR-3
- Business Rules: BR-1, BR-2
- Functional Requirements: FR-2, FR-6

## 3. Preconditions
- Tenant "acme" (HR Mark) and Tenant "globex" (employee Kofi) with report data; departments in each.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS | `q=<script>alert(1)</script>` | search field |
| SQLi | `q=' OR 1=1 --` | injection probe |
| Foreign id | globex employeeId / departmentId | cross-tenant IDOR probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit the employee-search filter with an XSS payload | The value is treated as literal text (parameterized query); it is stored/echoed safely-encoded and never executes in the rendered report/export. |
| 2 | Submit a SQL-injection payload in a filter | The query is parameterized; no extra rows returned, no error leaking SQL; the result is an empty/normal match. |
| 3 | As acme HR, pass a globex `employeeId`/`departmentId` filter | No globex data returned — the filter is intersected with acme's tenant scope (empty/own-tenant result), no IDOR (cross-ref TC-LV-ISO-045). |
| 4 | As an acme manager, pass an out-of-team `employeeId` | No out-of-team data returned (role scope enforced, cross-ref TC-LV-246). |

## 6. Postconditions
- Report filters are injection-safe and cannot be used to read cross-tenant or out-of-scope data.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
