---
id: TC-CHR-145
user_story: US-CHR-003
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-145: Export 10,000 rows completes within 5 minutes or is async (NFR-5)

## 1. Test Objective
Verify that exporting 10,000 employee rows completes within 5 minutes. For datasets above 10,000, the export should be processed as an async Hangfire background job with user notification. This validates NFR-5.

## 2. Related Requirements
- User Story: US-CHR-003
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "large-corp" exists with status `active`.
- HR Officer is authenticated in "large-corp".
- 10,000 employee records exist in the "large-corp" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant | large-corp | Large dataset |
| Employee count | 10,000 | NFR-5 threshold |
| Export format | CSV | Faster than Excel |
| Max time | 5 minutes | 300,000ms |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "large-corp" | JWT contains full access. |
| 2 | Send `GET /api/v1/tenant/employees/directory/export?format=Csv` (no filters, all 10,000) | Either: (a) synchronous response with CSV file within 5 minutes, or (b) 202 Accepted with a background job ID. |
| 3 | If synchronous: verify the CSV has 10,000 data rows + 1 header | File size is reasonable; all rows present. |
| 4 | If async: poll the job status endpoint | Job completes within 5 minutes; download link is provided. |
| 5 | Verify download contains 10,000 rows | All employees present, no truncation. |
| 6 | Repeat with Excel format | Same constraints: 10,000 rows within 5 minutes. |
| 7 | Verify no timeout or 500 error | The server does not crash or time out. |

## 6. Postconditions
- Export completed successfully within SLA.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
