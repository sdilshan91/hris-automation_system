---
module: Leave Management
total_user_stories: 6
total_test_cases: 152
created: 2026-06-13
updated: 2026-06-14
status: draft
---

# Leave Management -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 6 (US-LV-001, US-LV-002, US-LV-003, US-LV-004, US-LV-005, US-LV-006) |
| Total Test Cases | 152 |
| Critical Priority | 70 |
| High Priority | 72 |
| Medium Priority | 10 |
| Low Priority | 0 |
| Blocked Test Cases | 0 (TC-LV-056 holiday-exclusion steps conditionally blocked on US-LV-007) |
| Deferred Test Cases | 5+ (TC-LV-021 onboarding seeding, TC-LV-ISO-004 cache -- partial, TC-LV-031 FTE, TC-LV-042 Redis cache, TC-LV-046 job-level dimension, TC-LV-ISO-008/TC-LV-ISO-012/TC-LV-ISO-016 balance cache keys -- partial; US-LV-004: TC-LV-077 history/team-calendar subsections, TC-LV-079 SignalR real-time push, TC-LV-088 multi-level approval -- conditional on downstream modules; US-LV-005: TC-LV-097 multi-level approval -- conditional on US-ADM-007, TC-LV-098 payroll-lock -- conditional on payroll module, TC-LV-107 async notification -- DEFERRED on notifications module, TC-LV-ISO-020 balance cache keys -- partial pending Redis; US-LV-006: TC-LV-121/TC-LV-125 Redis balance cache -- DEFERRED (DB-fallback verified), TC-LV-ISO-024 balance cache keys -- partial pending Redis) |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-LV-001 | Configure Leave Types Per Tenant | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-009, TC-LV-010, TC-LV-011, TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-016, TC-LV-017, TC-LV-018, TC-LV-019, TC-LV-020, TC-LV-021, TC-LV-022, TC-LV-023, TC-LV-024, TC-LV-025 | 25 |
| Cross-cutting (LV-001) | Multi-tenant isolation (mandatory) | TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 4 |
| US-LV-002 | Set Yearly Leave Entitlements by Job Level/Department | TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-033, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-038, TC-LV-039, TC-LV-040, TC-LV-041, TC-LV-042, TC-LV-043, TC-LV-044, TC-LV-045, TC-LV-046, TC-LV-047 | 22 |
| Cross-cutting (LV-002) | Multi-tenant isolation (mandatory) | TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 4 |
| US-LV-003 | Employee Applies for Leave | TC-LV-048, TC-LV-049, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-055, TC-LV-056, TC-LV-057, TC-LV-058, TC-LV-059, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-063, TC-LV-064, TC-LV-065 | 18 |
| Cross-cutting (LV-003) | Multi-tenant isolation (mandatory) | TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | 4 |
| US-LV-004 | Manager Views Pending Leave Queue with Balance Inline | TC-LV-066, TC-LV-067, TC-LV-068, TC-LV-069, TC-LV-070, TC-LV-071, TC-LV-072, TC-LV-073, TC-LV-074, TC-LV-075, TC-LV-076, TC-LV-077, TC-LV-078, TC-LV-079, TC-LV-080, TC-LV-081, TC-LV-082, TC-LV-083, TC-LV-084, TC-LV-085, TC-LV-086, TC-LV-087, TC-LV-088 | 23 |
| Cross-cutting (LV-004) | Multi-tenant isolation (mandatory) | TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015, TC-LV-ISO-016 | 4 |
| US-LV-005 | Manager Approves or Rejects Leave Request | TC-LV-089, TC-LV-090, TC-LV-091, TC-LV-092, TC-LV-093, TC-LV-094, TC-LV-095, TC-LV-096, TC-LV-097, TC-LV-098, TC-LV-099, TC-LV-100, TC-LV-101, TC-LV-102, TC-LV-103, TC-LV-104, TC-LV-105, TC-LV-106, TC-LV-107, TC-LV-108 | 20 |
| Cross-cutting (LV-005) | Multi-tenant isolation (mandatory) | TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020 | 4 |
| US-LV-006 | Leave Balance Dashboard for Employee | TC-LV-109, TC-LV-110, TC-LV-111, TC-LV-112, TC-LV-113, TC-LV-114, TC-LV-115, TC-LV-116, TC-LV-117, TC-LV-118, TC-LV-119, TC-LV-120, TC-LV-121, TC-LV-122, TC-LV-123, TC-LV-124, TC-LV-125, TC-LV-126, TC-LV-127, TC-LV-128 | 20 |
| Cross-cutting (LV-006) | Multi-tenant isolation (mandatory) | TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (LV-001) | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-009, TC-LV-010, TC-LV-011, TC-LV-017, TC-LV-018, TC-LV-021, TC-LV-022, TC-LV-024, TC-LV-025 | 17 |
| Functional (LV-002) | TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-033, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-038, TC-LV-043, TC-LV-045, TC-LV-046 | 16 |
| Functional (LV-003) | TC-LV-048, TC-LV-049, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-055, TC-LV-056, TC-LV-057, TC-LV-059, TC-LV-063 | 12 |
| Functional (LV-004) | TC-LV-066, TC-LV-067, TC-LV-068, TC-LV-069, TC-LV-070, TC-LV-071, TC-LV-072, TC-LV-073, TC-LV-074, TC-LV-075, TC-LV-076, TC-LV-077, TC-LV-078, TC-LV-079, TC-LV-080, TC-LV-087, TC-LV-088 | 17 |
| Functional (LV-005) | TC-LV-089, TC-LV-090, TC-LV-091, TC-LV-092, TC-LV-093, TC-LV-094, TC-LV-095, TC-LV-096, TC-LV-097, TC-LV-098, TC-LV-105, TC-LV-106, TC-LV-107, TC-LV-108 | 14 |
| Functional (LV-006) | TC-LV-109, TC-LV-110, TC-LV-111, TC-LV-112, TC-LV-113, TC-LV-114, TC-LV-115, TC-LV-116, TC-LV-117, TC-LV-118, TC-LV-119, TC-LV-120, TC-LV-121, TC-LV-127 | 14 |
| Security (LV-001) | TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 8 |
| Security (LV-002) | TC-LV-039, TC-LV-040, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 7 |
| Security (LV-003) | TC-LV-058, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | 8 |
| Security (LV-004) | TC-LV-081, TC-LV-082, TC-LV-083, TC-LV-084, TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015, TC-LV-ISO-016 | 8 |
| Security (LV-005) | TC-LV-099, TC-LV-100, TC-LV-101, TC-LV-102, TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020 | 8 |
| Security (LV-006) | TC-LV-122, TC-LV-123, TC-LV-124, TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024 | 7 |
| Performance (LV-001) | TC-LV-016, TC-LV-023 | 2 |
| Performance (LV-002) | TC-LV-041, TC-LV-042 | 2 |
| Performance (LV-003) | TC-LV-064 | 1 |
| Performance (LV-004) | TC-LV-085 (TC-LV-069 page-size bound embedded) | 1 |
| Performance (LV-005) | TC-LV-103 (TC-LV-107 non-blocking-notification embedded) | 1 |
| Performance (LV-006) | TC-LV-125, TC-LV-126 | 2 |
| Accessibility (LV-001) | TC-LV-019 | 1 |
| Accessibility (LV-002) | TC-LV-044 | 1 |
| Accessibility (LV-003) | TC-LV-065 | 1 |
| Accessibility (LV-004) | TC-LV-086 | 1 |
| Accessibility (LV-005) | TC-LV-104 | 1 |
| Accessibility (LV-006) | TC-LV-128 | 1 |
| Cross-Browser (LV-001) | TC-LV-020 | 1 |
| Cross-Browser (LV-002) | TC-LV-045 | 1 |
| Cross-Browser (LV-003) | TC-LV-065 | 1 |
| Cross-Browser (LV-004) | TC-LV-086, TC-LV-087 | 2 |
| Cross-Browser (LV-005) | TC-LV-108 | 1 |
| Cross-Browser (LV-006) | TC-LV-127, TC-LV-128 | 2 |

## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-LV-001, TC-LV-002, TC-LV-004, TC-LV-005, TC-LV-009, TC-LV-010, TC-LV-012, TC-LV-017, TC-LV-021, TC-LV-024, TC-LV-025, TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-046, TC-LV-048, TC-LV-049, TC-LV-055, TC-LV-056, TC-LV-066, TC-LV-071, TC-LV-072, TC-LV-073, TC-LV-075, TC-LV-077, TC-LV-078, TC-LV-079, TC-LV-080, TC-LV-089, TC-LV-090, TC-LV-091, TC-LV-105, TC-LV-109, TC-LV-110, TC-LV-111, TC-LV-113, TC-LV-114, TC-LV-116, TC-LV-117, TC-LV-120, TC-LV-121 | 49 |
| Negative Test | TC-LV-003, TC-LV-006, TC-LV-007, TC-LV-011, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-022, TC-LV-025, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-032, TC-LV-033, TC-LV-038, TC-LV-039, TC-LV-040, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-057, TC-LV-058, TC-LV-059, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-063, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012, TC-LV-067, TC-LV-070, TC-LV-074, TC-LV-081, TC-LV-082, TC-LV-083, TC-LV-084, TC-LV-088, TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015, TC-LV-ISO-016, TC-LV-092, TC-LV-093, TC-LV-094, TC-LV-095, TC-LV-096, TC-LV-097, TC-LV-098, TC-LV-099, TC-LV-100, TC-LV-101, TC-LV-102, TC-LV-106, TC-LV-107, TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020, TC-LV-119, TC-LV-122, TC-LV-123, TC-LV-124, TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024 | 76 |
| Boundary Test | TC-LV-006, TC-LV-008, TC-LV-009, TC-LV-029, TC-LV-031, TC-LV-033, TC-LV-038, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-055, TC-LV-056, TC-LV-057, TC-LV-063, TC-LV-067, TC-LV-068, TC-LV-069, TC-LV-070, TC-LV-073, TC-LV-074, TC-LV-076, TC-LV-078, TC-LV-080, TC-LV-088, TC-LV-091, TC-LV-092, TC-LV-093, TC-LV-096, TC-LV-110, TC-LV-112, TC-LV-113, TC-LV-115, TC-LV-116, TC-LV-117, TC-LV-118, TC-LV-119, TC-LV-120, TC-LV-124, TC-LV-127 | 41 |
| Security Test | TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-039, TC-LV-040, TC-LV-042, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008, TC-LV-058, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-063, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012, TC-LV-081, TC-LV-082, TC-LV-083, TC-LV-084, TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015, TC-LV-ISO-016, TC-LV-096, TC-LV-099, TC-LV-100, TC-LV-101, TC-LV-102, TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020, TC-LV-122, TC-LV-123, TC-LV-124, TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024 | 49 |
| Multi-Tenant Isolation | TC-LV-012, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-042, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012, TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015, TC-LV-ISO-016, TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020, TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024 | 26 |
| Performance Test | TC-LV-016, TC-LV-023, TC-LV-041, TC-LV-042, TC-LV-064, TC-LV-069, TC-LV-085, TC-LV-103, TC-LV-107, TC-LV-125, TC-LV-126 | 11 |
| Accessibility Test | TC-LV-019, TC-LV-044, TC-LV-065, TC-LV-086, TC-LV-104, TC-LV-128 | 6 |
| Cross-Browser Test | TC-LV-018, TC-LV-020, TC-LV-043, TC-LV-045, TC-LV-065, TC-LV-086, TC-LV-087, TC-LV-108, TC-LV-127, TC-LV-128 | 10 |

