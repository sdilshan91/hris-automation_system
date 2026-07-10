---
id: US-TRN-003
module: Training & Benefits
priority: Should Have
persona: HR Officer / Employee
status: ready
created: 2026-07-06
updated: 2026-07-09
sprint: backlog
acceptance_criteria_count: 9
---

# US-TRN-003: Benefit Eligibility & Employee Enrollment

> Part of the Training & Benefits epic ([[US-TRN-EPIC]], COMPLETION-PLAN Theme M). Depends on benefit plans
> from [[US-TRN-002]]. **Greenfield** — reuses `Benefits.View.Own` / `Benefits.View.All` / `Benefits.Manage`
> (`PermissionCatalog.cs`).

## 1. Description
**As an** HR Officer (defining eligibility rules and administering enrollment) and an **Employee** (enrolling
in benefits they qualify for),
**I want** eligibility rules that determine which employees can enroll in which benefit plans, plus an
enrollment flow bounded by an optional open-enrollment window,
**So that** employees enroll only in benefits they qualify for and their elections are recorded with effective
dating.

## 2. Preconditions
- Benefit plans exist and are `Active` within their effective window ([[US-TRN-002]]).
- The tenant has employees with the attributes eligibility keys off (employment type, hire date/tenure,
  department) — Core HR.
- The acting user holds `Benefits.Manage` (define rules, enroll on behalf), or `Benefits.View.Own`
  (self-service enroll/view).
- US-NTF-006 exists for enrollment-confirmation notifications.

## 3. Acceptance Criteria (IEEE 830 §3.2 — Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An admin with `Benefits.Manage` defines eligibility rules for a plan (e.g. employment type = FullTime, min tenure = 90 days, department ∈ {…}) | They save the rules | Tenant-scoped `BenefitEligibilityRule` rows are created against the plan; the action is audited. Absence of rules means the plan is open to all active employees. |
| AC-2 | Rules exist for a plan | An employee's eligibility is evaluated | The engine returns eligible/ineligible for that `(employee, plan)` by ANDing all rules against the employee's Core HR attributes; the eligible-plans list for an employee returns only plans they satisfy. |
| AC-3 | An eligible employee, within the plan's open-enrollment window | They submit an election (plan, coverage level, effective date) | A `BenefitEnrollment` row is created `Status = Active` with `EffectiveDate`, `ElectedAt`/`ElectedBy`; an enrollment-confirmation notification is dispatched (US-NTF-006). |
| AC-4 | An **ineligible** employee attempts to enroll | They submit | The enrollment is rejected (422) with a clear, specific eligibility reason (which rule failed); no enrollment row is created. |
| AC-5 | An employee is already actively enrolled in a plan | They enroll again in the same plan | The duplicate is rejected (409); no second active enrollment for the same `(plan, employee)`. |
| AC-6 | A plan defines an open-enrollment window and the current date is **outside** it | An employee (without a qualifying new-hire exception) attempts to enroll | The enrollment is rejected with an "enrollment window closed" reason; enrollment is allowed only within the window (or when no window is defined = always open). |
| AC-7 | An enrolled employee (or HR) terminates an enrollment | Termination is submitted | The enrollment → `Terminated` with `EndDate`; it stops appearing as an active election; the change is audited. |
| AC-8 | An employee views their benefits | They open self-service | They see their eligible plans and their own active/terminated enrollments only (`View.Own`); HR (`View.All`) can view any employee's enrollments. |
| AC-9 | Two tenants run enrollment | Any eligibility/enrollment action runs | Eligibility rules and enrollments are tenant-isolated via the EF global query filter (RLS-eligible); no cross-tenant visibility or action. |

## 4. Functional Requirements (IEEE 830 §3.2)
- FR-1: `BenefitEligibilityRule : BaseEntity` — `BenefitPlanId` (FK → `BenefitPlan`), `Attribute`
  (`EligibilityAttribute`: `EmploymentType`/`TenureDays`/`Department`/`JobGrade`), `Operator` (reuse the
  `WorkflowCondition` operator vocabulary: `==`/`!=`/`>`/`>=`/`<`/`<=`, plus `In` for set membership),
  `Value` (string/JSON — e.g. `"FullTime"`, `90`, or a department-id list).
- FR-2: `BenefitEnrollment : BaseEntity` — `BenefitPlanId` (FK), `EmployeeId` (FK → Employee), `Status`
  (`BenefitEnrollmentStatus`: `Active`/`Pending`/`Declined`/`Terminated`), `CoverageLevel`
  (`CoverageLevel`: `EmployeeOnly`/`EmployeeSpouse`/`Family`), `EffectiveDate`, `EndDate` (DateTime?),
  `ElectedAt`, `ElectedBy` (Guid). Unique active enrollment per `(BenefitPlanId, EmployeeId)`.
