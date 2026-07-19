---
id: TC-PAY-018
user_story: US-PAY-004
module: Payroll
priority: high
type: integration
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - ISSUE-159
---

# TC-PAY-018: Tenant payslip footer disclaimer round-trips through save → GET (ISSUE-159 — the ToOrgProfileDto read-path gap, BR-3)

## 1. Test Objective
Verify the ISSUE-159 fix on US-PAY-004 BR-3: the tenant-configured **payslip footer disclaimer** persists and is read back through the org-profile settings API. Pre-fix the value was written and consumed by the payslip renderer, but `ToOrgProfileDto` dropped it on the read path, so a settings `GET` always returned `null` (a write/read asymmetry). The fix restores the round-trip: the update result **and** a fresh `GetSettingsAsync` both echo the saved disclaimer.

## 2. Related Requirements
- User Story: US-PAY-004
- Business Rule: BR-3 (the payslip must include a disclaimer/footer as configured by the tenant)
- Functional Requirement: FR-3 (per-tenant payslip templates — customizable footer text)
- Finding: ISSUE-159 (PR #371)

## 3. Preconditions
- A seeded tenant.
- Uses the tenant-settings service through the real EF context (mirrors `TenantSettingsServiceTests`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| PayslipFooterDisclaimer | "Confidential — payroll use only." | saved value to round-trip |
| Org profile Name | "Acme" | required field on the update |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | `UpdateOrgProfileAsync(... PayslipFooterDisclaimer: "Confidential — payroll use only.")`. | Success; the returned DTO echoes the saved disclaimer. | `TenantSettingsServiceTests.UpdateOrgProfile_PayslipFooterDisclaimer_RoundTripsThroughGet` |
| 2 | `GetSettingsAsync()` immediately after. | `OrgProfile.PayslipFooterDisclaimer` equals the saved value (was `null` before the fix — the `ToOrgProfileDto` read-path gap). | `TenantSettingsServiceTests.UpdateOrgProfile_PayslipFooterDisclaimer_RoundTripsThroughGet` |

## 6. Postconditions
- The payslip footer disclaimer is durable and visible on both the write result and a fresh settings read; the payslip renderer and the settings UI now agree.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test (write/read symmetry)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real EF context):**
  - `TenantSettingsServiceTests.UpdateOrgProfile_PayslipFooterDisclaimer_RoundTripsThroughGet`
- Backing suite trait: `[Trait("TC", "TC-PAY-018")]`.
