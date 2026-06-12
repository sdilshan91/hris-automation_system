---
id: TC-CHR-261
user_story: US-CHR-010
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-261: Transaction behavior -- synchronous import uses all-or-nothing per batch; async uses batch commits with per-batch rollback

## 1. Test Objective
Verify the transactional guarantees described in NFR-4: synchronous imports (<= 500 rows) use all-or-nothing semantics per batch, and async imports use batch commits of 100 rows with rollback per batch on error. If a batch fails mid-way in async mode, successfully committed prior batches remain while the failed batch is rolled back.

## 2. Related Requirements
- User Story: US-CHR-010
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient capacity.
- An HR Officer user is authenticated.
- For the sync test: a file with 100 rows, where row 50 causes a database constraint violation (e.g., unique constraint forced externally after validation).
- For the async test: a file with 600 rows, where rows 201-300 (batch 3) contain deliberate errors.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Sync File | sync_100_rows.csv | 100 rows, all valid (sync test) |
| Async File | async_batch_error.csv | 600 rows; batch 3 (rows 201-300) has errors |
| Batch Size | 100 | Per NFR-4 for async |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `sync_100_rows.csv` (100 valid rows, sync mode) and click "Import". | All 100 rows are imported in a single transaction. If any fails, none are committed. (For the happy path, all 100 succeed.) |
| 2 | Verify all 100 employees exist. | 100 employees created in a single atomic operation. |
| 3 | Upload `async_batch_error.csv` (600 rows, async mode) and let background processing begin. | Processing occurs in batches of 100. |
| 4 | Wait for completion. | Batches 1-2 (rows 1-200) committed. Batch 3 (rows 201-300) rolled back due to errors. Batches 4-6 (rows 301-600) committed (valid rows). |
| 5 | Verify employee count. | ~500 employees created (batches 1,2,4,5,6). ~100 failed (batch 3). The error report includes rows 201-300. |
| 6 | Verify no partial records from batch 3 exist. | No employees from rows 201-300 exist -- the batch was rolled back atomically. |

## 6. Postconditions
- Sync: all-or-nothing transaction behavior confirmed.
- Async: per-batch commit/rollback behavior confirmed.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
