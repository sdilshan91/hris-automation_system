---
module: Leave Management
total_user_stories: 10
total_test_cases: 249
created: 2026-06-13
updated: 2026-06-14
status: draft
---

# Leave Management -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 10 (US-LV-001, US-LV-002, US-LV-003, US-LV-004, US-LV-005, US-LV-006, US-LV-007, US-LV-008, US-LV-009, US-LV-010) |
| Total Test Cases | 249 |
| Critical Priority | 111 |
| High Priority | 121 |
| Medium Priority | 16 |
| Low Priority | 0 |
| Blocked Test Cases | 0 (TC-LV-056 holiday-exclusion now realized by US-LV-007 TC-LV-131) |
| Deferred Test Cases | 5+ (TC-LV-021 onboarding seeding, TC-LV-ISO-004 cache -- partial, TC-LV-031 FTE, TC-LV-042 Redis cache, TC-LV-046 job-level dimension, TC-LV-ISO-008/TC-LV-ISO-012/TC-LV-ISO-016 balance cache keys -- partial; US-LV-004: TC-LV-077 history/team-calendar subsections, TC-LV-079 SignalR real-time push, TC-LV-088 multi-level approval -- conditional on downstream modules; US-LV-005: TC-LV-097 multi-level approval -- conditional on US-ADM-007, TC-LV-098 payroll-lock -- conditional on payroll module, TC-LV-107 async notification -- DEFERRED on notifications module, TC-LV-ISO-020 balance cache keys -- partial pending Redis; US-LV-006: TC-LV-121/TC-LV-125 Redis balance cache -- DEFERRED (DB-fallback verified), TC-LV-ISO-024 balance cache keys -- partial pending Redis; US-LV-007: TC-LV-140 payroll-period delete-lock -- CONDITIONAL on payroll module, TC-LV-144 onboarding seeding -- DEFERRED (wizard UNWIRED), TC-LV-147 holiday-list 200ms cached read -- DEFERRED on Redis (DB-fallback measured), TC-LV-ISO-028 holiday cache keys -- partial pending Redis; US-LV-008: TC-LV-151 carry-forward-expiry Redis invalidation -- DEFERRED (DB/ledger verified), TC-LV-154 encashment-on-expiry -- CONDITIONAL on leave-type config, TC-LV-167 fiscal-year boundary -- CONDITIONAL on tenant fiscal-year config (calendar-year verified), TC-LV-ISO-032 balance cache keys -- partial pending Redis; US-LV-009: TC-LV-179 holiday-background highlight -- depends on US-LV-007 (implemented), TC-LV-186 month-range 300ms cached read -- DEFERRED on Redis (DB-fallback measured), TC-LV-ISO-036 calendar cache keys -- partial pending Redis; US-LV-010: TC-LV-190 Redis balance-cache invalidation on cancel -- DEFERRED (reversal ledger/balance verified), TC-LV-194 payroll-lock block -- CONDITIONAL on payroll module (non-locked path verified), TC-LV-202 carry-forward-pool restoration -- CONDITIONAL (general adjusted reversal recorded if pool-split not tracked), TC-LV-203 N-day cancellation window -- CONDITIONAL on tenant-settings (default anytime-before-start verified), TC-LV-ISO-040 balance cache keys on cancel -- partial pending Redis) |
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
| US-LV-007 | Holiday Calendar Management Per Tenant | TC-LV-129, TC-LV-130, TC-LV-131, TC-LV-132, TC-LV-133, TC-LV-134, TC-LV-135, TC-LV-136, TC-LV-137, TC-LV-138, TC-LV-139, TC-LV-140, TC-LV-141, TC-LV-142, TC-LV-143, TC-LV-144, TC-LV-145, TC-LV-146, TC-LV-147, TC-LV-148 | 20 |
| Cross-cutting (LV-007) | Multi-tenant isolation (mandatory) | TC-LV-ISO-025, TC-LV-ISO-026, TC-LV-ISO-027, TC-LV-ISO-028 | 4 |
| US-LV-008 | Leave Carry-Forward and Expiry Rules | TC-LV-149, TC-LV-150, TC-LV-151, TC-LV-152, TC-LV-153, TC-LV-154, TC-LV-155, TC-LV-156, TC-LV-157, TC-LV-158, TC-LV-159, TC-LV-160, TC-LV-161, TC-LV-162, TC-LV-163, TC-LV-164, TC-LV-165, TC-LV-166, TC-LV-167, TC-LV-168 | 20 |
| Cross-cutting (LV-008) | Multi-tenant isolation (mandatory) | TC-LV-ISO-029, TC-LV-ISO-030, TC-LV-ISO-031, TC-LV-ISO-032 | 4 |
| US-LV-009 | Team Leave Calendar View | TC-LV-169, TC-LV-170, TC-LV-171, TC-LV-172, TC-LV-173, TC-LV-174, TC-LV-175, TC-LV-176, TC-LV-177, TC-LV-178, TC-LV-179, TC-LV-180, TC-LV-181, TC-LV-182, TC-LV-183, TC-LV-184, TC-LV-185, TC-LV-186, TC-LV-187, TC-LV-188 | 20 |
| Cross-cutting (LV-009) | Multi-tenant isolation (mandatory) | TC-LV-ISO-033, TC-LV-ISO-034, TC-LV-ISO-035, TC-LV-ISO-036 | 4 |
| US-LV-010 | Leave Cancellation by Employee | TC-LV-189, TC-LV-190, TC-LV-191, TC-LV-192, TC-LV-193, TC-LV-194, TC-LV-195, TC-LV-196, TC-LV-197, TC-LV-198, TC-LV-199, TC-LV-200, TC-LV-201, TC-LV-202, TC-LV-203, TC-LV-204, TC-LV-205, TC-LV-206, TC-LV-207, TC-LV-208, TC-LV-209 | 21 |
| Cross-cutting (LV-010) | Multi-tenant isolation (mandatory) | TC-LV-ISO-037, TC-LV-ISO-038, TC-LV-ISO-039, TC-LV-ISO-040 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (LV-001) | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-009, TC-LV-010, TC-LV-011, TC-LV-017, TC-LV-018, TC-LV-021, TC-LV-022, TC-LV-024, TC-LV-025 | 17 |
| Functional (LV-002) | TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-033, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-038, TC-LV-043, TC-LV-045, TC-LV-046 | 16 |
| Functional (LV-003) | TC-LV-048, TC-LV-049, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-055, TC-LV-056, TC-LV-057, TC-LV-059, TC-LV-063 | 12 |
| Functional (LV-004) | TC-LV-066, TC-LV-067, TC-LV-068, TC-LV-069, TC-LV-070, TC-LV-071, TC-LV-072, TC-LV-073, TC-LV-074, TC-LV-075, TC-LV-076, TC-LV-077, TC-LV-078, TC-LV-079, TC-LV-080, TC-LV-087, TC-LV-088 | 17 |
| Functional (LV-005) | TC-LV-089, TC-LV-090, TC-LV-091, TC-LV-092, TC-LV-093, TC-LV-094, TC-LV-095, TC-LV-096, TC-LV-097, TC-LV-098, TC-LV-105, TC-LV-106, TC-LV-107, TC-LV-108 | 14 |
| Functional (LV-006) | TC-LV-109, TC-LV-110, TC-LV-111, TC-LV-112, TC-LV-113, TC-LV-114, TC-LV-115, TC-LV-116, TC-LV-117, TC-LV-118, TC-LV-119, TC-LV-120, TC-LV-121, TC-LV-127 | 14 |
| Functional (LV-007) | TC-LV-129, TC-LV-130, TC-LV-131, TC-LV-132, TC-LV-133, TC-LV-134, TC-LV-135, TC-LV-137, TC-LV-138, TC-LV-139, TC-LV-140, TC-LV-141, TC-LV-142, TC-LV-143, TC-LV-144 | 15 |
| Functional/Integration (LV-008) | TC-LV-149, TC-LV-150, TC-LV-151, TC-LV-152, TC-LV-153, TC-LV-154, TC-LV-155, TC-LV-156, TC-LV-157, TC-LV-158, TC-LV-166, TC-LV-167, TC-LV-168 | 13 |
| Functional/Integration (LV-009) | TC-LV-169, TC-LV-170, TC-LV-171, TC-LV-173, TC-LV-174, TC-LV-177, TC-LV-178, TC-LV-179, TC-LV-180, TC-LV-181, TC-LV-182 | 11 |
| Functional (LV-010) | TC-LV-189, TC-LV-190, TC-LV-191, TC-LV-192, TC-LV-193, TC-LV-194, TC-LV-195, TC-LV-196, TC-LV-197, TC-LV-198, TC-LV-201, TC-LV-202, TC-LV-203, TC-LV-204, TC-LV-205 | 15 |
| Security (LV-001) | TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 8 |
| Security (LV-002) | TC-LV-039, TC-LV-040, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 7 |
| Security (LV-003) | TC-LV-058, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | 8 |
| Security (LV-004) | TC-LV-081, TC-LV-082, TC-LV-083, TC-LV-084, TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015, TC-LV-ISO-016 | 8 |
| Security (LV-005) | TC-LV-099, TC-LV-100, TC-LV-101, TC-LV-102, TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020 | 8 |
| Security (LV-006) | TC-LV-122, TC-LV-123, TC-LV-124, TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024 | 7 |
| Security (LV-007) | TC-LV-145, TC-LV-146, TC-LV-ISO-025, TC-LV-ISO-026, TC-LV-ISO-027, TC-LV-ISO-028 | 6 |
| Security (LV-008) | TC-LV-161, TC-LV-162, TC-LV-163, TC-LV-ISO-029, TC-LV-ISO-030, TC-LV-ISO-031, TC-LV-ISO-032 | 7 |
| Security (LV-009) | TC-LV-172, TC-LV-175, TC-LV-176, TC-LV-183, TC-LV-184, TC-LV-185, TC-LV-ISO-033, TC-LV-ISO-034, TC-LV-ISO-035, TC-LV-ISO-036 | 10 |
| Security (LV-010) | TC-LV-199, TC-LV-200, TC-LV-206, TC-LV-207, TC-LV-ISO-037, TC-LV-ISO-038, TC-LV-ISO-039, TC-LV-ISO-040 | 8 |
| Performance (LV-001) | TC-LV-016, TC-LV-023 | 2 |
| Performance (LV-002) | TC-LV-041, TC-LV-042 | 2 |
| Performance (LV-003) | TC-LV-064 | 1 |
| Performance (LV-004) | TC-LV-085 (TC-LV-069 page-size bound embedded) | 1 |
| Performance (LV-005) | TC-LV-103 (TC-LV-107 non-blocking-notification embedded) | 1 |
| Performance (LV-006) | TC-LV-125, TC-LV-126 | 2 |
| Performance (LV-007) | TC-LV-136, TC-LV-147 | 2 |
| Performance (LV-008) | TC-LV-159 | 1 |
| Performance (LV-009) | TC-LV-186, TC-LV-187 | 2 |
| Performance (LV-010) | TC-LV-208 | 1 |
| Accessibility (LV-001) | TC-LV-019 | 1 |
| Accessibility (LV-002) | TC-LV-044 | 1 |
| Accessibility (LV-003) | TC-LV-065 | 1 |
| Accessibility (LV-004) | TC-LV-086 | 1 |
| Accessibility (LV-005) | TC-LV-104 | 1 |
| Accessibility (LV-006) | TC-LV-128 | 1 |
| Accessibility (LV-007) | TC-LV-148 | 1 |
| Accessibility (LV-008) | TC-LV-164 | 1 |
| Accessibility (LV-009) | TC-LV-188 | 1 |
| Accessibility (LV-010) | TC-LV-209 | 1 |
| Cross-Browser (LV-001) | TC-LV-020 | 1 |
| Cross-Browser (LV-002) | TC-LV-045 | 1 |
| Cross-Browser (LV-003) | TC-LV-065 | 1 |
| Cross-Browser (LV-004) | TC-LV-086, TC-LV-087 | 2 |
| Cross-Browser (LV-005) | TC-LV-108 | 1 |
| Cross-Browser (LV-006) | TC-LV-127, TC-LV-128 | 2 |
| Cross-Browser (LV-007) | TC-LV-148 | 1 |
| Cross-Browser (LV-009) | TC-LV-174, TC-LV-188 | 2 |
| Cross-Browser (LV-010) | TC-LV-209 | 1 |

## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-LV-001, TC-LV-002, TC-LV-004, TC-LV-005, TC-LV-009, TC-LV-010, TC-LV-012, TC-LV-017, TC-LV-021, TC-LV-024, TC-LV-025, TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-046, TC-LV-048, TC-LV-049, TC-LV-055, TC-LV-056, TC-LV-066, TC-LV-071, TC-LV-072, TC-LV-073, TC-LV-075, TC-LV-077, TC-LV-078, TC-LV-079, TC-LV-080, TC-LV-089, TC-LV-090, TC-LV-091, TC-LV-105, TC-LV-109, TC-LV-110, TC-LV-111, TC-LV-113, TC-LV-114, TC-LV-116, TC-LV-117, TC-LV-120, TC-LV-121, TC-LV-129, TC-LV-130, TC-LV-134, TC-LV-137, TC-LV-141, TC-LV-143, TC-LV-144 | 56 |
| Negative Test | TC-LV-003, TC-LV-006, TC-LV-007, TC-LV-011, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-022, TC-LV-025, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-032, TC-LV-033, TC-LV-038, TC-LV-039, TC-LV-040, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-057, TC-LV-058, TC-LV-059, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-063, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012, TC-LV-067, TC-LV-070, TC-LV-074, TC-LV-081, TC-LV-082, TC-LV-083, TC-LV-084, TC-LV-088, TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015, TC-LV-ISO-016, TC-LV-092, TC-LV-093, TC-LV-094, TC-LV-095, TC-LV-096, TC-LV-097, TC-LV-098, TC-LV-099, TC-LV-100, TC-LV-101, TC-LV-102, TC-LV-106, TC-LV-107, TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020, TC-LV-119, TC-LV-122, TC-LV-123, TC-LV-124, TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024, TC-LV-132, TC-LV-133, TC-LV-135, TC-LV-138, TC-LV-139, TC-LV-140, TC-LV-142, TC-LV-145, TC-LV-146, TC-LV-ISO-025, TC-LV-ISO-026, TC-LV-ISO-027, TC-LV-ISO-028 | 89 |
| Boundary Test | TC-LV-006, TC-LV-008, TC-LV-009, TC-LV-029, TC-LV-031, TC-LV-033, TC-LV-038, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-055, TC-LV-056, TC-LV-057, TC-LV-063, TC-LV-067, TC-LV-068, TC-LV-069, TC-LV-070, TC-LV-073, TC-LV-074, TC-LV-076, TC-LV-078, TC-LV-080, TC-LV-088, TC-LV-091, TC-LV-092, TC-LV-093, TC-LV-096, TC-LV-110, TC-LV-112, TC-LV-113, TC-LV-115, TC-LV-116, TC-LV-117, TC-LV-118, TC-LV-119, TC-LV-120, TC-LV-124, TC-LV-127, TC-LV-131, TC-LV-132, TC-LV-133, TC-LV-135, TC-LV-136, TC-LV-138, TC-LV-139, TC-LV-140, TC-LV-141, TC-LV-142, TC-LV-143 | 52 |
| Security Test | TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-039, TC-LV-040, TC-LV-042, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008, TC-LV-058, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-063, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012, TC-LV-081, TC-LV-082, TC-LV-083, TC-LV-084, TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015, TC-LV-ISO-016, TC-LV-096, TC-LV-099, TC-LV-100, TC-LV-101, TC-LV-102, TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020, TC-LV-122, TC-LV-123, TC-LV-124, TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024, TC-LV-145, TC-LV-146, TC-LV-ISO-025, TC-LV-ISO-026, TC-LV-ISO-027, TC-LV-ISO-028 | 55 |
| Multi-Tenant Isolation | TC-LV-012, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-042, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012, TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015, TC-LV-ISO-016, TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020, TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024, TC-LV-ISO-025, TC-LV-ISO-026, TC-LV-ISO-027, TC-LV-ISO-028 | 30 |
| Performance Test | TC-LV-016, TC-LV-023, TC-LV-041, TC-LV-042, TC-LV-064, TC-LV-069, TC-LV-085, TC-LV-103, TC-LV-107, TC-LV-125, TC-LV-126, TC-LV-136, TC-LV-147 | 13 |
| Accessibility Test | TC-LV-019, TC-LV-044, TC-LV-065, TC-LV-086, TC-LV-104, TC-LV-128, TC-LV-148 | 7 |
| Cross-Browser Test | TC-LV-018, TC-LV-020, TC-LV-043, TC-LV-045, TC-LV-065, TC-LV-086, TC-LV-087, TC-LV-108, TC-LV-127, TC-LV-128, TC-LV-148 | 11 |

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

