---
id: TC-LV-ISO-041
user_story: US-LV-011
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-041: LOP data in Tenant A must not be visible to, or affect, Tenant B (NFR-2)

## 1. Test Objective
Verify cross-tenant data isolation for LOP: LOP requests/entries and lop-summary data created in Tenant A are never visible to Tenant B users, and an LOP assignment in Tenant A never alters Tenant B balances or payroll data (AC-1..4, NFR-2; Test Hint §11).

## 2. Related Requirements
- User Story: US-LV-011
- Non-Functional Requirements: NFR-2
- Note: enforced by EF Core global query filters + TenantInterceptor (per docs/vault/modules/leave-management.md), not Postgres RLS.

## 3. Preconditions
- Tenant "acme" (HR Asha, employee Mark with LOP entries) and Tenant "globex" (HR Bola, employee Kofi).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme LOP | Mark, 2 LOP days | tenant A |
| globex probe | Bola / Kofi | tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Bola (globex), call `GET /api/v1/leaves/lop-summary?employeeId={Mark}` | No acme LOP data returned (empty/404); Mark's LOP entries are invisible to globex. |
| 2 | As Bola, list LOP entries / compulsory-leave records | Only globex rows appear; none of acme's LOP/compulsory rows are present. |
| 3 | Assign LOP in acme (Mark) | Kofi's (globex) balances, LOP count, and lop-summary are completely unchanged. |
| 4 | Compare totals | acme and globex LOP datasets are fully partitioned; no leakage either direction. |

## 6. Postconditions
- LOP data is strictly tenant-partitioned; Tenant A LOP activity is invisible to and inert for Tenant B.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
