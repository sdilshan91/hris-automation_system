---
id: US-TRN-001
module: Training & Benefits
priority: Should Have
persona: HR Officer / Employee
status: ready
created: 2026-07-06
updated: 2026-07-09
sprint: backlog
acceptance_criteria_count: 10
---

# US-TRN-001: Training Catalog & Course Enrollment

> Part of the Training & Benefits epic ([[US-TRN-EPIC]], COMPLETION-PLAN Theme M). **Greenfield** — confirmed
> no Training entity/service/controller exists in `src/`; only the permission constants
> `Training.View.Own` / `Training.View.All` / `Training.Manage` (`PermissionCatalog.cs`) and a `PlanModules`
> entry are pre-declared.

## 1. Description
**As an** HR Officer (maintaining the catalog and administering enrollments) and an **Employee** (browsing and
enrolling),
**I want** a tenant-scoped training course catalog with an enrollment lifecycle (capacity, waitlist,
cancellation), completion + certification tracking, and a per-employee training history,
**So that** employees can be enrolled in training, capacity is respected, and each employee's completed
training and certificates are on record.

## 2. Preconditions
- The tenant is `active`/`trial` and the Training module is enabled for its plan (`PlanModules`).
- The tenant has employees (Core HR).
- The acting user holds `Training.Manage` (catalog/enrollment admin), `Training.View.All` (HR read), or
  `Training.View.Own` (employee reads own history/enrollments).
- US-NTF-006 delivery layer exists for enrollment/waitlist/completion notifications.

## 3. Acceptance Criteria (IEEE 830 §3.2 — Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer with `Training.Manage` opens the catalog | They create a course (title, description, mode, capacity, schedule) | A tenant-scoped `TrainingCourse` row is created (`Status = Draft`), `TenantId` auto-stamped, the action audited. Required fields are validated (non-blank title, `Capacity >= 0` when set, `EndDate >= StartDate`). |
| AC-2 | A `Draft` course exists | HR sets it to `Open` | The course becomes enrollable; `Open` is a precondition for creating enrollments (AC-4). |
| AC-3 | An employee with `Training.View.Own` (or HR with `Training.Manage`) browses the catalog | They list courses | Only `Open` (and, for HR, all-status) tenant courses are returned; no cross-tenant courses appear. |
| AC-4 | An `Open` course has remaining capacity | An employee (or HR on their behalf) enrolls | A `CourseEnrollment` row is created `Status = Enrolled`, `EnrolledAt`/`EnrolledBy` stamped, and an enrollment notification is dispatched (US-NTF-006). |
| AC-5 | An `Open` course is at full capacity | An employee enrolls | The enrollment is created `Status = Waitlisted` with a `WaitlistPosition` (next in FIFO order); the employee is notified they are waitlisted. |
| AC-6 | An employee is already `Enrolled`/`Waitlisted` on a course | They enroll again for the same course | The duplicate is rejected (409) with a clear message; no second active enrollment is created. |
| AC-7 | An enrolled employee cancels (or HR cancels) before completion | Cancellation is submitted | The enrollment → `Cancelled` (`CancelledAt` stamped); if a seat frees and a waitlist exists, the first `Waitlisted` enrollment is promoted to `Enrolled` and that employee is notified. |
| AC-8 | An employee attended a course | HR marks completion (optionally with a certificate + score) | The enrollment → `Completed` with `CompletedAt`, optional `CertificateReference`/`Score`; the completion appears on the employee's training history and a completion notification is dispatched. |
| AC-9 | An employee (or HR) views training history | They open the employee's training view | All of that employee's enrollments (current + completed + cancelled) with status/dates/certificate are listed; an employee with only `Training.View.Own` sees **only their own** history. |
| AC-10 | Two tenants use the catalog | Any course/enrollment action runs | Courses and enrollments are tenant-isolated via the EF global query filter (RLS-eligible); Tenant A can neither read nor mutate Tenant B's courses/enrollments. |

## 4. Functional Requirements (IEEE 830 §3.2)
- FR-1: `TrainingCourse : BaseEntity` — `Title` (string, required), `Description` (string?), `Mode`
  (`TrainingMode`: `InPerson`/`Online`/`Hybrid`), `Capacity` (int?, null = unlimited), `Location` (string?),
  `Instructor` (string?), `StartDate` (DateOnly?/DateTime?), `EndDate` (DateOnly?/DateTime?), `DurationHours`
  (decimal?), `Status` (`CourseStatus`: `Draft`/`Open`/`Closed`/`Cancelled`/`Completed`). (`Cost`/budget →
  future.)