## Acceptance Criteria Coverage (US-LV-007)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Add holiday (name, date, type, locations) -> saved, tenant-scoped, visible to employees (location-filtered) | TC-LV-129, TC-LV-130, TC-LV-143 |
| AC-2 | Holiday excludes that date from leave-day count (Mon-Fri spanning a Wed holiday = 4 days) | TC-LV-131, TC-LV-132, TC-LV-133 |
| AC-3 | CSV import -- valid rows created; duplicate dates flagged for review | TC-LV-134, TC-LV-135, TC-LV-136 |
| AC-4 | Dual view -- color-coded month/year calendar + list view | TC-LV-137, TC-LV-148 |

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

## Functional Requirements Coverage (US-LV-007)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | CRUD operations for holidays scoped to tenant_id | TC-LV-129, TC-LV-130, TC-LV-138, TC-LV-143 | Direct |
| FR-2 | Holiday fields (name, date, type, location_id, description, is_recurring) | TC-LV-129, TC-LV-137, TC-LV-142, TC-LV-143 | Direct |
| FR-3 | Recurring holidays auto-generate next year via Hangfire job | TC-LV-141 | Direct |
| FR-4 | CSV import endpoint POST /api/v1/holidays/import | TC-LV-134, TC-LV-135, TC-LV-136 | Direct |
| FR-5 | Holiday seeding during tenant onboarding (Step 4) with country template | TC-LV-144 | DEFERRED (onboarding wizard call site UNWIRED; seeding service + idempotency verified) |
| FR-6 | Integration with leave day calculation (GET /api/v1/holidays?from&to) | TC-LV-131, TC-LV-132, TC-LV-133, TC-LV-147 | Direct |

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

