---
id: TC-CHR-262
user_story: US-CHR-010
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-262: Performance -- 10,000-row file processed within 5 minutes with bounded memory usage

## 1. Test Objective
Verify that a bulk import of 10,000 rows completes within 5 minutes (NFR-1) when processed asynchronously. Also observe server memory usage to verify the system streams/chunk-reads the file rather than loading it entirely into memory (NFR-6). This is an observational/performance test.

## 2. Related Requirements
- User Story: US-CHR-010
- Non-Functional Requirements: NFR-1, NFR-6

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient capacity (plan limit > 10,000 or unlimited).
- An HR Officer user is authenticated.
- A valid 10,000-row CSV file is prepared with unique emails and valid department/job title references.
- Server monitoring tools (e.g., dotnet-counters, Prometheus, or equivalent) are available to observe memory.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | perf_10000.csv | 10,000 valid rows |
| File Size | ~5 MB | Well under 25 MB limit |
| Expected Processing Time | <= 5 minutes | NFR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Record baseline server memory usage before import. | Memory reading noted (e.g., ~300 MB working set). |
| 2 | Upload `perf_10000.csv` and click "Import". | System queues the import as a Hangfire background job (> 500 rows). |
| 3 | Start a timer. Monitor the progress indicator. | Progress updates as batches are processed. |
| 4 | Monitor server memory during processing (via dotnet-counters or equivalent). | Memory usage does NOT spike by more than ~100 MB above baseline. The file is streamed/chunked, not loaded entirely into memory. (NOTE: exact threshold is observational; the goal is no excessive allocation per NFR-6.) |
| 5 | Wait for the job to complete. Stop the timer. | Import completes. Total elapsed time <= 5 minutes. |
| 6 | Verify the results. | 10,000 employees created. Summary: "10,000 of 10,000 records imported successfully." |
| 7 | Verify server memory returns to near baseline after GC. | Memory usage stabilizes close to the pre-import level (no memory leak). |

## 6. Postconditions
- 10,000 employees created within the 5-minute SLA.
- Memory usage bounded during processing.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