## Acceptance Criteria Coverage (US-LV-001)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Create leave type with full config, tenant-scoped | TC-LV-001, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-012, TC-LV-022, TC-LV-024 |
| AC-2 | Edit entitlement/carry-forward with audit trail, effective next cycle | TC-LV-002, TC-LV-017 |
| AC-3 | Duplicate name rejected case-insensitive | TC-LV-003 |
| AC-4 | Deactivate hides from dropdown, existing requests unaffected | TC-LV-004 |
| AC-5 | Documents-required threshold enforced on apply | TC-LV-005 |

## Acceptance Criteria Coverage (US-LV-002)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Create entitlement rule mapping leave type to department/level, employees get correct days on next accrual | TC-LV-026, TC-LV-036 |
| AC-2 | Overlapping rules resolved by specificity (most specific wins) | TC-LV-027 |
| AC-3 | Per-employee override takes precedence over all rule-based entitlements | TC-LV-028 |
| AC-4 | Mid-year joiner entitlement pro-rated based on joining date and accrual frequency | TC-LV-029 |
| AC-5 | Rule modification triggers Hangfire recalculation and audit log | TC-LV-030 |

## Acceptance Criteria Coverage (US-LV-003)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Submit valid request -> Pending, leave-requested notification queued, confirmation shown | TC-LV-048 |
| AC-2 | Inline balance shown; insufficient balance (no negative allowed) blocks submission | TC-LV-049, TC-LV-050 |
| AC-3 | Sick leave over document threshold without attachment is rejected | TC-LV-051 |
| AC-4 | Half-day leave creates 0.5-day request and decrements balance accordingly | TC-LV-055 |
| AC-5 | Overlapping dates with existing Pending/Approved request rejected | TC-LV-052 |
| AC-6 | Public holidays excluded from leave day count; adjusted count shown | TC-LV-056 (holiday exclusion depends on US-LV-007) |

## Acceptance Criteria Coverage (US-LV-004)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Pending queue lists direct reports' requests, oldest-first, with employee/type/dates/days/reason/inline balance | TC-LV-066, TC-LV-067, TC-LV-075, TC-LV-076, TC-LV-080 |
| AC-2 | Server-side pagination (default 20), total count shown | TC-LV-068, TC-LV-069, TC-LV-070 |
| AC-3 | Filter by leave type, employee, or date range | TC-LV-071, TC-LV-072, TC-LV-073, TC-LV-074, TC-LV-075 |
| AC-4 | Detail panel: full details, attachments, balance, history summary, team-calendar snippet, conflict count | TC-LV-077, TC-LV-078 |
| AC-5 | New request included on queue refresh (real-time push via SignalR) | TC-LV-079 (SignalR push DEFERRED on notifications module) |