## Non-Functional Requirements Coverage (US-LV-007)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Holiday list API for a year within 200ms P95; Redis cache with invalidation on write | TC-LV-147 | Direct (Redis cache DEFERRED; DB-fallback path measured against 200ms via ix_holiday_tenant_id_date) |
| NFR-2 | All holiday data tenant-isolated via EF Core filters + PostgreSQL RLS (RLS-equivalent per vault) | TC-LV-145, TC-LV-146, TC-LV-ISO-025, TC-LV-ISO-026, TC-LV-ISO-027, TC-LV-ISO-028 | Direct |
| NFR-3 | CSV import handles up to 100 rows within 5 seconds | TC-LV-136 | Direct |
| NFR-4 | Calendar view responsive and functional on mobile | TC-LV-148 | Direct |

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

## Business Rules Coverage (US-LV-007)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Holiday dates unique within tenant + location (no duplicate same date/location) | TC-LV-135, TC-LV-138, TC-LV-139 | Direct |
| BR-2 | Public applies to all (tenant/location); restricted may require apply | TC-LV-132, TC-LV-133, TC-LV-142 | Direct |
| BR-3 | Optional holidays may count against an optional-holiday leave type if configured | TC-LV-142 | Direct (optional-leave-type linkage CONDITIONAL on tenant config) |
| BR-4 | Holidays in a finalized payroll period cannot be deleted, only deactivated | TC-LV-140, TC-LV-143 | Direct for deactivate-retention; payroll-period delete-lock CONDITIONAL on payroll module |
| BR-5 | Recurring holidays auto-generate for next year 30 days before year-end (Hangfire) | TC-LV-141 | Direct |

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

