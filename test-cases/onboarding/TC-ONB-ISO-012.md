---
id: TC-ONB-ISO-012
user_story: US-ONB-004
module: Onboarding / Offboarding
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-012: Tenant A cannot see Tenant B assets/issuances (cross-tenant READ block)

## 1. Test Objective
Verify AC-5 and NFR-2: asset register entries and issuance records are isolated per tenant. A user in Tenant B querying the asset register sees zero Tenant A assets; cross-tenant reads return nothing.

## 2. Related Requirements
- User Story: US-ONB-004
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-2 (EF Core global query filters; RLS deferred)
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: This platform enforces read isolation via EF Core global query filters keyed on `ITenantContext.TenantId`, not PostgreSQL RLS (RLS is a deferred extension — same family as Auth/Leave/Payroll/Admin/prior ONB stories). This test asserts the EF mechanism in force today.

## 3. Preconditions
- Tenant A (`acme`) has assets and issuance records (e.g. LAP-001 assigned to E200).
- Tenant B (`globex`) has its own distinct assets; a Tenant B user is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme asset | LAP-001 (assigned to E200) | Tenant A |
| globex user | authenticated in globex | Tenant B context |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the globex user, list the asset register | Only globex assets returned; LAP-001 and all other acme assets are absent (AC-5, NFR-2). |
| 2 | As the globex user, list/search issuance records | No acme issuance records appear; results scoped to globex. |
| 3 | As the acme user, list the asset register | Only acme assets returned; no globex assets visible (symmetry). |
| 4 | Compare counts | Each tenant sees only its own asset/issuance counts; no overlap. |

## 6. Postconditions
- Asset and issuance reads are strictly tenant-scoped; no cross-tenant visibility.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
