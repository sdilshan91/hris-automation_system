---
id: TC-CHR-334
user_story: US-CHR-001
module: Core HR
priority: high
type: security
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - DF-30
---

# TC-CHR-334: LocalFileStorage rejects any relativePath that escapes the tenant base directory (path-traversal defense-in-depth) — DF-30

## 1. Test Objective
Verify the DF-30 guard on `LocalFileStorage`: although `relativePath` is server-derived today, the storage layer must refuse any path that resolves **outside** the tenant's base directory. Legitimate tenant-scoped paths write and read back; `../`-style traversal on upload/read/delete throws; and a sibling-tenant directory whose name is a **prefix** of the tenant's is not treated as inside (the trailing-separator boundary check).

## 2. Related Requirements
- User Story: US-CHR-001 (employee file/photo storage) — infra guard
- Finding: DF-30 (deferred-followups register)
- Security concern: path traversal / cross-tenant file access

## 3. Preconditions
- `LocalFileStorage` rooted at a fresh temp base directory (mirrors `LocalFileStorageTests`).
- A tenant GUID scoping the storage.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Legit path | branding/logo.png | writes to `/{tenant}/branding/logo.png` |
| Traversal paths | `../../etc/passwd`, `../other-tenant/secret.png`, `branding/../../escape.txt` | must throw |
| Sibling-prefix | `..\{tenant:N}-sibling/x` | must throw (prefix ≠ inside) |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Upload then read a legitimate tenant-scoped path. | URL is `/{tenant}/branding/logo.png`; the bytes read back match what was written. | `LocalFileStorageTests.UploadAsync_LegitimatePath_WritesAndReadsBack` |
| 2 | Upload with each of 3 traversal relativePaths. | Each throws `InvalidOperationException` (resolves outside the base). | `LocalFileStorageTests.UploadAsync_TraversalRelativePath_IsRejected` (Theory ×3) |
| 3 | Open-read with a traversal path. | Throws `InvalidOperationException`. | `LocalFileStorageTests.OpenReadAsync_TraversalRelativePath_IsRejected` |
| 4 | Delete with a traversal path. | Throws `InvalidOperationException`. | `LocalFileStorageTests.DeleteAsync_TraversalRelativePath_IsRejected` |
| 5 | Upload targeting a sibling dir whose name is a prefix of the tenant's. | Throws — the trailing-separator check blocks `/base/tenantX` vs `/base/tenantXY`. | `LocalFileStorageTests.UploadAsync_SiblingTenantPrefix_IsNotTreatedAsInside` |

## 6. Postconditions
- File storage is confined to the tenant base directory; no traversal or sibling-prefix trick escapes it.

## 7. Test Category Tags
- [x] Happy path (legit path)
- [x] Negative test (traversal rejected)
- [x] Boundary test (sibling-prefix)
- [x] Security test (path traversal / cross-tenant file access)
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite):**
  - `LocalFileStorageTests.UploadAsync_LegitimatePath_WritesAndReadsBack`
  - `LocalFileStorageTests.UploadAsync_TraversalRelativePath_IsRejected` (`[Theory]`, 3 inline cases)
  - `LocalFileStorageTests.OpenReadAsync_TraversalRelativePath_IsRejected`
  - `LocalFileStorageTests.DeleteAsync_TraversalRelativePath_IsRejected`
  - `LocalFileStorageTests.UploadAsync_SiblingTenantPrefix_IsNotTreatedAsInside`
- Backing suite trait: `[Trait("TC", "TC-CHR-334")]`.