## Coverage Summary (US-LV-007)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 4/4 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 6/6 (100%) -- FR-5 onboarding-seeding trigger DEFERRED (wizard UNWIRED; seeding service verified) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) -- NFR-1 Redis-cached latency DEFERRED (DB-fallback measured against 200ms) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) -- BR-4 payroll-period delete-lock CONDITIONAL on payroll module | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 6 (4 dedicated ISO-025..028 + embedded tenant/authz scope in TC-LV-145, TC-LV-146) | >= 3 | PASS |
| Security Test Cases | 6/24 (25%) including ISO | >= 30% | NOTE (close; all critical vectors covered: authz, tenant isolation, injection/CSV) |
| Performance Test Cases | 2/24 (TC-LV-136, TC-LV-147) | >= 1 | PASS |
| Accessibility Test Cases | 1/24 (TC-LV-148) | >= 1 | PASS |
| Cross-Browser Test Cases | 1/24 (TC-LV-148) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred / Conditional Test Cases | TC-LV-140 (payroll-period delete-lock -- CONDITIONAL on payroll module; deactivate-retention verified), TC-LV-144 (onboarding seeding -- DEFERRED, wizard UNWIRED; service verified), TC-LV-147 (200ms cached-read target -- DEFERRED on Redis; DB-fallback measured), TC-LV-ISO-028 partial (holiday cache keys -- pending Redis) | -- | NOTE |

## Coverage Summary (US-LV-008)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-7 Redis invalidation DEFERRED (DB/ledger verified) | >= 85% | PASS |
| Non-Functional Requirements Coverage | covered (Redis-cached latency DEFERRED; DB-fallback verified) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) -- fiscal-year boundary CONDITIONAL on tenant fiscal-year config (calendar-year verified) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO-029..032 + embedded tenant scope in TC-LV-161) | >= 3 | PASS |
| Security Test Cases | 7/24 (TC-LV-161..163, TC-LV-ISO-029..032) | >= 30% | NOTE (close; all critical vectors covered: authz, tenant isolation, injection) |
| Performance Test Cases | 1/24 (TC-LV-159) | >= 1 | PASS |
| Accessibility Test Cases | 1/24 (TC-LV-164) | >= 1 | PASS |
| Cross-Browser Test Cases | embedded in TC-LV-164/preview UI | >= 1 | NOTE |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred / Conditional Test Cases | TC-LV-151 (carry-forward-expiry Redis invalidation -- DEFERRED; DB/ledger verified), TC-LV-154 (encashment-on-expiry -- CONDITIONAL on leave-type config), TC-LV-167 (fiscal-year boundary -- CONDITIONAL on tenant fiscal-year config; calendar-year verified), TC-LV-ISO-032 partial (balance cache keys -- pending Redis) | -- | NOTE |

## Acceptance Criteria Coverage (US-LV-009)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Manager month view shows direct reports' approved + pending leaves as colored blocks | TC-LV-169, TC-LV-170, TC-LV-178, TC-LV-181 |
| AC-2 | Employee view shows only approved department leaves, no pending, no leave-type ("on leave") | TC-LV-171, TC-LV-172, TC-LV-185 |
| AC-3 | Manager week view -- Gantt-like, employees on Y-axis, days on X-axis | TC-LV-173 |
| AC-4 | Mobile 360px -- compact list grouped by date (employee, leave type, status) | TC-LV-174, TC-LV-188 |

## Functional Requirements Coverage (US-LV-009)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | GET /api/v1/leaves/team-calendar?from&to scoped to user's team/department | TC-LV-169, TC-LV-181, TC-LV-182, TC-LV-ISO-034 | Direct |
| FR-2 | Manager view shows both approved and pending leaves for direct reports | TC-LV-169, TC-LV-175, TC-LV-176 | Direct |
| FR-3 | Employee view shows only approved department leaves (no pending) | TC-LV-171, TC-LV-172, TC-LV-185 | Direct |
| FR-4 | Response fields: employeeId, employeeName, leaveTypeName, color, startDate, endDate, status, totalDays | TC-LV-170, TC-LV-181, TC-LV-178 | Direct (employee subset suppressed per BR-1) |
| FR-5 | Views supported: month, week, list | TC-LV-169, TC-LV-173, TC-LV-174 | Direct |
| FR-6 | Filter by employee, leave type, status (status manager-only) | TC-LV-180, TC-LV-175 | Direct |
| FR-7 | Public holidays displayed as background highlights | TC-LV-179 | Direct (depends on US-LV-007, implemented) |

