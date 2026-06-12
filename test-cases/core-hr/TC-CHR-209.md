---
id: TC-CHR-209
user_story: US-CHR-008
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-209: Performance -- file upload within 5 seconds for 10 MB; API read/write within SLA

## 1. Test Objective
Verify that document upload completes within 5 seconds for a 10 MB file on a stable connection (NFR-1), document list API read responses are within 400ms P95, and document upload/delete write responses are within 800ms P95 (matching the platform SLA). This validates NFR-1 and the general performance SLA.

## 2. Related Requirements
- User Story: US-CHR-008
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) exists with 20 existing documents (to test list performance under load).
- Stable network connection (> 10 Mbps upload).
- Object storage and virus scanner are operational.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Upload File | perf-test.pdf | Exactly 10 MB (10,485,760 bytes) |
| Existing Documents | 20 | Pre-loaded for Jane Doe |
| Network | Stable, > 10 Mbps upload | Controlled test environment |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Measure the time to upload "perf-test.pdf" (10 MB) via `POST /api/v1/tenant/employees/{emp-001-uuid}/documents`. | Total upload time (client-side from submit to 201 response) is <= 5 seconds. This includes: network transfer, server-side MIME validation, virus scan, storage write, and metadata DB insert. |
| 2 | Repeat the upload 20 times and calculate P95 latency. | P95 upload latency is <= 5 seconds. |
| 3 | Measure `GET /api/v1/tenant/employees/{emp-001-uuid}/documents` response time. | Response time is <= 400ms P95. Response contains the full document list (21 documents). |
| 4 | Repeat the list request 50 times and calculate P95 latency. | P95 read latency is <= 400ms. |
| 5 | Measure `GET /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-id}/download` response time (signed URL generation, not file download). | Signed URL generation response time is <= 400ms P95. |
| 6 | Measure `DELETE /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-id}` response time. | Soft-delete response time is <= 800ms P95. |

## 6. Postconditions
- All performance measurements are within the defined SLA thresholds.
- Test documents can be cleaned up.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
