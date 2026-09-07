---
id: US-CHR-014
module: Core HR
priority: Must Have
persona: HR Manager / Payroll Manager / Employee
status: draft
created: 2026-09-07
sprint: backlog
acceptance_criteria_count: 8
findings: ENH-018, ISSUE-523
---

# US-CHR-014: Capture Employee Bank Details for Salary Disbursement

> **Traceability:** this story is the **expensive half** of [[ENH-018]]. ENH-018 as filed asks only for a QA
> seed fixture; the queue's 2026-09-07 sizing records that the filed remedy is the *smaller* half and
> **must not close the finding** (`docs/QA/plans/GAP-CLOSURE-QUEUE.md:487,512`). This story is the other
> half: the capture path that does not exist.

## 1. Description
**As an** HR Manager (capturing on behalf of an employee) or an **Employee** (maintaining my own payment
details),
**I want to** record and update the bank account that my salary is paid into, through the product,
**So that** payroll can actually disburse pay — today the Bank Advice report (US-RPT-003 AC-4) renders a
column that is empty for every employee in production, because nothing in the system can write it.

## 2. Background / Problem Statement
Three columns exist on `Employee` and have since US-RPT-003 — `BankName`, `BankBranchCode`,
`BankAccountNumber` (`src/backend/HRM.Domain/Entities/Employee.cs:181-197`). Everything *downstream* of them
is built: last-4 masking, audit redaction, and the `Payroll.ViewSensitive`-gated reveal path.

**All of it is unreachable in production, because there is no write path anywhere.** The entity comment
itself asserts the fields are "populated elsewhere" — there is no elsewhere. The only code that ever sets
them is a test fixture. The consequence is a whole feature (US-RPT-003 AC-4 bank advice, US-RPT-004 masked
export) that cannot be exercised by a real tenant, and a set of security controls whose correctness has
never been observed against real data.

This is the shape the queue calls a **false green**: seeding the fixture would make the masking testable
while leaving the product incapable of capturing a bank account. The seed must not be mistaken for this
story.

## 3. Preconditions
- **[[ISSUE-523]] is settled first — this is a hard precondition, not a dependency.** `bank_account_number`
  is plaintext while its sibling `national_id` on the same entity is encrypted. The encryption decision must
  land **before** capture ships, because the no-op back-fill window closes on the first successful write:
  once real account numbers exist, enabling encryption stops being a no-op migration and becomes a data
  migration over live PII. **This story does not re-specify ISSUE-523**; it must not ship ahead of it.
- **The unmasked-export exposure filed by the orchestrator on 2026-09-07 (HIGH) is dispositioned.** HR
  Officer holds `Payroll.Export` but is deliberately denied `Payroll.ViewSensitive`
  (`PermissionCatalog.cs:746-750`). That exposure is latent today only because the columns are empty —
  **this story is what makes it live.** Capture must not ship while the export path can emit unmasked,
  unaudited account numbers.
- The employee record exists (US-CHR-001) and the Core HR module is enabled for the tenant.
- The tenant has at least one country resolvable for the employee (drives IFSC-vs-SWIFT validation, AC-3).

## 4. Acceptance Criteria (IEEE 830 §3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A user holding the new bank-details write permission is on an employee's profile | They submit bank name, branch/routing code and account number | The three `Employee` columns are persisted for that employee, and a subsequent read returns them **masked to last-4 by default** (`AccountMasking.MaskLast4`), never in full |
| AC-2 | A user holds `Employee.Edit` **or** `Payroll.Configure` but NOT the new bank-details write permission | They submit a bank-details update | The request is rejected **403**, and the employee's stored bank details are unchanged. (HR Officer holds both of those permissions today — neither may be sufficient on its own) |
| AC-3 | An employee's resolved country uses IFSC (e.g. India) | A branch code is submitted | The value is accepted only if it matches the IFSC format; a SWIFT/BIC-shaped value is rejected with a field-level error naming the expected format. Symmetrically, for a SWIFT/BIC country an IFSC-shaped value is rejected |
| AC-4 | Bank details are submitted with an account number containing spaces, hyphens or a non-alphanumeric character | The request is validated | The value is normalised (whitespace/hyphens stripped) or rejected per BR-4 — never stored raw and never silently truncated |
| AC-5 | A bank-details write succeeds | The write completes | An audit row is written for that employee recording **which fields changed**, by whom and when, with the account number stored **masked (last-4) in both the before and after snapshot** — the plaintext never lands in the audit log |
| AC-6 | A caller holds `Payroll.ViewSensitive` | They invoke the existing reveal endpoint for that employee | The full account number is returned **and** a PII-read audit event is recorded (tech doc §24.2 — "PII reads of sensitive fields (bank account…)") |
| AC-7 | An HR user in Tenant A is authenticated | They attempt to read or write bank details for an employee id belonging to Tenant B | The request fails (404/403) and **no** Tenant B row is read or written; the EF global query filter and the RLS `tenant_isolation` policy on `employees` both apply |
| AC-8 | An employee has no bank details captured | The Bank Advice report (US-RPT-003 AC-4) is generated | The employee is reported as **missing bank details** as an explicit, actionable row — not silently omitted from the disbursement file and not emitted with a blank account number |