## Non-Functional Requirements Coverage (US-LV-009)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Calendar API for a month range within 300ms P95 | TC-LV-186 | Direct (Redis cache DEFERRED module-wide; DB-backed path measured against 300ms) |
| NFR-2 | Tenant-isolated via EF Core filters (RLS-equivalent per vault) | TC-LV-ISO-033, TC-LV-ISO-034, TC-LV-ISO-035, TC-LV-ISO-036 | Direct |
| NFR-3 | Employee/manager/HR access control (department-approved vs direct-report+pending vs org-wide) | TC-LV-171, TC-LV-172, TC-LV-175, TC-LV-176, TC-LV-183, TC-LV-185 | Direct |
| NFR-4 | Renders smoothly with up to 50 employees and 200 entries | TC-LV-174, TC-LV-187, TC-LV-188 | Direct |

## Business Rules Coverage (US-LV-009)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Employees see only approved department leaves; no pending, no leave types (just "on leave") | TC-LV-171, TC-LV-172, TC-LV-180, TC-LV-181, TC-LV-185 | Direct (KEY data-leak prevention) |
| BR-2 | Managers see full details for their direct reports only | TC-LV-169, TC-LV-175, TC-LV-185 | Direct |
| BR-3 | HR Officers with Leave.ViewAll see the entire organization's calendar | TC-LV-176 | Direct |
| BR-4 | Cancelled leaves are not shown | TC-LV-177 | Direct |
| BR-5 | Half-day leaves are visually differentiated | TC-LV-178, TC-LV-181 | Direct |

## Coverage Summary (US-LV-009)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 4/4 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-7 holiday-highlight depends on US-LV-007 (implemented) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) -- NFR-1 Redis-cached latency DEFERRED (DB-backed path measured against 300ms) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 6 (4 dedicated ISO-033..036 + embedded tenant scope in TC-LV-176, TC-LV-183) | >= 3 | PASS |
| Security Test Cases | 10/24 (42%) including ISO (TC-LV-172, TC-LV-175, TC-LV-176, TC-LV-183, TC-LV-184, TC-LV-185, TC-LV-ISO-033..036) | >= 30% | PASS |
| Performance Test Cases | 2/24 (TC-LV-186, TC-LV-187) | >= 1 | PASS |
| Accessibility Test Cases | 1/24 (TC-LV-188) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/24 (TC-LV-174, TC-LV-188) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred / Conditional Test Cases | TC-LV-179 (holiday-background highlight -- depends on US-LV-007, implemented), TC-LV-186 (300ms cached-read target -- DEFERRED on Redis; DB-fallback measured), TC-LV-ISO-036 partial (calendar cache keys -- pending Redis) | -- | NOTE |

## Acceptance Criteria Coverage (US-LV-010)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Cancel a PENDING request -> Cancelled, no ledger entry, manager notification, audit log | TC-LV-189, TC-LV-198, TC-LV-204 |
| AC-2 | Cancel an APPROVED future request with reason -> Cancelled, reversal `adjusted` (positive) ledger restores balance, Redis invalidated, notification, audit | TC-LV-190, TC-LV-191, TC-LV-204 |
| AC-3 | Cancel an approved leave already started/passed -> blocked "Cannot cancel leave that has already started..." | TC-LV-192, TC-LV-193, TC-LV-203 |
| AC-4 | Cancel a leave in a payroll-locked period -> blocked | TC-LV-194 (CONDITIONAL on payroll module; non-locked path verified) |

## Functional Requirements Coverage (US-LV-010)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | API POST /api/v1/leaves/{id}/cancel with required `reason` body | TC-LV-197, TC-LV-205, TC-LV-189, TC-LV-190 | Direct |
| FR-2 | Pending: status -> Cancelled; no ledger entry | TC-LV-189, TC-LV-198, TC-LV-205 | Direct |
| FR-3 | Approved: status -> Cancelled; reversal `leave_ledger` entry (type `adjusted`, positive) restores balance | TC-LV-190, TC-LV-191, TC-LV-196, TC-LV-202 | Direct |
| FR-4 | Redis cache invalidation for `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` | TC-LV-190, TC-LV-ISO-040 | Direct (Redis DEFERRED module-wide; reversal-ledger/DB-fallback balance verified) |
| FR-5 | Notification queued to manager for both pending and approved cancellations | TC-LV-189, TC-LV-190 | Direct (notification dispatch DEFERRED on notifications module; non-blocking seam verified) |
| FR-6 | Cancellation recorded in `leave_approval_history` (action = Cancelled, actor = employee) | TC-LV-189, TC-LV-198, TC-LV-204, TC-LV-205 | Direct |
| FR-7 | Tenant-configurable policy: allow cancellation up to N days before start (default 0 = anytime before start) | TC-LV-203, TC-LV-192 | Direct for default (N=0); N>0 window CONDITIONAL on tenant-settings |