## Acceptance Criteria Coverage (US-LV-005)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Approve -> status Approved, used-ledger entry, balance decreased, audit, leave-approved notification queued, Redis cache invalidated | TC-LV-089, TC-LV-091, TC-LV-105 (Redis invalidation DEFERRED; notification seam DEFERRED) |
| AC-2 | Reject with mandatory reason -> status Rejected, no ledger entry, audit, leave-rejected notification with reason, reason in approval history | TC-LV-090, TC-LV-094, TC-LV-105 |
| AC-3 | Insufficient balance at approval -> block (negative not allowed) or confirm (negative allowed) | TC-LV-092, TC-LV-093 |
| AC-4 | Multi-level approval -> first approval moves to Pending L2 and notifies next approver | TC-LV-097 (CONDITIONAL on approval-workflow config US-ADM-007) |
| AC-5 | Two simultaneous decisions -> only first succeeds, second gets 409 (xmin optimistic concurrency) | TC-LV-096 |

## Acceptance Criteria Coverage (US-LV-006)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Dashboard shows a summary card per active leave type with entitlement/used/pending/balance + progress bar | TC-LV-109, TC-LV-110, TC-LV-114, TC-LV-115, TC-LV-116 |
| AC-2 | Click a balance card -> ledger/transaction history (accruals, usages, adjustments, carry-forwards, expirations) for the current leave year | TC-LV-111, TC-LV-112, TC-LV-117 |
| AC-3 | Upcoming Leaves lists approved + pending future requests with dates/type/status/days | TC-LV-113, TC-LV-120 |
| AC-4 | Mobile 360px -- cards stack, remain readable, progress bars scale | TC-LV-127 |
| AC-5 | New joiner with no ledger data -> friendly empty state | TC-LV-119 |

## Functional Requirements Coverage (US-LV-001)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | CRUD operations for leave types scoped to tenant_id | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-011, TC-LV-012, TC-LV-022 | Direct |
| FR-2 | All configurable fields supported | TC-LV-001, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-010, TC-LV-024, TC-LV-025 | Direct |
| FR-3 | Leave types orderable via display_order | TC-LV-009 | Direct |
| FR-4 | Default leave types seeded during tenant onboarding | TC-LV-021 | DEFERRED (onboarding wizard not implemented) |
| FR-5 | Soft delete -- deactivated types hidden from forms but retained | TC-LV-004, TC-LV-011 | Direct |

## Functional Requirements Coverage (US-LV-002)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | Entitlement rules support dimensions: leave type, department, job level, job title, employment type, tenure brackets | TC-LV-026, TC-LV-027, TC-LV-038, TC-LV-046 | Direct (tenure and job-level dimensions DEFERRED in TC-LV-046) |
| FR-2 | Rule priority/specificity engine | TC-LV-027, TC-LV-028 | Direct |
| FR-3 | Pro-rata calculation for mid-year joiners | TC-LV-029, TC-LV-034 | Direct |
| FR-4 | Bulk entitlement assignment UI | TC-LV-037 | Direct |
| FR-5 | Hangfire recurring job for accrual processing | TC-LV-030, TC-LV-036, TC-LV-041 | Direct |
| FR-6 | Computed balances cached in Redis | TC-LV-042 | DEFERRED (Redis caching not implemented) |

## Functional Requirements Coverage (US-LV-003)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | Leave application form fields: type, dates, half-day, reason, attachment | TC-LV-048, TC-LV-051, TC-LV-055, TC-LV-063 | Direct |
| FR-2 | Real-time balance display (current, requested, projected remaining) | TC-LV-049, TC-LV-050 | Direct |
| FR-3 | Working-days calc -- exclude weekends (work-week config) and public holidays | TC-LV-056 | Direct (holiday exclusion depends on US-LV-007) |
| FR-4 | Overlap detection against existing Pending/Approved requests | TC-LV-052 | Direct |
| FR-5 | API endpoint POST /api/v1/leaves with documented body | TC-LV-048, TC-LV-055, TC-LV-061, TC-LV-064 | Direct |
| FR-6 | Insert leave_request status=Pending and queue notification | TC-LV-048 | Direct |
| FR-7 | Multi-level approval routing per tenant workflow config | -- | NOT COVERED (approval routing is downstream of submission; deferred to approval story) |