- FR-3: (Optional, v1) an open-enrollment window on the plan — `EnrollmentOpensAt`/`EnrollmentClosesAt`
  (nullable columns on `BenefitPlan`, or a small `BenefitEnrollmentWindow` child). If both null → always open.
- FR-4: Eligibility evaluation ANDs all `BenefitEligibilityRule` rows for a plan against the employee's Core HR
  attributes; no rules → eligible-if-active-employee.
- FR-5: Enrollment creation enforces: plan `Active` + in effective window; employee eligible; within open
  window (or new-hire exception); no duplicate active enrollment.
- FR-6: Enrollment termination sets `Status = Terminated` + `EndDate`; does not delete history.
- FR-7: Eligible-plans read + own-enrollment read for `View.Own`; any-employee read for `View.All`; rule
  definition + enroll-on-behalf for `Manage`.
- FR-8: All entities tenant-scoped (`TenantId` + EF query filter); RLS-eligible.
- FR-9: Notification on enrollment confirmation / termination via US-NTF-006.
- FR-10: Rule/enrollment administrative actions audited to `audit_log`.

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: Eligibility evaluation for one employee across all plans SHALL complete within 300ms.
- NFR-2: All eligibility/enrollment data SHALL be tenant-isolated (EF query filters + RLS once enabled).
- NFR-3: Enrollment creation SHALL be transactional and duplicate-safe under concurrency (unique constraint on
  `(BenefitPlanId, EmployeeId)` for active rows).
- NFR-4: Rule/enrollment actions SHALL be audited.

## 6. Business Rules
- BR-1: An employee may enroll in a plan only if they satisfy **all** of that plan's eligibility rules.
- BR-2: Enrollment is only allowed on `Active`, in-effective-window plans and within the plan's
  open-enrollment window (or always, if no window is defined).
- BR-3: One active enrollment per employee per plan.
- BR-4: Ineligible enrollment attempts are rejected with the specific failing rule as the reason.
- BR-5: Termination preserves the record (soft state change), never a hard delete.
- BR-6: `Benefits.View.Own` sees only self; `View.All`/`Manage` see all; only `Manage` defines rules.
- BR-7: All eligibility/enrollment data is tenant-scoped; no cross-tenant visibility.

## 7. Data Requirements
- **New tables:** `benefit_eligibility_rule`, `benefit_enrollment` (tenant-scoped `BaseEntity`, snake_case,
  RLS-eligible). New enums `EligibilityAttribute`, `BenefitEnrollmentStatus`, `CoverageLevel`. Optional window
  columns on `benefit_plan`.
- **Reads:** `BenefitPlan` (US-TRN-002), Employee attributes (Core HR: employment type, hire date/tenure,
  department, job grade).
- **Writes:** the two new tables; `audit_log`.
- **Migrations:** `dotnet ef migrations add` only.

## 8. API Surface (proposed)
- `POST /api/v1/tenant/benefits/plans/{planId}/eligibility-rules` · `GET .../plans/{planId}/eligibility-rules`
  · `DELETE .../eligibility-rules/{id}` (`Benefits.Manage`).
- `GET /api/v1/tenant/benefits/eligible` (employee's eligible plans, `View.Own`) ·
  `GET .../employees/{employeeId}/eligible` (`View.All`).
- `POST /api/v1/tenant/benefits/enrollments` (enroll) · `POST .../enrollments/{id}/terminate` ·
  `GET .../me/enrollments` (`View.Own`) · `GET .../employees/{employeeId}/enrollments` (`View.All`).

## 9. Dependencies
- [[US-TRN-002]] (benefit plans) — hard dependency.
- Core HR (employee attributes for eligibility).
- US-NTF-006 (notifications).
- [[US-TRN-EPIC]].

## 10. Assumptions & Constraints
- **v1:** eligibility rules (employment type / tenure / department / job grade, AND-composed), eligibility
  evaluation, enrollment with coverage level + effective dating, optional open-enrollment window, termination,
  self-service + HR views.
- **Future:** **dependent management** (spouse/children records under an enrollment — epic-flagged future),
  life-event qualifying changes outside the window, complex (AND/OR) rule groups, coverage-level premium
  calculation, payroll-deduction integration for `EmployeeCost`.
- Reuses the `WorkflowCondition` operator vocabulary conceptually for rule comparisons (no new expression
  language for v1).

## 11. Test Hints
- Define rules (FullTime + 90-day tenure); evaluate an eligible vs ineligible employee.
- Enroll eligible → `Active` + confirmation notification; enroll ineligible → 422 with failing-rule reason.
- Duplicate active enrollment → 409.
- Enrollment outside the open-enrollment window → rejected; inside → allowed.
- Terminate → `Terminated` + `EndDate`; no longer active.
- `View.Own` cannot see another employee's enrollments.
- Cross-tenant: Tenant A cannot read/mutate Tenant B's rules/enrollments.