## Non-Functional Requirements Coverage (US-LV-010)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Cancellation API responds within 500ms P95 | TC-LV-208 | Direct (Redis-cache-invalidation DEFERRED; DB path measured against 500ms) |
| NFR-2 | All operations tenant-isolated via EF Core filters (RLS-equivalent per vault) | TC-LV-199, TC-LV-200, TC-LV-206, TC-LV-207, TC-LV-ISO-037, TC-LV-ISO-038, TC-LV-ISO-039, TC-LV-ISO-040 | Direct |
| NFR-3 | Optimistic concurrency via PostgreSQL xmin (manager approve vs employee cancel) | TC-LV-201 | Direct |
| NFR-4 | Audit log captures before/after state of the leave request | TC-LV-204, TC-LV-189, TC-LV-190 | Direct |

## Business Rules Coverage (US-LV-010)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Only the requesting employee can cancel their own leave; managers cannot cancel on behalf | TC-LV-199, TC-LV-200, TC-LV-ISO-037 | Direct (KEY ownership/authz) |
| BR-2 | Rejected or already-cancelled leaves cannot be cancelled again | TC-LV-195, TC-LV-196 | Direct |
| BR-3 | Cancellation of approved leave after the start date is not allowed by default | TC-LV-192, TC-LV-193 | Direct |
| BR-4 | Carry-forward days consumed by the cancelled leave restored to the carry-forward pool | TC-LV-202 | CONDITIONAL (pool-split restoration; general `adjusted` reversal recorded if pool-specific not tracked) |
| BR-5 | Cancellation reason mandatory for approved, optional for pending | TC-LV-197, TC-LV-198 | Direct |

## Coverage Summary (US-LV-010)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 4/4 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-4 Redis invalidation DEFERRED (reversal ledger/DB-fallback verified); FR-7 N>0 window CONDITIONAL on tenant-settings (default verified) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) -- NFR-1 measured against 500ms (Redis-invalidation DEFERRED) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) -- BR-4 carry-forward-pool restoration CONDITIONAL (general adjusted reversal recorded if pool-split untracked) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 6 (4 dedicated ISO-037..040 + embedded tenant/ownership scope in TC-LV-200, TC-LV-ISO-037) | >= 3 | PASS |
| Security Test Cases | 8/25 (32%) including ISO (TC-LV-199, TC-LV-200, TC-LV-206, TC-LV-207, TC-LV-ISO-037..040) | >= 30% | PASS |
| Performance Test Cases | 1/25 (TC-LV-208) | >= 1 | PASS |
| Accessibility Test Cases | 1/25 (TC-LV-209) | >= 1 | PASS |
| Cross-Browser Test Cases | 1/25 (TC-LV-209) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred / Conditional Test Cases | TC-LV-190 (Redis balance-cache invalidation -- DEFERRED; reversal ledger/balance verified), TC-LV-194 (payroll-lock block -- CONDITIONAL on payroll module; non-locked verified), TC-LV-202 (carry-forward-pool restoration -- CONDITIONAL), TC-LV-203 (N-day cancellation window -- CONDITIONAL on tenant-settings; default verified), TC-LV-ISO-040 partial (balance cache keys -- pending Redis) | -- | NOTE |

---

