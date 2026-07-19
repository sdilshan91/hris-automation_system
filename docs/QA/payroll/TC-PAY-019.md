---
id: TC-PAY-019
user_story: US-PAY-011
module: Payroll
priority: high
type: integration
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - ISSUE-229
---

# TC-PAY-019: Bulk payslip email uses the tenant-configured sender "From" address; invalid address rejected and not persisted (ISSUE-229 — US-PAY-011 BR-4)

## 1. Test Objective
Verify the ISSUE-229 fix on US-PAY-011 BR-4: the bulk payslip distribution job sends each email **From** the tenant's configured payroll sender when one is set, and falls back to the system default (`null` at the sender layer → `SmtpEmailSender` applies the default) when the tenant has none. The tenant sender address is validated on save — an invalid address is **rejected (400) and not persisted** — and it round-trips through the org-profile settings GET (the ISSUE-159 write/read-symmetry lesson).

## 2. Related Requirements
- User Story: US-PAY-011
- Business Rule: BR-4 (payslip emails are sent from the tenant-configured sender; else the system default)
- Finding: ISSUE-229 (PR #371)

## 3. Preconditions
- A finalized payroll run with generated payslip PDFs; a recording email sender + fake file storage (mirrors `PayslipDistributionTests`).
- A seeded tenant with/without `PayrollFromEmail`; tenant-settings service over the real EF context (mirrors `TenantSettingsServiceTests`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant sender (set) | payroll@acme.test | used as the email From |
| Tenant sender (unset) | null | → system default at SmtpEmailSender |
| Invalid sender | not-an-email | rejected 400, not persisted |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Run the distribution job for a tenant with `PayrollFromEmail = payroll@acme.test`. | The single sent email's `FromAddress == payroll@acme.test`. | `PayslipDistributionTests.Runner_UsesTenantConfiguredFromAddress_WhenSet` |
| 2 | Run the job for a tenant with no configured sender (`null`). | The sent email's `FromAddress` is `null` → the SMTP layer applies the system default. | `PayslipDistributionTests.Runner_FromAddressIsNull_WhenTenantHasNoConfiguredSender` |
| 3 | Save a valid `PayrollFromEmail` via org-profile update, then GET settings. | Update echoes `payroll@acme.test`; a fresh GET (cross-context read) returns the same value. | `TenantSettingsServiceTests.UpdateOrgProfile_PayrollFromEmail_RoundTripsThroughGet` |
| 4 | Save an invalid `PayrollFromEmail` (`not-an-email`). | Failure with status **400**; the tenant's `PayrollFromEmail` remains `null` (nothing persisted). | `TenantSettingsServiceTests.UpdateOrgProfile_InvalidPayrollFromEmail_Is400_AndNotPersisted` |

## 6. Postconditions
- Tenant payslip sender identity is honoured on send, validated on save, and durably readable; an invalid address never reaches the DB.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test (unset → system default)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite):**
  - `PayslipDistributionTests.Runner_UsesTenantConfiguredFromAddress_WhenSet`
  - `PayslipDistributionTests.Runner_FromAddressIsNull_WhenTenantHasNoConfiguredSender`
  - `TenantSettingsServiceTests.UpdateOrgProfile_PayrollFromEmail_RoundTripsThroughGet`
  - `TenantSettingsServiceTests.UpdateOrgProfile_InvalidPayrollFromEmail_Is400_AndNotPersisted`
- Backing suite trait: `[Trait("TC", "TC-PAY-019")]`.
