---
id: US-PAY-006
module: Payroll
priority: Must Have
persona: Tenant Admin / HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PAY-006: Statutory Deductions Configuration (Tax, Social Security)

## 1. Description
**As a** Tenant Admin or HR Officer,
**I want to** configure statutory deduction rules (income tax slabs, social security contributions such as EPF, ETF, and other country-specific deductions) for my tenant,
**So that** payroll calculations automatically apply the correct statutory deductions in compliance with local regulations.

## 2. Preconditions
- Tenant is provisioned and Payroll module is enabled.
- User has Tenant Admin role or `Payroll.*.All` permission.
- Salary components of type "Statutory" exist (US-PAY-001).
- The tenant's country/jurisdiction is configured in tenant settings.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Tenant Admin is on the Statutory Configuration page | Admin configures income tax slabs (e.g., 0-250K at 0%, 250K-500K at 5%, 500K-1M at 20%, 1M+ at 30%) with an effective date | The tax slabs are saved and will be used for tax calculations in payroll runs on or after the effective date |
| AC-2 | Admin configures EPF rules | Admin sets employee contribution rate (12%), employer contribution rate (12%), and wage ceiling (15,000) | EPF deductions are calculated correctly during payroll: employee EPF = min(basic, ceiling) * 12%, employer EPF = min(basic, ceiling) * 12% |
| AC-3 | Statutory rules exist for fiscal year 2025-2026 | Admin creates new rules for fiscal year 2026-2027 with different tax slabs | Both versions are retained; the system uses the rules matching the payroll period's fiscal year |
| AC-4 | Statutory rules are configured for Tenant A | A user from Tenant B accesses the statutory configuration API | Only Tenant B's rules are returned; Tenant A's rules are invisible due to RLS |
| AC-5 | A statutory deduction component is flagged as mandatory | HR attempts to remove it from a salary structure | The system prevents removal and displays an error indicating the component is mandatory for statutory compliance |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL allow configuration of income tax slabs with fields: slab_from, slab_to, rate_percentage, effective_from_date, effective_to_date, fiscal_year.
- FR-2: The system SHALL allow configuration of social security rules (EPF, ETF, or equivalent) with fields: component_name, employee_rate, employer_rate, wage_ceiling, applicable_on (Basic | Gross | Custom components), effective_from_date.
- FR-3: The system SHALL support multiple statutory deduction types configurable per tenant: income tax, employee provident fund, employer provident fund, social security tax, professional tax, and custom statutory items.
- FR-4: The system SHALL maintain versioned statutory rules with effective date ranges so that historical payroll calculations use the rules that were in effect at the time.
- FR-5: The system SHALL provide a "Test Calculation" feature where the admin enters a sample gross salary and sees the computed statutory deductions before saving.
- FR-6: The system SHALL validate that tax slabs are contiguous (no gaps or overlaps in income ranges).
- FR-7: The system SHALL support tax exemptions/rebates configuration (e.g., standard deduction, Section 80C equivalent).
- FR-8: All statutory configuration records SHALL carry `tenant_id` and be governed by PostgreSQL RLS policies.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Statutory rules SHALL be cached in Redis with a TTL of 30 minutes; cache invalidated on any write operation.
- NFR-2: Tax calculation for a single employee SHALL complete in < 10ms to support the 5,000-employee / 10-minute payroll NFR.
- NFR-3: Test coverage for statutory calculation logic SHALL be >= 90% (higher than the 85% module minimum due to financial/legal risk).
- NFR-4: All statutory configuration changes SHALL be audit-logged with before/after values and the user who made the change.
- NFR-5: Statutory calculation logic SHALL be isolated in a dedicated domain service with no side effects for easy unit testing.

## 6. Business Rules
- BR-1: Phase 1 supports statutory deductions for ONE configured country only (per technical doc section 3.2). The system architecture must support adding more countries in future phases.
- BR-2: Income tax is calculated on taxable income = gross earnings minus exempt components minus declared exemptions (e.g., provident fund contributions, investment declarations).
- BR-3: Tax slabs must be evaluated progressively (each slab applies only to the income within that slab range, not the total income).
- BR-4: Employer-side statutory contributions (e.g., Employer EPF, ETF) may or may not be included in the employee's CTC depending on tenant configuration.
- BR-5: Statutory deductions are calculated monthly but may need to consider year-to-date cumulative income for progressive tax computation.
- BR-6: If an employee has a tax-exempt status (e.g., below threshold), the system must skip tax deduction for that employee.
- BR-7: Statutory rules cannot be modified retroactively for periods that have finalized payroll runs; corrections must go through payroll adjustments (US-PAY-007).
- BR-8: Wage ceiling for social security contributions must be evaluated per pay period (monthly ceiling = annual ceiling / 12, or as per statutory rules).