*Note: This test matrix covers US-LV-001 (29 TCs), US-LV-002 (26 TCs), US-LV-003 (22 TCs), US-LV-004 (27 TCs), US-LV-005 (24 TCs), US-LV-006 (24 TCs), US-LV-007 (24 TCs), US-LV-008 (24 TCs), US-LV-009 (24 TCs), and US-LV-010 (25 TCs) for the Leave Management module. US-LV-010 (Leave Cancellation by Employee) adds 21 functional/security/performance/accessibility test cases (TC-LV-189..209) plus 4 dedicated multi-tenant isolation tests (TC-LV-ISO-037..040) for the employee-driven leave cancellation flow. All 4 acceptance criteria for US-LV-010 have direct coverage. Notes for US-LV-010: TC-LV-189 verifies the pending-cancel path (Cancelled, NO ledger entry, manager notification, audit); TC-LV-190 is the KEY approved-cancel path (Cancelled + reversal `adjusted` positive ledger entry restoring balance + audit), with the Redis cache invalidation (FR-4) DEFERRED module-wide per docs/vault/modules/leave-management.md and the LeaveLedger running-total DB-fallback verified; TC-LV-192/TC-LV-193 enforce the already-started block (AC-3/BR-3) at the start-date==today boundary with the contact-HR message; TC-LV-194 records the payroll-locked block (AC-4) as CONDITIONAL on the payroll module (non-locked path verified, error-contract surfaced); ownership/authz (BR-1) is verified at two layers -- a manager cannot cancel on behalf (TC-LV-199, 403) and an unrelated employee cannot cancel via IDOR (TC-LV-200, 403/404); BR-2 refuses re-cancelling rejected (TC-LV-195) or already-cancelled (TC-LV-196, no double reversal) requests; BR-5 reason-mandatory-for-approved/optional-for-pending is split across TC-LV-197/TC-LV-198; TC-LV-201 is the concurrency test (manager approve vs employee cancel -> xmin 409, only one wins); BR-4 carry-forward-pool restoration (TC-LV-202) is CONDITIONAL (a general `adjusted` reversal is recorded if pool-split is not separately tracked, flagged not silently passed); FR-7 N-day cancellation window (TC-LV-203) verifies the default anytime-before-start live and records the N>0 window CONDITIONAL on tenant-settings; NFR-1 (TC-LV-208) measures the DB-backed cancel path against 500ms P95; tenant isolation (TC-LV-ISO-037..039) confirms a Tenant A employee cannot cancel/resolve/restore a Tenant B request at the API, tenant-context, and EF-query-filter layers, and TC-LV-ISO-040 verifies the tenant+employee-scoped balance cache-key design with DB-fallback isolation verified live (partial pending Redis). US-LV-009 adds 20 functional/integration/security/performance/accessibility test cases (TC-LV-169..188) plus 4 dedicated multi-tenant isolation tests (TC-LV-ISO-033..036) for the Team Leave Calendar view. All 4 acceptance criteria for US-LV-009 have direct coverage. Notes for US-LV-009: the KEY access-control rule (AC-2/BR-1) is verified at two layers -- TC-LV-171 (UI shows department-approved leaves as a neutral "on leave" block with no pending and no leave-type) and TC-LV-172 (the raw API payload to an employee omits pending entries and leaveTypeName/type-color server-side, so a curious employee cannot read sensitive leave reasons via the network response); TC-LV-185 confirms an employee cannot escalate scope via parameter tampering (departmentId/employeeId/status overrides are ignored). Manager scope (BR-2, TC-LV-175) is limited to direct reports (ReportsToEmployeeId) and excludes other managers' teams; HR with Leave.ViewAll (BR-3, TC-LV-176) sees the whole tenant org. Cancelled leaves are excluded (BR-4, TC-LV-177); half-day leaves are visually differentiated (BR-5, TC-LV-178). FR-7 public-holiday background highlights (TC-LV-179) integrate the implemented US-LV-007 holiday calendar (location-scoped). NFR-1 (TC-LV-186) measures the DB-backed month-range path against 300ms P95 and records the Redis-cached read path as DEFERRED module-wide per docs/vault/modules/leave-management.md; TC-LV-ISO-036 verifies the tenant- and scope-scoped calendar cache-key design by design with DB-fallback isolation verified live (partial pending Redis). Tenant isolation (TC-LV-ISO-033..035) confirms Tenant A's calendar is invisible to Tenant B at the API, tenant-context, and EF-query-filter layers. US-LV-008 (TC-LV-149..168, TC-LV-ISO-029..032) coverage summary added here for rollup consistency (its detailed per-TC traceability lives in each TC file); US-LV-008 deferred items: TC-LV-151 (carry-forward-expiry Redis invalidation -- DEFERRED; DB/ledger verified), TC-LV-154 (encashment-on-expiry -- CONDITIONAL on leave-type config), TC-LV-167 (fiscal-year boundary -- CONDITIONAL on tenant fiscal-year config; calendar-year verified), TC-LV-ISO-032 (balance cache keys -- partial pending Redis). US-LV-007 adds 20 functional/integration/security/performance/accessibility test cases (TC-LV-129..148) plus 4 dedicated multi-tenant isolation tests (TC-LV-ISO-025..028) for tenant holiday-calendar management. All 4 acceptance criteria for US-LV-007 have direct coverage. Notes for US-LV-007: TC-LV-131 is the KEY integration test realizing the holiday-exclusion seam US-LV-003 (TC-LV-056) left dependent -- Mon-Fri spanning a Wednesday public holiday yields totalDays=4; TC-LV-132 confirms only Public holidays are auto-excluded (restricted/optional are not), and TC-LV-133 confirms location scoping (a New-York-only holiday does not reduce a London employee's count); duplicate-date prevention (BR-1) is split across location-specific (TC-LV-138) and tenant-wide null-location (TC-LV-139) partial unique indexes, and is also enforced on CSV import (TC-LV-135); recurring generation (FR-3/BR-5, TC-LV-141) is idempotent via the Hangfire HolidayRecurrenceJob; FR-5 onboarding seeding (TC-LV-144) verifies the seeding service + idempotency but records the onboarding-wizard call site as UNWIRED/DEFERRED (TODO(onboarding)); BR-4 payroll-period delete-lock (TC-LV-140) verifies deactivate-retention now and marks the finalized-period delete-block CONDITIONAL on the payroll module; NFR-1 Redis caching (TC-LV-147) and TC-LV-ISO-028 holiday cache keys are DEFERRED module-wide (DB-fallback path measured against 200ms and tenant-scoped key pattern verified by design); tenant isolation (TC-LV-ISO-025..027) confirms holidays in Tenant A are invisible to Tenant B at the API, context, and EF-query-filter layers. US-LV-001..US-LV-006 deferred items remain unchanged.*
