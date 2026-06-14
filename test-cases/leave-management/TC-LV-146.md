---
id: TC-LV-146
user_story: US-LV-007
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-14
---

# TC-LV-146: Input sanitization -- XSS/SQLi in holiday name, description, location filter, and CSV cells (NFR-2)

## 1. Test Objective
Verify that malicious input in holiday fields (name, description), query parameters (locationId, year, from/to), and CSV cell values is neutralized: persisted as inert data, rendered escaped in the calendar/list views, and never executed or used to inject SQL (NFR-2).

## 2. Related Requirements
- User Story: US-LV-007
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1, FR-4

## 3. Preconditions
- Tenant "acme" active; HR Officer "Priya" authenticated with `Holiday.Create` / `Holiday.Import`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS name | `<script>alert('xss')</script>` | stored/escaped |
| XSS description | `<img src=x onerror=alert(1)>` | stored/escaped |
| SQLi date param | `2026-01-01'; DROP TABLE holiday;--` | rejected/parameterized |
| Malformed year | `abc`, `2026 OR 1=1` | rejected, no injection |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create a holiday with the XSS name/description | Stored verbatim as text; on render in calendar/list the markup is escaped and does NOT execute. |
| 2 | Call GET with SQLi-laden `from`/`to`/`year`/`locationId` params | Parameters are bound/validated; malformed values yield a 400 (or empty result), never a SQL error or data leak. |
| 3 | Import a CSV whose name/description cells contain XSS payloads | Cells stored as inert text; no script executes when the imported rows render. |
| 4 | Verify CSV formula-injection guard | Cells beginning with `=`, `+`, `-`, `@` are not executed as spreadsheet formulas on any export round-trip (treated as text). |

## 6. Postconditions
- All holiday inputs are sanitized; no stored/reflected XSS or SQL injection is possible.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
