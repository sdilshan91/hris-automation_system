---
id: TC-LV-265
user_story: US-LV-011
module: Leave Management
priority: high
type: integration
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - ISSUE-222
---

# TC-LV-265: LOP system leave type is seeded at tenant provisioning, not lazily on first assign (ISSUE-222 — US-LV-011 FR-1)

## 1. Test Objective
Verify the ISSUE-222 fix on US-LV-011 FR-1: the **Loss of Pay (LOP)** system leave type is seeded as part of the canonical default leave-type set **at tenant provisioning time**, not lazily on the first `assign-lop`/compulsory-leave request. A brand-new tenant that has never issued any LOP assignment must already list "Loss of Pay" (code `LOP`, `SystemCategory = LossOfPay`, zero annual entitlement).

## 2. Related Requirements
- User Story: US-LV-011
- Functional Requirement: FR-1 (LOP is a system leave type — auto-created, non-deletable, renamable)
- Business Rule: BR-1 (LOP has no entitlement/balance — pure deduction mechanism)
- Finding: ISSUE-222 (PR #371)

## 3. Preconditions
- A subscription plan seeded; the tenant-provisioning service runnable against the real EF context (mirrors `TenantProvisioningIntegrationTests`).
- **No** `assign-lop` / `EnsureLopTypeForTenantAsync` call is made — the LOP type must exist purely from provisioning-time seeding.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant subdomain | acme | freshly provisioned |
| LOP code | LOP | canonical system code |
| LOP name | Loss of Pay | default label (renamable) |
| AnnualEntitlement | 0 | pure deduction, no balance |
| SystemCategory | LossOfPay | discriminates the system type |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Provision a brand-new tenant (`ProvisionAsync`) without ever calling assign-lop. | Provisioning succeeds; the tenant exists. | `TenantProvisioningIntegrationTests.ProvisionTenant_SeedsLopSystemLeaveType_WithoutAnyAssign_ISSUE222` |
| 2 | Query the tenant's leave types for `SystemCategory == LossOfPay`. | Exactly one LOP leave type exists purely from provisioning-time seeding (not null). | `...ProvisionTenant_SeedsLopSystemLeaveType_WithoutAnyAssign_ISSUE222` |
| 3 | Inspect the seeded LOP type. | `Code == "LOP"`, `Name == "Loss of Pay"`, `AnnualEntitlement == 0`. | `...ProvisionTenant_SeedsLopSystemLeaveType_WithoutAnyAssign_ISSUE222` |

## 6. Postconditions
- Every newly provisioned tenant carries the LOP system leave type from setup; payroll/LOP flows never depend on a lazy first-assign to create it.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test (provisioning-time seeding vs lazy create)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real EF context):**
  - `TenantProvisioningIntegrationTests.ProvisionTenant_SeedsLopSystemLeaveType_WithoutAnyAssign_ISSUE222`
- Backing suite trait: `[Trait("TC", "TC-LV-265")]`.
