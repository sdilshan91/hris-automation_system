---
id: US-TRN-001
module: Training & Benefits
priority: Should Have
persona: HR Officer / Employee
status: draft
created: 2026-07-06
sprint: backlog
acceptance_criteria_count: 4
---

# US-TRN-001: Training Catalog & Course Enrollment  [STUB — flesh out before build]

> **STUB** — goal + AC skeleton + dependencies only. Part of the Training & Benefits epic (US-TRN-EPIC),
> COMPLETION-PLAN Theme M (zero prior coverage).

## 1. Description
**As an** HR Officer (maintaining a catalog) and an **Employee** (enrolling),
**I want** a tenant-scoped training course catalog with enrollment and completion tracking,
**So that** employees can be enrolled in training and their progress/completion is recorded.

## 2. Preconditions
- The tenant has employees (Core HR).

## 3. Acceptance Criteria (SKELETON — expand before build)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer manages the catalog | They create/edit a course | A tenant-scoped course record is created (title, description, mode, capacity, schedule). |
| AC-2 | A course exists | An employee (or HR) enrolls | An enrollment record is created; capacity/duplicate rules enforced; an enrollment notification is dispatched (US-NTF-006). |
| AC-3 | An enrolled employee finishes a course | Completion is marked | Completion status/date (and optional certificate) is recorded and visible on the employee's training history. |
| AC-4 | Two tenants use the catalog | Any action runs | Courses/enrollments are tenant-isolated; no cross-tenant visibility. |

## 4–10. Requirements (TO AUTHOR)
- FR/BR/NFR/data/UI: course model, enrollment lifecycle, capacity/waitlist, completion & certification, training history view, tenant isolation, audit.

## 9. Dependencies
- Core HR (employees), US-NTF-006 (notifications), US-TRN-EPIC.

## 11. Test Hints
- Create course; enroll; enforce capacity; mark complete; verify tenant isolation.
