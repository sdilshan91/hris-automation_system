---
id: US-TRN-003
module: Training & Benefits
priority: Should Have
persona: HR Officer / Employee
status: draft
created: 2026-07-06
sprint: backlog
acceptance_criteria_count: 4
---

# US-TRN-003: Benefit Eligibility & Employee Enrollment  [STUB — flesh out before build]

> **STUB** — goal + AC skeleton + dependencies only. Part of the Training & Benefits epic (US-TRN-EPIC),
> COMPLETION-PLAN Theme M (zero prior coverage).

## 1. Description
**As an** HR Officer (administering eligibility) and an **Employee** (enrolling in benefits),
**I want** eligibility rules that determine which employees can enroll in which benefit plans, plus an
enrollment flow,
**So that** employees enroll only in benefits they qualify for and their elections are recorded.

## 2. Preconditions
- Benefit plans exist (US-TRN-002); the tenant has employees (Core HR).

## 3. Acceptance Criteria (SKELETON — expand before build)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Eligibility rules are defined for a plan (e.g. by employment type / tenure / department) | An employee's eligibility is evaluated | The system determines eligible plans for that employee. |
| AC-2 | An eligible employee enrolls in a plan | They submit an election | An enrollment record is created (plan, coverage, effective date); an enrollment confirmation notification is dispatched (US-NTF-006). |
| AC-3 | An ineligible employee attempts to enroll | They submit | The enrollment is rejected with a clear eligibility reason. |
| AC-4 | Two tenants run enrollment | Any action runs | Eligibility/enrollment data is tenant-isolated; no cross-tenant visibility. |

## 4–10. Requirements (TO AUTHOR)
- FR/BR/NFR/data/UI: eligibility-rule model, evaluation engine, enrollment lifecycle, effective dating, open-enrollment windows (candidate), audit, tenant isolation.

## 9. Dependencies
- US-TRN-002 (plans), Core HR (employee attributes for eligibility), US-NTF-006 (notifications), US-TRN-EPIC.

## 11. Test Hints
- Define eligibility; evaluate eligible vs ineligible employee; enroll; reject ineligible; verify tenant isolation.
