---
id: TC-ONB-ISO-015
user_story: US-ONB-004
module: Onboarding / Offboarding
priority: high
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-015: Asset acknowledgment storage keys + any asset lookup cache are tenant-scoped

## 1. Test Objective
Verify NFR-2 and NFR-4: acknowledgment documents are stored under a tenant-first object-storage key, so one tenant can never retrieve another tenant's acknowledgment file; and any cached asset/register lookup is tenant-scoped (no cross-tenant cache collision).

## 2. Related Requirements
- User Story: US-ONB-004
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-2 (isolation), NFR-4 (acknowledgment uploads)
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: The storage key shape `{tenantId}/onboarding/{employeeId}/assets/{assetId}/{filename}` places tenant first, guaranteeing per-tenant isolation at the storage layer (mirrors TC-ONB-ISO-011). If no distributed cache is wired for asset lookups yet, assert the equivalent always-tenant-filtered property and flag the target cache-key shape `onboarding:assets:{tenant_id}:{employee_id}` to the caller.

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) each have issuance records with acknowledgment documents.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| storage key (acme) | T-acme/onboarding/E200/assets/ASSET-500/receipt.pdf | tenant first segment |
| storage key (globex) | T-globex/onboarding/.../... | distinct namespace |
| asset cache key (target) | onboarding:assets:{tenant_id}:{employee_id} | tenant-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Inspect acme and globex acknowledgment storage keys | Both begin with their respective `{tenantId}` segment; the two tenants' files live in separate namespaces (NFR-4). |
| 2 | From the acme context, request a globex acknowledgment by its full storage key | Denied/not found; tenant-prefixed key + access checks prevent cross-tenant retrieval (NFR-2). |
| 3 | Load acme's assets/me, then globex's assets/me | Each resolves from a distinct tenant-scoped entry; no key collision; no cross-tenant data returned. |
| 4 | (CONDITIONAL) If no asset lookup cache is wired | Assert asset lists are always recomputed under the tenant query filter (equivalent property); flag the target key shape to the caller. |

## 6. Postconditions
- Acknowledgment storage keys and any asset lookup cache are tenant-scoped; no cross-tenant file or cache access is possible.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