## 5. Functional Requirements (IEEE 830 §3.2)
- FR-1: The system SHALL expose a bank-details **write** endpoint accepting `bankName`, `bankBranchCode` and
  `bankAccountNumber`, persisting to the three existing `Employee` columns. No new table and no new columns
  are required.
- FR-2: The system SHALL introduce a **new, dedicated permission** for bank-details write (see §11 Open
  Question OQ-1 for the name). It SHALL NOT reuse `Employee.Edit` or `Payroll.Configure` — see BR-1.
- FR-3: The new permission **requires no migration**. Built-in role grants are reconciled at startup by
  `DbInitializer.ReconcileBuiltInRolePermissionsAsync` (`DbInitializer.cs:854`), which only ever ADDS.
- FR-4: The frontend permission catalogue
  (`src/frontend/src/app/features/admin/roles/models/permission-catalog.ts`) SHALL be updated with the new
  permission. This is **not optional**: `PERMISSION_CATALOG` is what the role editor renders as assignable
  checkboxes, so a backend-only permission is ungrantable through the product, and a mismatched spelling
  fails `FrontendPermissionLiteralTests`.
- FR-5: The system SHALL validate the branch/routing code by **country format** — IFSC vs SWIFT/BIC — using
  the employee's resolved country, following the existing FluentValidation convention for employee data.
- FR-6: The system SHALL write its **own audit rows** for bank-detail changes. `Employee` is marked
  `IAuditExempt` (`Employee.cs:12`), so the audit interceptor will **not** pick these writes up
  automatically. Follow the `NationalId` precedent at `EmployeeService.cs:583-586`, which masks to last-4
  *into* the before/after snapshot.
- FR-7: The system SHALL return bank details **masked by default** on every read surface, with the full
  value available only through the existing `Payroll.ViewSensitive`-gated, audited reveal path.
- FR-8: The frontend SHALL provide a bank-details capture/edit section on the employee profile, visible and
  editable only to holders of the new permission, and displaying the masked value with a reveal affordance
  for `Payroll.ViewSensitive` holders.
- FR-9: The Bank Advice report SHALL surface employees with missing bank details as an explicit exception
  list (AC-8).

## 6. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: **Tenant isolation** — bank details live on the already tenant-scoped `employees` row; every read
  and write SHALL be constrained by the EF Core global query filter and the `tenant_isolation` RLS policy.
  No bank-details read may use raw SQL or `IgnoreQueryFilters()`.
