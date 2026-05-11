---
id: US-LV-002
module: Leave Management
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-LV-002: Set Yearly Leave Entitlements by Job Level/Department

## 1. Description
**As an** HR Officer,
**I want to** define leave entitlements per leave type based on job level, department, or employment type,
**So that** different employee groups automatically receive the correct number of leave days according to company policy.

## 2. Preconditions
- At least one leave type has been configured (US-LV-001).
- Core HR departments, job titles, and job levels are set up.
- User has `Leave.Configure` permission.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer is on the Leave Entitlement configuration page | They create an entitlement rule mapping "Annual Leave" to "Engineering Department, Senior Level" with 25 days/year | The rule is saved and employees matching the criteria receive 25 days upon next accrual calculation |
| AC-2 | An employee belongs to multiple matching rules (e.g., department rule and job-level rule) | The system calculates entitlement | The most specific rule wins (job-level + department > department-only > default); conflict resolution order is documented and consistent |
| AC-3 | HR Officer sets a per-employee override for a specific leave type | They enter a custom entitlement for employee X | The override takes precedence over all rule-based entitlements for that employee and leave type |
| AC-4 | A new employee is onboarded mid-year | Their leave balance is initialized | Entitlement is pro-rated based on joining date and the configured accrual frequency |
| AC-5 | HR Officer modifies an entitlement rule | They save the updated rule | A Hangfire background job recalculates affected employees' balances; changes are audit-logged |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: Entitlement rules support dimensions: leave type, department, job level, job title, employment type (full-time/part-time/contract), and tenure brackets.
- FR-2: Rule priority/specificity engine: employee override > (department + job level + employment type) > (department + job level) > (department) > (job level) > default entitlement on leave type.
- FR-3: Pro-rata calculation for mid-year joiners based on accrual frequency setting.
- FR-4: Bulk entitlement assignment UI for mass updates.
- FR-5: Hangfire recurring job to process annual entitlement accruals (monthly/quarterly/yearly/upfront based on leave type config).
- FR-6: Computed balances cached in Redis with key pattern `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Entitlement recalculation for 5,000 employees must complete within 60 seconds (Hangfire job).
- NFR-2: All entitlement data tenant-isolated via EF Core filters and PostgreSQL RLS.
- NFR-3: Redis cache for leave balances must have TTL of 24 hours with event-driven invalidation on write.

## 6. Business Rules
- BR-1: Entitlement rules are effective per leave year (configurable: calendar year or fiscal year per tenant).
- BR-2: Part-time employees receive entitlement proportional to their FTE (full-time equivalent) ratio.
- BR-3: Probation-period employees receive entitlement only for leave types marked `probation_eligible = true`.
- BR-4: Entitlement cannot be negative; minimum is zero.
- BR-5: When an employee transfers departments mid-year, entitlement is recalculated pro-rata for both periods.

## 7. Data Requirements
- **Table:** `leave_entitlement_rule`
- **Key columns:** `rule_id (uuid PK)`, `tenant_id (uuid FK)`, `leave_type_id (uuid FK)`, `department_id (uuid FK, nullable)`, `job_level_id (uuid FK, nullable)`, `job_title_id (uuid FK, nullable)`, `employment_type (varchar(20), nullable)`, `tenure_min_months (int, nullable)`, `tenure_max_months (int, nullable)`, `entitlement_days (numeric(5,2))`, `priority (int)`, `effective_from (date)`, `effective_to (date, nullable)`, `is_active (boolean)`, audit columns.
- **Table:** `leave_entitlement_override`
- **Key columns:** `override_id (uuid PK)`, `tenant_id`, `employee_id`, `leave_type_id`, `leave_year (int)`, `entitlement_days (numeric(5,2))`, `reason (text)`, audit columns.
- **Table:** `leave_ledger` — transaction log: accrual, used, adjusted, encashed, carry-forward, expired.

## 8. UI/UX Notes (Notion-like)
- Entitlement rules displayed in a filterable matrix: rows = leave types, columns = departments/levels, cells = entitlement days.
- Inline editing of entitlement values in the matrix cells.
- Employee override accessible from the employee profile page under a "Leave" tab.
- Notion-like database view with filter/sort/group capabilities.
- Mobile: Collapse matrix into a card-based list grouped by leave type.

## 9. Dependencies
- **US-LV-001**: Leave types must be configured first.
- **US-CORE-***: Departments, job titles, job levels, employment types must exist.
- **Hangfire**: Background job infrastructure for accrual processing.
- **Redis**: Caching infrastructure for computed balances.

## 10. Assumptions & Constraints
- Leave year start date is configurable per tenant (default: January 1).
- The system recalculates entitlements on a scheduled basis (configurable: daily at midnight tenant-local-time via Hangfire).
- Only free/open-source libraries are used.
- Pro-rata calculation rounds to two decimal places (half-up rounding).

## 11. Test Hints
- Test rule priority: Create overlapping rules (department-only vs department+level) and verify the most specific rule wins.
- Test pro-rata: Onboard an employee on July 1 with annual entitlement of 20 days; verify they receive 10 days.
- Test override: Set an override of 30 days for an employee who would normally get 20; verify balance shows 30.
- Test part-time FTE: Employee at 0.5 FTE with 20-day entitlement should receive 10 days.
- Test tenant isolation: Verify rules in Tenant A do not affect employees in Tenant B.
- Test Hangfire job: Trigger accrual job and verify ledger entries are created correctly.