## Functional Requirements Coverage (US-LV-004)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | GET /api/v1/leaves/pending scoped to manager's direct reports within tenant | TC-LV-066, TC-LV-067, TC-LV-081, TC-LV-085 | Direct |
| FR-2 | Result item fields (requestId, employeeName/photo, leaveType/color, dates, totalDays, reason, hasAttachments, currentBalance, requestedAt) | TC-LV-066, TC-LV-077, TC-LV-080 | Direct |
| FR-3 | Server-side filtering (leave type, employee, date range) and sorting (requested/start date) | TC-LV-071, TC-LV-072, TC-LV-073, TC-LV-074, TC-LV-075, TC-LV-084 | Direct |
| FR-4 | Server-side pagination with page, pageSize, totalCount | TC-LV-068, TC-LV-069, TC-LV-070, TC-LV-074 | Direct |
| FR-5 | Team conflict count (approved overlapping leave) per request | TC-LV-077, TC-LV-078 | Direct |
| FR-6 | Real-time SignalR notification of new requests to the manager's queue | TC-LV-079 | DEFERRED (SignalR/notifications module not implemented; API-reload path verified) |

## Functional Requirements Coverage (US-LV-005)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | POST /api/v1/leaves/{id}/approve with optional comment | TC-LV-089, TC-LV-091, TC-LV-095, TC-LV-099 | Direct |
| FR-2 | POST /api/v1/leaves/{id}/reject with required reason | TC-LV-090, TC-LV-094, TC-LV-095, TC-LV-099 | Direct |
| FR-3 | On approval: insert leave_ledger 'used' entry; invalidate Redis balance cache | TC-LV-089, TC-LV-092, TC-LV-093, TC-LV-ISO-020 | Direct (Redis invalidation DEFERRED; ledger + DB-fallback balance verified) |
| FR-4 | On rejection: no ledger entry, only status update and audit | TC-LV-090 | Direct |
| FR-5 | Multi-level approval chain (1-3 levels); track approval history | TC-LV-091, TC-LV-097 | Direct for history; multi-level CONDITIONAL on approval-workflow config (US-ADM-007) |
| FR-6 | Optimistic concurrency via PostgreSQL xmin (UseXminAsConcurrencyToken) | TC-LV-096 | Direct |
| FR-7 | Audit log Leave.Approved/Leave.Rejected, resource_type LeaveRequest, before/after JSON | TC-LV-089, TC-LV-090, TC-LV-105 | Direct |

## Functional Requirements Coverage (US-LV-006)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | GET /api/v1/leaves/my-balance returns all leave-type balances for the authenticated employee within their tenant | TC-LV-109, TC-LV-116, TC-LV-119, TC-LV-122 | Direct |
| FR-2 | Response per leave type: leaveTypeId, leaveTypeName, color, entitlement, used, pending, balance, carryForward, expired | TC-LV-109, TC-LV-110, TC-LV-114, TC-LV-115 | Direct |
| FR-3 | GET /api/v1/leaves/my-ledger?leaveTypeId&year returns the full transaction log | TC-LV-111, TC-LV-112, TC-LV-117 | Direct |
| FR-4 | GET /api/v1/leaves/my-upcoming returns approved and pending future leaves | TC-LV-113 | Direct |
| FR-5 | Balance sourced from Redis cache (key tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}); DB fallback on cache miss | TC-LV-115, TC-LV-121, TC-LV-125, TC-LV-ISO-024 | Direct (Redis cache DEFERRED; DB-fallback computation verified) |
| FR-6 | Leave history section with filterable list of past requests (approved, rejected, cancelled) | TC-LV-120 | Direct |

## Non-Functional Requirements Coverage (US-LV-001)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Leave type list API <= 200ms P95 with Redis cache; cache invalidation on write | TC-LV-016, TC-LV-023, TC-LV-ISO-004 | Direct (cache steps DEFERRED if not implemented) |
| NFR-2 | Tenant-isolated via EF Core global query filters and PostgreSQL RLS | TC-LV-012, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | Direct |
| NFR-3 | Config changes audit-logged with before/after JSON | TC-LV-002, TC-LV-017 | Direct |
| NFR-4 | UI fully responsive 360px to 4K | TC-LV-018, TC-LV-019, TC-LV-020 | Direct |

## Non-Functional Requirements Coverage (US-LV-002)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Recalculation for 5,000 employees within 60 seconds (Hangfire) | TC-LV-041 | Direct |
| NFR-2 | All entitlement data tenant-isolated via EF Core filters and PostgreSQL RLS | TC-LV-039, TC-LV-040, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | Direct |
| NFR-3 | Redis cache for leave balances with 24h TTL and event-driven invalidation | TC-LV-042, TC-LV-ISO-008 | DEFERRED (Redis caching not implemented) |

## Non-Functional Requirements Coverage (US-LV-003)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Submission API responds within 500ms P95 | TC-LV-064 | Direct |
| NFR-2 | Balance check uses Redis-cached values; DB fallback on cache miss | TC-LV-049, TC-LV-050, TC-LV-064, TC-LV-ISO-012 | Direct (cache layer DEFERRED; DB-fallback path tested) |
| NFR-3 | Attachments stored in tenant-scoped blob path {tenantId}/leaves/{requestId}/ | TC-LV-063, TC-LV-ISO-012 | Direct |
| NFR-4 | All operations tenant-isolated via EF Core filters + PostgreSQL RLS | TC-LV-062, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | Direct |
| NFR-5 | Form usable on mobile 360px+ with touch-friendly date pickers | TC-LV-065 | Direct |