- NFR-2: **PII handling** — the plaintext account number SHALL never appear in logs, audit snapshots, error
  messages, or API responses outside the audited reveal path (tech doc §3090: "Never log: … full bank
  account numbers").
- NFR-3: **Separation of duties** — the ability to *write* a bank account and the ability to *read it
  unmasked* are distinct capabilities and SHALL remain separately grantable. Changing where salary is paid
  is a fraud-relevant action.
- NFR-4: The capture form SHALL meet WCAG 2.1 AA (labelled inputs, field-level error association, keyboard
  navigable).

## 7. Business Rules
- BR-1: Bank-details write is gated by a **dedicated permission**, not by `Employee.Edit` and not by
  `Payroll.Configure`. Both are semantically wrong and both are **already held by HR Officer**
  (`PermissionCatalog.cs:735-750`) — the persona the catalogue *deliberately* denies unmasked bank PII.
  Reusing either silently grants salary-destination control to a role the permission model was designed to
  withhold it from.
- BR-2: Bank details are masked to last-4 by default on every surface; full disclosure is `Payroll.ViewSensitive`-gated and audited.
- BR-3: A branch/routing code is validated against the format implied by the employee's country (IFSC or SWIFT/BIC); an unrecognised country is handled per OQ-2 rather than silently accepting any string.
- BR-4: An account number is stored normalised (no whitespace or separators) and is never silently truncated to fit.
- BR-5: Every bank-detail change is audited with field-level before/after, with the account number masked in the snapshot.
- BR-6: An employee with no bank details is an **exception on the bank advice run**, never a silent omission — a missing account is a payment failure, not a formatting detail.

## 8. Data Requirements
**employees (existing columns — no schema change):**
| Field | Type | Notes |
|-------|------|-------|
| bank_name | text, nullable | Existing (`Employee.cs:188`) |
| bank_branch_code | text, nullable | Existing (`Employee.cs:191`) — IFSC or SWIFT/BIC |
| bank_account_number | text, nullable | Existing (`Employee.cs:197`). **PII.** Plaintext today — see [[ISSUE-523]] |

**Input:** `bankName`, `bankBranchCode`, `bankAccountNumber` + employee id (path).
**Output:** masked bank-details DTO (last-4 account number); full value only via the reveal path.

## 9. UI/UX Notes
- A "Bank Details" section on the employee profile (US-CHR-002 surface), collapsed by default, hidden entirely for users without the new permission.
- Masked display as `••••1234`; a "Reveal" control shown only to `Payroll.ViewSensitive` holders, with a visible notice that revealing is recorded.
- Field-level validation messages that name the expected format ("Expected an 11-character SWIFT/BIC code"), not a generic "invalid".
- No bank-details FE surface exists today; this is net-new UI.

## 10. Dependencies
- **US-CHR-001 / US-CHR-002** — own the employee record and the profile edit surface this section lives on.
- **US-RPT-003 (AC-4 / FR-6)** — the Bank Advice report and the masking/reveal contract this story feeds; the columns were added *for* it.
- **US-RPT-004 (BR-2)** — masked export path.
- **[[ISSUE-523]]** — encryption of `bank_account_number`. **Precondition, handled separately, not re-specified here.**
- **US-PLT-002** — the `tenant_isolation` RLS policy applied to `employees`.

## 11. Open Questions — MUST be answered before this story is `ready`
> Per the BA contract this story stays `draft`. Each question below changes acceptance criteria, not just
> implementation detail.
- **OQ-1 (permission name and grant set).** What is the new permission called, and which built-in roles get
  it by default? *Recommendation (confidence 70%):* `Payroll.ManageBankDetails`, scoped **like**
  `Payroll.ViewSensitive` — Tenant Owner / Tenant Admin / HR Manager, **not** HR Officer — because the
  catalogue already uses exactly that reasoning for `ViewCompensation` (`PermissionCatalog.cs:220-227`).
  Naming it under `Payroll.*` rather than `Employee.*` follows the data's *sensitivity* class rather than
  its storage location. This needs a product/security sign-off, not a BA guess.
- **OQ-2 (self-service).** May an **Employee** maintain their own bank details, or is this HR-only? The
  persona line above assumes self-service is in scope; if it is, an `Employee.Edit.Own`-style self-scope arm
  and almost certainly a change-notification/approval step are required (changing your own salary
  destination is a classic fraud vector). If it is HR-only, drop the Employee persona and AC coverage
  shrinks. **Unanswered — this is the single largest scope fork in the story.**
- **OQ-3 (country→format mapping source).** Where does the IFSC-vs-SWIFT decision read the country from —
  `Location`, tenant default, or an explicit field on the bank details — and what happens for a country with
  no configured format? *Recommendation:* reuse the tax-country resolution chain already used by F&F
  (US-PAY-013 AC-5: Location → tenant default → single-country fallback), and for an unmapped country accept
  a permissive validated string rather than blocking payroll.

## 12. Assumptions & Constraints
- No schema change is needed for capture itself; the columns already exist. Any migration in this story
  belongs to [[ISSUE-523]] (encryption), not to capture.
- Only free/open-source libraries; PostgreSQL with EF global query filters + RLS for isolation.
- The masking helper, the reveal endpoint and the audit redaction are **already built** and are to be reused,
  not rewritten. Verify before building — do not re-implement what is already there.

## 13. Test Hints
- **The core regression:** capture a bank account through the API, then generate the Bank Advice report and assert the masked value appears. This end-to-end path is impossible today; it is the definition of done.
- Permission: attempt the write as a principal holding only `Employee.Edit`, then only `Payroll.Configure` → expect 403 on both. Then with the new permission → 200.
- Format validation: IFSC-country employee with a SWIFT-shaped code → 422; SWIFT-country employee with an IFSC-shaped code → 422; correct pairs → 200.
- Audit: perform a write, then read the audit row and assert the account number is **masked** in both before and after snapshots (mirror the `NationalId` assertion style).
- Reveal: call the reveal endpoint with and without `Payroll.ViewSensitive`; assert 200-with-audit-row and 403 respectively.
- Tenant isolation: authenticate in Tenant A, target a Tenant B employee id for both read and write; assert no cross-tenant row is touched.
- Export exposure: generate the export as an HR Officer (holds `Payroll.Export`, lacks `Payroll.ViewSensitive`) and assert the account numbers are **masked** — this is the arm that turns the latent HIGH into a live one.
- Missing details: run the bank advice for a tenant where some employees have no bank record; assert they appear as exceptions, not silently dropped.