- FR-2: `CourseEnrollment : BaseEntity` — `CourseId` (FK → `TrainingCourse`), `EmployeeId` (FK → Employee),
  `Status` (`EnrollmentStatus`: `Enrolled`/`Waitlisted`/`Cancelled`/`Completed`/`NoShow`), `EnrolledAt`,
  `EnrolledBy` (Guid), `WaitlistPosition` (int?), `CancelledAt` (DateTime?), `CompletedAt` (DateTime?),
  `CertificateReference` (string?), `Score` (decimal?). Unique active enrollment per `(CourseId, EmployeeId)`.
- FR-3: Course CRUD (create/edit/list/get/status-transition) restricted to `Training.Manage`.
- FR-4: Enrollment creation enforces: course is `Open`; capacity check (→ `Waitlisted` when full); duplicate
  active-enrollment rejection.
- FR-5: Cancellation frees a seat and promotes the head of the FIFO waitlist to `Enrolled` (transactional).
- FR-6: Completion marks `Completed` + `CompletedAt` (+ optional certificate/score), by `Training.Manage`.
- FR-7: Training-history read: HR (`View.All`) for any employee; employee (`View.Own`) for self only.
- FR-8: All entities tenant-scoped (`TenantId` + EF query filter + `TenantInterceptor`); RLS-eligible.
- FR-9: Notifications on enroll / waitlist / waitlist-promotion / completion via US-NTF-006.
- FR-10: Administrative actions (course CRUD, enrollment status changes) audited to `audit_log`.

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: Catalog list SHALL return within 500ms for up to 500 courses.
- NFR-2: All course/enrollment data SHALL be tenant-isolated (EF query filters + RLS once enabled).
- NFR-3: Capacity/waitlist promotion SHALL be race-safe — concurrent enrollments must not oversubscribe past
  `Capacity`; run the seat-claim + promotion inside the Postgres retry-safe execution strategy (BUG-068 class).
- NFR-4: Enrollment/completion actions SHALL be audited.

## 6. Business Rules
- BR-1: Enrollment is only allowed on `Open` courses with remaining capacity; a full course waitlists.
- BR-2: One active (`Enrolled`/`Waitlisted`) enrollment per employee per course.
- BR-3: Cancellation of an `Enrolled` seat promotes the earliest `Waitlisted` enrollment (FIFO).
- BR-4: Only `Training.Manage` may create/edit courses or mark completion; employees may self-enroll and
  self-cancel their own enrollments.
- BR-5: `Capacity = null` means unlimited (never waitlists).
- BR-6: All training data is tenant-scoped; no cross-tenant visibility.

## 7. Data Requirements
- **New tables:** `training_course`, `course_enrollment` (tenant-scoped `BaseEntity`, snake_case,
  RLS-eligible). New enums `TrainingMode`, `CourseStatus`, `EnrollmentStatus`.
- **Reads:** Employee (Core HR) for enrollee identity/history.
- **Writes:** the two new tables; `audit_log`.
- **Migrations:** `dotnet ef migrations add` only.

## 8. API Surface (proposed)
- `GET /api/v1/tenant/training/courses` · `GET .../courses/{id}` · `POST .../courses` ·
  `PUT .../courses/{id}` · `POST .../courses/{id}/status` — `Training.Manage` for writes, `View.*` for reads.
- `POST /api/v1/tenant/training/courses/{id}/enrollments` (enroll) ·
  `POST .../enrollments/{id}/cancel` · `POST .../enrollments/{id}/complete` (`Training.Manage`).
- `GET /api/v1/tenant/training/me/enrollments` (employee self) ·
  `GET .../employees/{employeeId}/training-history` (`Training.View.All`).

## 9. Dependencies
- Core HR (employees) — enrollee identity + history.
- US-NTF-006 (notifications).
- [[US-TRN-EPIC]].

## 10. Assumptions & Constraints
- **v1:** catalog CRUD, enrollment (capacity/waitlist/cancel/duplicate-block), completion + a certificate
  *reference* (string/URL) + score, per-employee history.
- **Future:** budget/cost tracking, training analytics/reports, external LMS integration, generated
  certificate documents, attendance/session tracking for multi-session courses.

## 11. Test Hints
- Create course; open it; enroll to capacity; verify the (capacity+1)th enrollment is `Waitlisted`.
- Duplicate enroll → 409.
- Cancel an enrolled seat → verify FIFO waitlist promotion + notification.
- Mark completion → verify it shows on training history + completion notification.
- `View.Own` employee cannot read another employee's history.
- Concurrent enroll on the last seat (real Postgres) → no oversubscription past `Capacity`.
- Cross-tenant: Tenant A cannot read/mutate Tenant B's courses/enrollments.