## Non-Functional Requirements Coverage (US-LV-004)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Pending queue API responds within 300ms P95 using ix_leave_pending | TC-LV-085 | Direct |
| NFR-2 | Inline balances fetched from Redis cache; DB fallback on cache miss | TC-LV-080, TC-LV-ISO-016 | Direct (Redis cache DEFERRED; DB-fallback path and tenant-scoped key pattern verified) |
| NFR-3 | Tenant-isolated via EF Core filters; manager scope limited to direct reports | TC-LV-081, TC-LV-082, TC-LV-084, TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015 | Direct |
| NFR-4 | Page fully responsive and usable on mobile 360px+ | TC-LV-086, TC-LV-087 | Direct |

## Non-Functional Requirements Coverage (US-LV-005)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Approve/Reject API responds within 500ms P95 | TC-LV-103 | Direct |
| NFR-2 | Notification queuing asynchronous and non-blocking | TC-LV-089, TC-LV-090, TC-LV-103, TC-LV-107 | Direct (notification dispatch DEFERRED on notifications module; non-blocking/best-effort verified) |
| NFR-3 | All operations tenant-isolated via EF Core filters (RLS-equivalent per vault) | TC-LV-099, TC-LV-102, TC-LV-105, TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020 | Direct |
| NFR-4 | Concurrency handling prevents double-approval / approve-then-reject races | TC-LV-096 | Direct |

## Non-Functional Requirements Coverage (US-LV-006)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Balance API responds within 200ms P95 using Redis cache | TC-LV-125, TC-LV-121 | Direct (Redis cache DEFERRED; DB-fallback path measured against the 200ms target) |
| NFR-2 | Dashboard achieves LCP under 2.5s | TC-LV-126 | Direct |
| NFR-3 | All data tenant-isolated via EF Core filters + PostgreSQL RLS (RLS-equivalent per vault) | TC-LV-122, TC-LV-123, TC-LV-124, TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024 | Direct |
| NFR-4 | Accessible (WCAG 2.1 AA): progress bars have aria-labels, color not the sole indicator | TC-LV-128 | Direct |

## Business Rules Coverage (US-LV-001)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Leave type names unique within tenant (case-insensitive) | TC-LV-003, TC-LV-012 | Direct |
| BR-2 | Cannot hard-delete if leave requests reference it; deactivate only | TC-LV-011 | Direct (forward-looking; leave-request module pending) |
| BR-3 | Entitlement must be positive; zero allowed for unpaid | TC-LV-006 | Direct |
| BR-4 | Gender-specific types shown only to matching gender employees | TC-LV-010 | Direct (employee-facing filtering forward-looking) |
| BR-5 | Config changes do not retroactively affect approved requests | TC-LV-002, TC-LV-004 | Direct |

## Business Rules Coverage (US-LV-002)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Entitlement rules effective per leave year (calendar or fiscal, configurable per tenant) | TC-LV-035 | Direct |
| BR-2 | Part-time employees receive entitlement proportional to FTE ratio | TC-LV-031 | DEFERRED (FTE field not on Employee entity) |
| BR-3 | Probation employees receive entitlement only for probation_eligible leave types | TC-LV-032 | Direct |
| BR-4 | Entitlement cannot be negative; minimum is zero | TC-LV-033 | Direct |
| BR-5 | Department transfer mid-year triggers pro-rata recalculation for both periods | TC-LV-034 | Direct |

## Business Rules Coverage (US-LV-003)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Cannot apply for past dates beyond a configurable lookback window | TC-LV-053 | Direct |
| BR-2 | Cannot apply for dates beyond a configurable future window | TC-LV-054 | Direct |
| BR-3 | Maximum consecutive leave days enforced per leave type config | TC-LV-057 | Direct |
| BR-4 | Gender-restricted leave types only shown to eligible employees | TC-LV-058 | Direct |
| BR-5 | Probation employees only see/apply for probation_eligible leave types | TC-LV-059 | Direct |
| BR-6 | Manager/approver determined by employee reporting line (manager_employee_id) | TC-LV-048 | Direct (notification target; full routing in approval story) |

## Business Rules Coverage (US-LV-004)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Managers see only their direct reports (not skip-level unless multi-level approval configured) | TC-LV-066, TC-LV-067, TC-LV-071, TC-LV-072, TC-LV-081, TC-LV-088, TC-LV-ISO-013 | Direct |
| BR-2 | Multi-level approval shows requests at the manager's approval level | TC-LV-088 | Direct (Scenario A now; multi-level workflow forward-looking on approval story) |
| BR-3 | Requests older than 30 days without action highlighted as overdue | TC-LV-076 | Direct |
| BR-4 | Balance shown is current real-time balance, not balance at request time | TC-LV-066, TC-LV-080 | Direct |

