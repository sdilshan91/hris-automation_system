---
id: TC-ONB-ISO-011
user_story: US-ONB-003
module: Onboarding / Offboarding
priority: high
type: security
status: pass
created: 2026-06-17
---

# TC-ONB-ISO-011: Onboarding progress cache + document storage keys are tenant-scoped

## 1. Test Objective
Verify NFR-2: any cached checklist/progress lookup and the document object-storage key are tenant-scoped, so one tenant can never read another tenant's cached progress or stored files. The storage key shape `{tenantId}/onboarding/{employeeId}/{taskId}/{filename}` places tenant first, guaranteeing per-tenant isolation at the storage layer.

## 2. Related Requirements
- User Story: US-ONB-003
- Acceptance Criteria: AC-4
- Functional Requirements: FR-4
- Non-Functional Requirements: NFR-2, NFR-6
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: If no distributed cache is wired for progress yet, assert the equivalent always-tenant-filtered property and flag the target cache-key shape `onboarding:progress:{tenant_id}:{employee_id}` to the caller. Document storage path is the AC-4 contract.

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) each have employees with onboarding progress and uploaded documents.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| progress cache key (target) | onboarding:progress:{tenant_id}:{employee_id} | tenant-scoped |
| storage key (acme) | T-acme/onboarding/E123/TK77/id-proof.pdf | tenant first segment |
| storage key (globex) | T-globex/onboarding/.../... | distinct namespace |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load acme employee progress, then globex employee progress | Each resolves from a distinct tenant-scoped cache entry; no key collision (NFR-2). |
| 2 | Inspect the document storage keys | Both begin with their respective `{tenantId}` segment; acme and globex files live in separate namespaces (AC-4). |
| 3 | From the acme context, request a globex document by its full storage key | Denied/not found; tenant-prefixed key + access checks prevent cross-tenant retrieval. |
| 4 | Verify cache invalidation on completion | Completing an acme task invalidates/updates only the acme progress entry; globex entries untouched. |
| 5 | (CONDITIONAL) If no progress cache is wired | Assert progress is always recomputed under the tenant query filter (equivalent property); flag the target key shape to the caller. |

## 6. Postconditions
- Progress caches and document storage keys are tenant-scoped; no cross-tenant cache or file access is possible.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
