---
id: TC-LV-021
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-021: New tenant gets default leave types on provisioning (DEFERRED)

## 1. Test Objective
Verify that when a new tenant is provisioned and completes onboarding wizard Step 4 (leave types & holidays), default leave types are seeded that the tenant admin can customize. **Status: DEFERRED** -- onboarding wizard hook is not yet implemented.

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
| Annual Leave | 20 | Customizable |
| Sick Leave | 10 | Customizable |
| Casual Leave | 7 | Customizable |
| Maternity Leave | 90 | Gender: female |
| Paternity Leave | 14 | Gender: male |
| Bereavement Leave | 3 | Customizable |
| Unpaid Leave | 0 | No entitlement |

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
