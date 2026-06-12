---
id: TC-CHR-205
user_story: US-CHR-008
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-205: Storage quota -- 80% warning and block at plan limit

## 1. Test Objective
Verify that tenant storage usage is tracked against the plan storage quota. When usage reaches 80%, a warning is displayed to the user. When the quota is fully consumed, further uploads are blocked with an appropriate error message. This validates NFR-4 and BR-6.

**DEFERRED NOTE:** Per-tenant storage quota enforcement depends on the Subscription/Plan module which is not yet built. This test case documents the expected behavior. The `Tenant.MaxEmployees` pattern suggests quotas will follow a similar design. Until the Subscription module is ready, verify any implemented hooks (e.g., storage usage tracking, quota check stubs) behave correctly.

## 2. Related Requirements
- User Story: US-CHR-008
- Non-Functional Requirements: NFR-4
- Business Rules: BR-6

## 3. Preconditions
- Tenant "acme" exists with a plan storage quota of 20 MB.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) exists.
- Current storage usage for tenant "acme" is 0 MB.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Plan Storage Quota | 20 MB | Tenant plan limit |
| File A | doc-a.pdf | 8 MB |
| File B | doc-b.pdf | 8 MB |
| File C | doc-c.pdf | 5 MB (would exceed quota) |
| 80% Threshold | 16 MB | Warning threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload "doc-a.pdf" (8 MB) to Jane Doe's record. | Upload succeeds. Tenant storage usage = 8 MB (40% of 20 MB). No warning. |
| 2 | Upload "doc-b.pdf" (8 MB) to Jane Doe's record. | Upload succeeds. Tenant storage usage = 16 MB (80% of 20 MB). |
| 3 | Verify the 80% warning is displayed. | A warning message or banner appears: "Your organization's storage usage is at 80%. Consider managing existing documents." (or similar). The warning may appear as a toast notification or a persistent banner on the Documents section. |
| 4 | Attempt to upload "doc-c.pdf" (5 MB) which would bring total to 21 MB. | Upload is **blocked**. Error message indicates the tenant has exceeded its storage quota (e.g., "Storage quota exceeded. Your plan allows 20 MB. Current usage: 16 MB. File size: 5 MB."). |
| 5 | Verify the API response for the blocked upload. | Response status is 403 Forbidden or 422 Unprocessable Entity with quota-exceeded error code. |
| 6 | Verify no document record was created for the blocked upload. | No row exists for "doc-c.pdf". |
| 7 | Verify no file was stored in object storage for the blocked upload. | Object storage has no "doc-c.pdf". |
| 8 | **[DEFERRED]** Verify the storage usage is accurately tracked in a tenant storage tracking table or field. | When implemented: a `tenant_storage_usage` record or computed field shows 16 MB for acme. |

## 6. Postconditions
- Tenant "acme" has 16 MB of document storage used.
- The blocked upload left no artifacts.
- The 80% warning was communicated to the user.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
