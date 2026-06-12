---
id: TC-CHR-ISO-032
user_story: US-CHR-008
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-032: Document storage paths and cache keys are tenant-scoped

## 1. Test Objective
Verify that document storage paths in object storage include the tenant ID as a prefix (preventing cross-tenant file access at the storage layer), and that any cache keys used for document metadata or listings are tenant-scoped (preventing cache poisoning across tenants). This validates FR-3 and NFR-2.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-3
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" (tenant_id = `acme-uuid`) has uploaded a document for employee emp-001-uuid.
- Tenant "globex" (tenant_id = `globex-uuid`) has uploaded a document for employee emp-002-uuid.
- Object storage and application cache are accessible for inspection.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Acme Document | contract.pdf | storage_key = `acme-uuid/core-hr/emp-001-uuid/2026/06/contract.pdf` |
| Globex Document | offer.pdf | storage_key = `globex-uuid/core-hr/emp-002-uuid/2026/06/offer.pdf` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload "contract.pdf" in acme context. Inspect the `storage_key` stored in the `employee_documents` table. | `storage_key` = `acme-uuid/core-hr/emp-001-uuid/2026/06/contract.pdf`. The path begins with the acme tenant ID. |
| 2 | Upload "offer.pdf" in globex context. Inspect the `storage_key`. | `storage_key` = `globex-uuid/core-hr/emp-002-uuid/2026/06/offer.pdf`. The path begins with the globex tenant ID. |
| 3 | Verify the actual object storage paths. | In object storage, the files are stored under distinct tenant-prefixed directories. There is no shared or flat namespace. |
| 4 | Attempt to generate a signed URL for `acme-uuid/core-hr/emp-001-uuid/2026/06/contract.pdf` from the globex application context. | The application refuses to generate the URL (the document record is not found in globex context due to EF filter). Even if the storage_key were known, the application's authorization check blocks access. |
| 5 | If the application uses caching for document metadata or listings, verify cache keys include tenant ID. | Cache keys follow a pattern like `tenant:{tenant-id}:employee:{employee-id}:documents` or equivalent. Acme's cache key differs from globex's. |
| 6 | Clear acme's document cache. Verify globex's cached data is unaffected. | Globex document list still returns from cache (if cached). Acme's cache is empty and requires a fresh DB query. |

## 6. Postconditions
- Storage paths are tenant-isolated at the object storage level.
- Cache keys are tenant-scoped with no cross-contamination.
- No cross-tenant file access was possible through storage or cache.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
