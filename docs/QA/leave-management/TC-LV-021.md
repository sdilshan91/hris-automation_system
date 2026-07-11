---
id: TC-LV-021
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: automated
exec_note: "2026-07-05 ISSUE-029 fixed at the PROVISIONING seed path: TenantProvisioningService now seeds the canonical FR-4 default set via LeaveTypeService.GetDefaultLeaveTypes (was a partial inline Annual/Sick/Casual only). Bound to automated regression -> HRM.Tests.Integration.TenantProvisioningIntegrationTests.ProvisionTenant_SeedsCanonicalDefaults_ISSUE029 (+ Provision_SeedsDefaultLeaveTypesAndShift). The onboarding-WIZARD UI Step-4 (modify/delete/save flow, steps 4-7 below) remains deferred/unbuilt."
created: 2026-06-13
---

# TC-LV-021: New tenant gets default leave types on provisioning

## 1. Test Objective
Verify that when a new tenant is provisioned, the canonical FR-4 default leave types are seeded that the tenant admin can later customize. The provisioning-service seed path is implemented and automated (ISSUE-029); the onboarding-wizard UI Step 4 modify/delete/save flow is still deferred.

> **Regression automation (ISSUE-029):** `HRM.Tests.Integration.TenantProvisioningIntegrationTests.ProvisionTenant_SeedsCanonicalDefaults_ISSUE029`. Asserts the seeded set contains all seven canonical FR-4 defaults (not the partial three), with Maternity gender=Female and Unpaid entitlement=0. Entitlements below reflect the implemented `LeaveTypeService.GetDefaultLeaveTypes` values.

## 2. Related Requirements
- User Story: US-LV-001
- Functional Requirements: FR-4
- Assumptions: Section 10

## 3. Preconditions
- Tenant provisioning and onboarding wizard are functional (dependency: US-TENANT-*).
- Step 4 of onboarding wizard is accessible.

## 4. Test Data
| Default Leave Type | Expected Entitlement | Notes |
|-------------------|---------------------|-------|
| Annual Leave | 14 | Customizable (code AL) |
| Sick Leave | 7 | Customizable; docs required > 2 days (code SL) |
| Casual Leave | 7 | Max 3 consecutive days (code CL) |
| Maternity Leave | 84 | Gender: female (code MAT) |
| Paternity Leave | 5 | Gender: male (code PAT) |
| Bereavement Leave | 3 | Customizable (code BL) |
| Unpaid Leave | 0 | No entitlement; negative balance up to 30 (code UL) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Provision a new tenant "newco" via onboarding wizard | Tenant created successfully. (DEFERRED) |
| 2 | Complete onboarding wizard up to Step 4 | Step 4 loads with pre-seeded default leave types listed. (DEFERRED) |
| 3 | Verify default leave types are listed | All default types (Annual, Sick, Casual, Maternity, Paternity, Bereavement, Unpaid) are present with suggested entitlements. (DEFERRED) |
| 4 | Modify "Annual Leave" entitlement from 20 to 25 days | Change accepted. (DEFERRED) |
| 5 | Delete "Bereavement Leave" from the defaults | Type removed from the list. (DEFERRED) |
| 6 | Complete Step 4 | All remaining (and modified) leave types are saved as the tenant's leave types. (DEFERRED) |
| 7 | Navigate to Leave Types configuration page | Shows the customized set: Annual (25 days), Sick (10), Casual (7), Maternity (90), Paternity (14), Unpaid (0). Bereavement not present. (DEFERRED) |

## 6. Postconditions
- New tenant has customized set of leave types based on defaults.
- All seeded types are scoped to the new tenant's `tenant_id`.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
