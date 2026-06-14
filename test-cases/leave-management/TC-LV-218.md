---
id: TC-LV-218
user_story: US-LV-011
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-218: LOP is a system leave type — auto-created at tenant setup, cannot be deleted, can be renamed (FR-1)

## 1. Test Objective
Verify FR-1: the LOP/"Unpaid Leave" leave type is a special system leave type that is auto-created during tenant setup, cannot be deleted (or hard-deactivated in a way that breaks LOP processing), but CAN be renamed.

## 2. Related Requirements
- User Story: US-LV-011
- Functional Requirements: FR-1
- Cross-ref: US-LV-001 (leave-type CRUD, system seeding "Unpaid Leave")

## 3. Preconditions
- Tenant "acme" provisioned; the default leave-type seed includes Unpaid Leave (per US-LV-001 SeedDefaultsForTenantAsync).
- HR Officer "Asha" authenticated with `LeaveType.*` permissions.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| System type | Unpaid Leave / LOP | seeded |
| New name | "Loss of Pay" | rename allowed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | List leave types for acme after provisioning | The LOP/Unpaid Leave system type is present (auto-created — see FR-1; note the onboarding seeding call site is DEFERRED per vault — the seeding service + idempotency is the verified surface). |
| 2 | Attempt to delete the LOP system type | Rejected — the system LOP type cannot be deleted (it is required for LOP processing). |
| 3 | Rename the LOP type to "Loss of Pay" | Succeeds; the type retains its system/LOP semantics under the new display name. |
| 4 | Re-run lop-summary / assign-lop after rename | LOP processing still resolves the (renamed) system type — rename does not break the integration. |

## 6. Postconditions
- The LOP system leave type is present, non-deletable, and renamable without breaking LOP processing.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