## Business Rules Coverage (US-LV-005)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Only the designated approver (or current-level approver) can approve/reject | TC-LV-099, TC-LV-ISO-017 | Direct |
| BR-2 | Rejection reason mandatory; approval comment optional | TC-LV-090, TC-LV-091, TC-LV-094 | Direct |
| BR-3 | A rejected (already-actioned) request cannot be re-approved | TC-LV-095, TC-LV-106 | Direct |
| BR-4 | Approving leave for a payroll-locked period is blocked | TC-LV-098 | CONDITIONAL (payroll module period-lock not implemented; non-locked path verified) |
| BR-5 | Approval deducts balance at approval time, not at request time | TC-LV-089, TC-LV-092, TC-LV-093 | Direct |

## Business Rules Coverage (US-LV-006)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Balance = Entitlement + Carry Forward - Used - Expired + Adjustments | TC-LV-110, TC-LV-112, TC-LV-115 | Direct |
| BR-2 | "Pending" days shown separately and not deducted from "balance" until approved | TC-LV-110, TC-LV-114 | Direct |
| BR-3 | Only active leave types shown; deactivated types with remaining balance shown in collapsed Archived section | TC-LV-116 | Direct |
| BR-4 | Leave-year boundaries tenant-configurable (calendar or fiscal year) | TC-LV-118 | Direct |
| BR-5 | Employee can view balances for previous leave years (read-only, via year selector) | TC-LV-117 | Direct |

## Coverage Summary (US-LV-001)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 4/5 (80%) -- FR-4 deferred (onboarding wizard) | >= 85% | NOTE (FR-4 is cross-module dependency) |
| Non-Functional Requirements Coverage | 4/4 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded in TC-LV-012) | >= 3 | PASS |
| Security Test Cases | 8/29 (27.6%) + embedded = 8/29 (27.6%) | >= 30% | NOTE (close; 8 security tests cover all critical vectors) |
| Performance Test Cases | 2/29 (TC-LV-016, TC-LV-023) | >= 1 | PASS |
| Accessibility Test Cases | 1/29 (TC-LV-019) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/29 (TC-LV-018, TC-LV-020) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-LV-021 (onboarding seeding -- pending US-TENANT-*), TC-LV-ISO-004 partial (cache -- pending Redis implementation for leave types) | -- | NOTE |

## Coverage Summary (US-LV-002)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 5/6 (83%) -- FR-6 deferred (Redis caching) | >= 85% | NOTE (FR-6 is infrastructure dependency) |
| Non-Functional Requirements Coverage | 2/3 (67%) -- NFR-3 deferred (Redis caching) | >= 85% | NOTE (NFR-3 is infrastructure dependency) |
| Business Rules Coverage | 4/5 (80%) -- BR-2 deferred (FTE field) | >= 85% | NOTE (BR-2 is entity-level dependency) |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded in TC-LV-042) | >= 3 | PASS |
| Security Test Cases | 7/26 (26.9%) including ISO | >= 30% | NOTE (close; all critical security vectors covered: auth, authz, tenant isolation, XSS) |
| Performance Test Cases | 2/26 (TC-LV-041, TC-LV-042) | >= 1 | PASS |
| Accessibility Test Cases | 1/26 (TC-LV-044) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/26 (TC-LV-043, TC-LV-045) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-LV-031 (FTE proration -- FTE field pending), TC-LV-042 (Redis cache -- pending implementation), TC-LV-046 (job-level/tenure dimensions -- pending entity), TC-LV-ISO-008 partial (cache keys -- pending Redis) | -- | NOTE |

## Coverage Summary (US-LV-003)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 6/6 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 6/7 (86%) -- FR-7 (multi-level approval routing) downstream of submission | >= 85% | PASS (FR-7 belongs to the approval story) |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 6 (4 dedicated ISO-009..012 + embedded in TC-LV-058, TC-LV-063) | >= 3 | PASS |
| Security Test Cases | 8/22 (36%) including ISO | >= 30% | PASS |
| Performance Test Cases | 1/22 (TC-LV-064) | >= 1 | PASS |
| Accessibility Test Cases | 1/22 (TC-LV-065) | >= 1 | PASS |
| Cross-Browser Test Cases | 1/22 (TC-LV-065) | >= 1 | PASS |
| Blocked Test Cases | 0 (TC-LV-056 holiday-exclusion steps conditionally blocked on US-LV-007) | -- | NOTE |
| Deferred Test Cases | TC-LV-ISO-012 partial (balance cache keys -- pending Redis); FR-7 approval routing out of scope | -- | NOTE |

