---
id: TC-RPT-ISO-016
user_story: US-RPT-004
module: Reports & Analytics
priority: high
type: security
status: blocked
exec_note: "2026-06-30: export storage-path/retention-purge/concurrency-cap are infra/job behaviors not exercisable via a single curl probe in the iso fixture (no exports seeded; retention job + Hangfire concurrency cap need time/queue state). Download-path isolation verified (see ISO-013/014). Keep blocked: needs export+job fixture."
created: 2026-06-17
---

# TC-RPT-ISO-016: Export storage path + retention purge + concurrency cap are tenant-isolated (FR-6, BR-3, FR-10, NFR-7)

## 1. Test Objective
Verify the export lifecycle infrastructure is tenant-partitioned: (a) every file is written under the
tenant-isolated path `{tenantId}/exports/{reportType}/{yyyy}/{mm}/{filename}` (FR-6), (b) the BR-3
7-day cleanup job purges only the owning tenant's expired files and never another tenant's, and (c) the
FR-10 concurrency cap is counted per-(tenant,user) so Tenant B traffic cannot consume Tenant A's slots.
PostgreSQL RLS (NFR-7) is documented as deferred defense-in-depth. Validates FR-6, BR-3, FR-10, NFR-7.

## 2. Related Requirements
- User Story: US-RPT-004
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6 (tenant-isolated path), FR-10 (per-user cap)
- Business Rules: BR-3 (7-day purge)
- Non-Functional: NFR-7 (RLS -- deferred)

> CONDITIONAL / DEFERRED INFRA: NFR-7 names PostgreSQL RLS as a tenant-isolation layer; this platform
> isolates via EF global query filters + TenantInterceptor + ITenantContext, NOT RLS (RLS deferred,
> consistent with prior modules). Assert the EF/path/job-context mechanism in force today; the
> raw-SQL-without-current-tenant -> zero-rows RLS expectation is CONDITIONAL.

## 3. Preconditions
- Tenant A and Tenant B active. Each has expired (8-day) and fresh export files.
- `hrA` and `hrB` authenticated with `Reports.Export`; Hangfire cleanup job configured.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| A path prefix | {tenantA}/exports/... | FR-6 |
| B path prefix | {tenantB}/exports/... | FR-6 |
| A expired file | EXP-A-OLD (8d) | purge candidate |
| B expired file | EXP-B-OLD (8d) | purge candidate |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Generate exports in both tenants; inspect stored paths | A under `{tenantA}/exports/{type}/{yyyy}/{mm}/...`, B under `{tenantB}/...` -- no shared/unprefixed path (FR-6) |
| 2 | Run the BR-3 cleanup for Tenant A's context | EXP-A-OLD purged; EXP-B-OLD untouched (cross-tenant files never purged by A's job) |
| 3 | Run cleanup for Tenant B's context | EXP-B-OLD purged; A's remaining files untouched |
| 4 | As `hrA`, occupy all 3 concurrency slots (FR-10) | A's 4th is capped (429/queued) |
| 5 | Simultaneously, as `hrB`, trigger an export | `hrB` is NOT blocked by A's slots -- cap is per (tenant,user), not global |
| 6 | (CONDITIONAL -- NFR-7) Run a raw SQL read of the exports table without tenant context | If RLS wired: zero rows. If not: assert EF query filter constrains app reads; flag RLS pending |

## 6. Postconditions
- Files tenant-pathed; purge per-tenant; concurrency cap per (tenant,user); RLS step conditional.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
