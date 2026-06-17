---
id: TC-ONB-ISO-014
user_story: US-ONB-004
module: Onboarding / Offboarding
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-014: EF query filter blocks reads; asset/issuance writes tenant-stamped; uniqueness scoped per tenant (RLS deferred)

## 1. Test Objective
Verify AC-5, FR-7 and NFR-2/NFR-5: the EF Core global query filter blocks cross-tenant reads of assets/issuances; the `TenantInterceptor` stamps tenant_id on every new asset, issuance, and acknowledgment record at SaveChanges; the atomic issuance transaction commits all rows under one tenant; and asset_tag/serial uniqueness is enforced per tenant (so the same tag can exist in two tenants).

## 2. Related Requirements
- User Story: US-ONB-004
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7 (tenant_id from session)
- Non-Functional Requirements: NFR-2 (isolation), NFR-5 (atomic transaction)
- Business Rules: BR-3 (uniqueness per tenant)

> PLATFORM NOTE: Write isolation = `TenantInterceptor` auto-stamping `TenantId` on new `BaseEntity` rows; read isolation = global query filter on `TenantId`. PostgreSQL RLS ("raw SQL without app.current_tenant_id returns zero rows") is CONDITIONAL/deferred — step 4 documents it as future hardening, not asserted against today's build.

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) each authenticated in their own context.
- Each tenant can hold an asset with the same asset_tag/serial (e.g. LAP-DUP / SN-DUP).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| asset_tag in both tenants | LAP-DUP | allowed once per tenant (BR-3) |
| serial in both tenants | SN-DUP | allowed once per tenant |
| 3-asset bulk issuance | one transaction | all rows stamped same tenant (NFR-5) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme, create LAP-DUP/SN-DUP and issue assets (incl. a 3-asset bulk) | All new asset/issuance/acknowledgment rows stamped tenant_id=T-acme by the interceptor; the 3-asset batch commits in one transaction (FR-7, NFR-5). |
| 2 | As globex, create LAP-DUP/SN-DUP | Accepted — uniqueness scoped per tenant; globex rows stamped tenant_id=T-globex (BR-3, FR-7). |
| 3 | As globex, query assets | The EF global query filter returns only globex rows; acme's LAP-DUP is invisible (NFR-2). |
| 4 | (CONDITIONAL) Run raw SQL without an app tenant variable | DEFERRED: documents the future Postgres-RLS expectation (zero rows without `app.current_tenant_id`); today isolation is the EF query filter + interceptor — flag RLS to the caller as future hardening. |
| 5 | Inspect persisted rows | Every asset/issuance/acknowledgment carries a non-null tenant_id matching the session tenant; no row stamped with a foreign tenant. |

## 6. Postconditions
- All writes tenant-stamped; reads tenant-filtered; uniqueness per tenant; RLS documented as deferred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