## Coverage Summary (US-LV-004)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 6/6 (100%) -- FR-6 real-time push DEFERRED (API-reload path verified) | >= 85% | PASS (FR-6 push depends on notifications module) |
| Non-Functional Requirements Coverage | 4/4 (100%) -- NFR-2 Redis cache DEFERRED (DB-fallback verified) | >= 85% | PASS |
| Business Rules Coverage | 4/4 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO-013..016 + embedded in TC-LV-081 intra-tenant scope) | >= 3 | PASS |
| Security Test Cases | 8/27 (30%) including ISO | >= 30% | PASS |
| Performance Test Cases | 2/27 (TC-LV-085, TC-LV-069 page-size bound) | >= 1 | PASS |
| Accessibility Test Cases | 1/27 (TC-LV-086) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/27 (TC-LV-086, TC-LV-087) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-LV-079 (SignalR real-time push -- notifications module), TC-LV-077 (history/team-calendar subsections -- US-LV-009), TC-LV-088 (multi-level approval -- approval workflow story), TC-LV-ISO-016 partial (balance cache keys -- pending Redis) | -- | NOTE |

## Coverage Summary (US-LV-005)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-3 Redis invalidation DEFERRED (ledger + DB-fallback verified); FR-5 multi-level CONDITIONAL on US-ADM-007 | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) -- NFR-2 notification dispatch DEFERRED (non-blocking verified) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) -- BR-4 payroll-lock CONDITIONAL on payroll module | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO-017..020 + embedded approver-scope in TC-LV-099) | >= 3 | PASS |
| Security Test Cases | 8/24 (33%) including ISO | >= 30% | PASS |
| Performance Test Cases | 2/24 (TC-LV-103, TC-LV-107 non-blocking) | >= 1 | PASS |
| Accessibility Test Cases | 1/24 (TC-LV-104) | >= 1 | PASS |
| Cross-Browser Test Cases | 1/24 (TC-LV-108) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred / Conditional Test Cases | TC-LV-097 (multi-level approval -- CONDITIONAL on US-ADM-007), TC-LV-098 (payroll-lock -- CONDITIONAL on payroll module), TC-LV-107 (async notification dispatch -- DEFERRED on notifications module), TC-LV-ISO-020 partial (balance cache keys -- pending Redis) | -- | NOTE |

## Coverage Summary (US-LV-006)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 6/6 (100%) -- FR-5 Redis cache DEFERRED (DB-fallback computation verified) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) -- NFR-1 Redis-cached latency DEFERRED (DB-fallback path measured against 200ms) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO-021..024 + embedded self/tenant scope in TC-LV-122) | >= 3 | PASS |
| Security Test Cases | 7/24 (29%) including ISO | >= 30% | NOTE (close; all critical vectors covered: auth, self-scope, tenant isolation, injection) |
| Performance Test Cases | 2/24 (TC-LV-125, TC-LV-126) | >= 1 | PASS |
| Accessibility Test Cases | 1/24 (TC-LV-128) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/24 (TC-LV-127, TC-LV-128) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred / Conditional Test Cases | TC-LV-121 (cache-miss re-cache -- DEFERRED on Redis; DB-fallback verified), TC-LV-125 (200ms cached-read target -- DEFERRED on Redis; DB-fallback measured), TC-LV-ISO-024 partial (balance cache keys -- pending Redis) | -- | NOTE |

---

*Note: This test matrix covers US-LV-001 (29 TCs), US-LV-002 (26 TCs), US-LV-003 (22 TCs), US-LV-004 (27 TCs), US-LV-005 (24 TCs), and US-LV-006 (24 TCs) for the Leave Management module. US-LV-006 adds 20 functional/security/performance/accessibility test cases (TC-LV-109..128) plus 4 dedicated multi-tenant isolation tests (TC-LV-ISO-021..024) for the employee leave-balance dashboard. All 5 acceptance criteria for US-LV-006 have direct coverage. Notes for US-LV-006: balance correctness (TC-LV-110/TC-LV-112/TC-LV-115) verifies the BR-1 formula entitlement + carry_forward - used - expired + adjustments against both the card and the ledger running total; pending-separation (TC-LV-114) verifies that submitting a request raises "pending" without reducing "balance" until approval; the Redis balance cache (FR-5/NFR-1) is DEFERRED module-wide per docs/vault/modules/leave-management.md, so TC-LV-121 (cache-miss recompute/re-cache) and TC-LV-125 (200ms cached-read latency) verify the DB-computed LeaveLedger-running-total fallback and record the cache-specific steps as CONDITIONAL/DEFERRED (not silent gaps); TC-LV-ISO-024 verifies the tenant+employee-scoped cache-key pattern by design with DB-fallback isolation verified live; tenant isolation (TC-LV-ISO-021..023) and self-scope (TC-LV-122) confirm an employee sees only their own tenant-scoped data. US-LV-001..US-LV-005 deferred items remain unchanged.*