## 7. Data Requirements

**statutory_rule table:**
| Column | Type | Constraints |
|--------|------|-------------|
| statutory_rule_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| rule_type | varchar(50) | NOT NULL (IncomeTax, EPF, ETF, ProfessionalTax, Custom) |
| rule_name | varchar(100) | NOT NULL |
| country_code | varchar(5) | NOT NULL |
| fiscal_year | varchar(10) | NOT NULL (e.g., "2026-2027") |
| effective_from | date | NOT NULL |
| effective_to | date | nullable |
| is_active | boolean | default true |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |

**tax_slab table:**
| Column | Type | Constraints |
|--------|------|-------------|
| tax_slab_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| statutory_rule_id | uuid (FK) | NOT NULL |
| slab_from | numeric(18,2) | NOT NULL |
| slab_to | numeric(18,2) | nullable (null = unlimited) |
| rate_percentage | numeric(5,2) | NOT NULL |
| order_index | int | NOT NULL |

**social_security_rule table:**
| Column | Type | Constraints |
|--------|------|-------------|
| social_security_rule_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| statutory_rule_id | uuid (FK) | NOT NULL |
| employee_rate | numeric(5,2) | NOT NULL |
| employer_rate | numeric(5,2) | NOT NULL |
| wage_ceiling_annual | numeric(18,2) | nullable |
| applicable_on | varchar(20) | NOT NULL (Basic, Gross, Custom) |
| applicable_component_ids | uuid[] | nullable (for Custom) |

## 8. UI/UX Notes (Notion-like)
- Statutory configuration page organized as tabbed sections: "Income Tax", "Provident Fund", "Other Deductions" -- each tab using a clean Notion-like card layout.
- Tax slab editor should use an editable inline table with add/remove row capabilities and real-time validation (highlight overlapping slabs in red).
- "Test Calculation" panel on the right side of the screen: enter a sample monthly gross, see computed tax, EPF, ETF, net deductions in real-time.
- Fiscal year selector at the top of the page as a dropdown or tab strip.
- Version history of statutory rules shown as a collapsible timeline below the configuration form.
- Mobile: configuration is a desktop-focused activity; mobile shows read-only view of current statutory rules.

## 9. Dependencies
- **US-PAY-001**: Statutory salary components must be defined (type=Statutory).
- **US-PAY-003**: Payroll run engine must call the statutory calculation service.
- **US-TENANT-xxx**: Tenant country/jurisdiction must be configured in tenant settings.
- No external tax API integration in Phase 1; rules are manually configured.

## 10. Assumptions & Constraints
- Statutory rules are manually configured by the Tenant Admin based on their country's tax laws; the system does not auto-update tax laws.
- Phase 1 supports one country's statutory framework; multi-country statutory engines are out of scope (technical doc section 3.2).
- The system provides the framework and calculation engine; compliance responsibility remains with the tenant.
- Tax computation uses the progressive slab method (most common globally); flat-rate tax is supported as a single-slab configuration.

## 11. Test Hints
- Unit test: Verify progressive tax calculation: income of 750,000 with slabs [0-250K@0%, 250K-500K@5%, 500K-1M@20%] should yield tax of 62,500.
- Unit test: Verify EPF calculation with wage ceiling: basic of 20,000, ceiling of 15,000, rate 12% should yield EPF of 1,800 (not 2,400).
- Unit test: Verify tax slab validation rejects overlapping ranges.
- Unit test: Verify tax slab validation rejects gaps in ranges.
- Integration test: Configure statutory rules in Tenant A, verify invisible from Tenant B.
- Integration test: Create tax slabs for FY 2026-2027, run payroll for a month in that FY, verify correct tax slab is applied.
- Integration test: Verify Redis cache invalidation when statutory rules are updated.
- E2E (Playwright): Navigate to statutory config, add tax slabs, use test calculator, verify computed values match expected.
