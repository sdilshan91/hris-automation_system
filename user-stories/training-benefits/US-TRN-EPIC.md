---
id: US-TRN-EPIC
module: Training & Benefits
priority: Should Have
persona: HR Officer / Employee
status: draft
created: 2026-07-06
updated: 2026-07-09
sprint: backlog
acceptance_criteria_count: 0
---

# EPIC: Training & Benefits Module

> Module-level epic for Training & Benefits.
> **Reconciliation (COMPLETION-PLAN Theme M).** The module had **no user stories, no test-cases, and had never
> been executed** — a full-module blind spot. This epic frames the module; its three core stories
> ([[US-TRN-001]]..[[US-TRN-003]]) are now **authored to full IEEE-830 (build-ready, 2026-07-09)**. Referenced
> by the BA module-priority list (#10 Training & Benefits).

## Goal
Provide tenant-scoped training management (course catalog, enrollments, completion tracking) and benefits
administration (benefit plans, eligibility, employee enrollment) so employees can be enrolled in training and
benefits, and HR can administer both — with full multi-tenant isolation.

## Scope (child stories — all authored, build-ready)
| ID | Title | Persona |
|----|-------|---------|
| [[US-TRN-001]] | Training catalog & course enrollment | HR Officer / Employee |
| [[US-TRN-002]] | Benefits plan administration | Tenant Admin / HR Officer |
| [[US-TRN-003]] | Benefit eligibility & employee enrollment | HR Officer / Employee |

> Additional candidate stories to author later (see **v1 vs Future** below): training budget/cost tracking,
> training reports/analytics, dependent management, benefit payroll-deduction integration.

## Cross-cutting requirements (apply to every child story)
- **Multi-tenant isolation:** all training/benefit entities carry `tenant_id`, EF query filters + RLS; no cross-tenant visibility.
- **Notifications:** enrollment/eligibility/completion events dispatch via US-NTF-006.
- **Audit:** administrative actions audited to the tenant `audit_log`.
- **IEEE 830:** each child story to be authored with full AC/FR/BR/NFR before build.

## Dependencies
- Core HR (employees, departments) for enrollee/eligibility data.
- US-NTF-006 (delivery layer) for notifications.
- US-ADM-009 / US-ADM-012 if training/benefits are plan-gated modules.

---

## Entity Model Sketch (all `: BaseEntity` → inherit `Id`, `TenantId`, audit fields, `IsDeleted`; snake_case; RLS-eligible)

**Training ([[US-TRN-001]]):**
- `TrainingCourse` — `Title`, `Description?`, `Mode` (`TrainingMode`: InPerson/Online/Hybrid), `Capacity?`
  (null = unlimited), `Location?`, `Instructor?`, `StartDate?`, `EndDate?`, `DurationHours?`, `Status`
  (`CourseStatus`: Draft/Open/Closed/Cancelled/Completed).
- `CourseEnrollment` — `CourseId` (FK), `EmployeeId` (FK), `Status` (`EnrollmentStatus`:
  Enrolled/Waitlisted/Cancelled/Completed/NoShow), `EnrolledAt`, `EnrolledBy`, `WaitlistPosition?`,
  `CancelledAt?`, `CompletedAt?`, `CertificateReference?`, `Score?`. Unique active per `(CourseId, EmployeeId)`.

**Benefits ([[US-TRN-002]] plans, [[US-TRN-003]] eligibility/enrollment):**
- `BenefitPlan` — `Name`, `Type` (`BenefitType`: Health/Dental/Vision/Life/Retirement/Disability/Other),
  `Description?`, `CoverageDetails?`, `EmployerCost?` (decimal), `EmployeeCost?` (decimal), `Currency`,
  `EffectiveFrom`, `EffectiveTo?`, `Status` (`BenefitPlanStatus`: Draft/Active/Inactive/Archived).
  Optional window columns `EnrollmentOpensAt?`/`EnrollmentClosesAt?`.
- `BenefitEligibilityRule` — `BenefitPlanId` (FK), `Attribute` (`EligibilityAttribute`:
  EmploymentType/TenureDays/Department/JobGrade), `Operator` (reuses `WorkflowCondition` vocab +`In`), `Value`.
- `BenefitEnrollment` — `BenefitPlanId` (FK), `EmployeeId` (FK), `Status` (`BenefitEnrollmentStatus`:
  Active/Pending/Declined/Terminated), `CoverageLevel` (EmployeeOnly/EmployeeSpouse/Family), `EffectiveDate`,
  `EndDate?`, `ElectedAt`, `ElectedBy`. Unique active per `(BenefitPlanId, EmployeeId)`.

> **Permission mapping** (pre-declared in `PermissionCatalog.cs`, dotted-segment string form):
> `Training.View.Own` (employee self history/enroll) · `Training.View.All` (HR read) · `Training.Manage`
> (course CRUD, enrollment admin, completion). `Benefits.View.Own` · `Benefits.View.All` · `Benefits.Manage`
> (plan/rule admin, enroll-on-behalf).

## Story Sequence (each = one shippable `/implement-story` unit)

1. **[[US-TRN-001]] Training** — independent (Core HR + US-NTF-006 only). Buildable standalone first. *(If it
   runs large, an optional split is 001a catalog + enrollment/waitlist, 001b completion + certification +
   history — but it is sized to ship as one unit.)*
2. **[[US-TRN-002]] Benefit plan admin** — independent; foundation for enrollment. Build before 003.
3. **[[US-TRN-003]] Benefit eligibility & enrollment** — **depends on 002** (consumes `BenefitPlan`). Build last.

> 001 and 002 are independent of each other and could be built in parallel (non-overlapping paths / separate
> migrations); 003 must follow 002.

## v1 vs Future

**v1 (this backlog):** course catalog CRUD; enrollment lifecycle (capacity, FIFO waitlist, cancellation,
duplicate-block); completion + certificate *reference* + score; per-employee training history; benefit-plan
CRUD + lifecycle + employer/employee cost + effective dating; eligibility rules (employment type / tenure /
department / job grade, AND-composed) + evaluation; enrollment with coverage level + effective dating +
optional open-enrollment window + termination; full tenant isolation + audit + US-NTF-006 notifications.

**Future (explicitly deferred):** **dependent management** (spouse/children under an enrollment);
budget/cost tracking for training; training reports/analytics; external LMS integration; generated
certificate documents; multi-session attendance tracking; benefit plan-cost **payroll-deduction integration**;
coverage-level premium tiers; plan-year/renewal cycles; life-event qualifying changes outside the enrollment
window; complex (AND/OR) eligibility rule groups; carrier/vendor metadata.
