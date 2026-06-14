---
title: Requirements Traceability Matrix
project: HRM SaaS Platform
created: 2026-05-11
status: draft
last_updated: 2026-06-14
---

# Requirements Traceability Matrix

This document links user stories to their corresponding test cases across all modules, ensuring complete requirements coverage per IEEE 829 and ISO/IEC/IEEE 29119 standards.

## Authentication & Authorization Module

### Forward Traceability (User Stories --> Test Cases)

| User Story ID | User Story Title | Priority | Test Cases | TC Count | Coverage |
|---------------|-----------------|----------|------------|----------|----------|
| US-AUTH-001 | Admin login with username and password | Must Have | TC-AUTH-001, TC-AUTH-002, TC-AUTH-003, TC-AUTH-004 | 4 | 6/6 AC covered |
| US-AUTH-002 | JWT token issuance and refresh token flow | Must Have | TC-AUTH-005, TC-AUTH-006, TC-AUTH-007 | 3 | 7/7 AC covered |
| US-AUTH-003 | User logout and token invalidation | Must Have | TC-AUTH-008, TC-AUTH-009 | 2 | 5/5 AC covered |
| US-AUTH-004 | Password reset flow | Must Have | TC-AUTH-010, TC-AUTH-011, TC-AUTH-012 | 3 | 6/6 AC covered |
| US-AUTH-005 | Multi-factor authentication (TOTP) | Should Have | TC-AUTH-013, TC-AUTH-014, TC-AUTH-015, TC-AUTH-029, TC-AUTH-030, TC-AUTH-031, TC-AUTH-032, TC-AUTH-033, TC-AUTH-034, TC-AUTH-035, TC-AUTH-036, TC-AUTH-037, TC-AUTH-038 | 13 | 7/7 AC covered |
| US-AUTH-006 | Role-based access control (RBAC) | Must Have | TC-AUTH-016, TC-AUTH-017, TC-AUTH-018, TC-AUTH-039, TC-AUTH-040, TC-AUTH-041, TC-AUTH-042, TC-AUTH-043, TC-AUTH-044, TC-AUTH-045, TC-AUTH-046, TC-AUTH-047, TC-AUTH-048, TC-AUTH-049, TC-AUTH-050 | 15 | 7/7 AC covered (deep) |
| US-AUTH-007 | Tenant resolution from subdomain | Must Have | TC-AUTH-019, TC-AUTH-020, TC-AUTH-021, TC-AUTH-051, TC-AUTH-052, TC-AUTH-053, TC-AUTH-054, TC-AUTH-055, TC-AUTH-056, TC-AUTH-057, TC-AUTH-058 | 11 | 6/6 AC covered (deep) |
| US-AUTH-008 | Cross-tenant user switching | Should Have | TC-AUTH-022, TC-AUTH-023, TC-AUTH-059, TC-AUTH-060, TC-AUTH-061, TC-AUTH-062, TC-AUTH-063, TC-AUTH-064 | 8 | 5/5 AC covered (deep) |
| US-AUTH-009 | Session management and concurrent limits | Should Have | TC-AUTH-024, TC-AUTH-025, TC-AUTH-065, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-069, TC-AUTH-070, TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-074, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077, TC-AUTH-078, TC-AUTH-079, TC-AUTH-080, TC-AUTH-081, TC-AUTH-082 | 20 | 6/6 AC covered (deep) |
| US-AUTH-010 | Account lockout after failed attempts | Must Have | TC-AUTH-026, TC-AUTH-027, TC-AUTH-028, TC-AUTH-083, TC-AUTH-084, TC-AUTH-085, TC-AUTH-086, TC-AUTH-087, TC-AUTH-088, TC-AUTH-089, TC-AUTH-090, TC-AUTH-091, TC-AUTH-092, TC-AUTH-093, TC-AUTH-094, TC-AUTH-095, TC-AUTH-096, TC-AUTH-097, TC-AUTH-098, TC-AUTH-099, TC-AUTH-100, TC-AUTH-101, TC-AUTH-102, TC-AUTH-103, TC-AUTH-104, TC-AUTH-105, TC-AUTH-106, TC-AUTH-107, TC-AUTH-108, TC-AUTH-109, TC-AUTH-110, TC-AUTH-111, TC-AUTH-112 | 33 | 6/6 AC covered (deep) |
| Cross-cutting | Multi-tenant isolation (mandatory) | Critical | TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004 | 4 | -- |
| **TOTAL** | | | **116 test cases** | **116** | **61/61 AC** |

### Backward Traceability (Test Cases --> User Stories)

| Test Case ID | Test Case Title | Type | Priority | User Story | Requirements Covered |
|-------------|----------------|------|----------|------------|---------------------|
| TC-AUTH-001 | Successful login with valid credentials | Functional | Critical | US-AUTH-001 | AC-1 |
| TC-AUTH-002 | Login fails with wrong password | Security | Critical | US-AUTH-001 | AC-2 |
| TC-AUTH-003 | Login fails with non-existent username | Security | Critical | US-AUTH-001 | AC-2 |
| TC-AUTH-004 | Login form validation (empty fields) | Functional | High | US-AUTH-001 | AC-1 |
| TC-AUTH-005 | JWT issued on successful login | Functional | Critical | US-AUTH-002 | AC-1, AC-7 |
| TC-AUTH-006 | Refresh token rotation works | Functional | Critical | US-AUTH-002 | AC-2 |
| TC-AUTH-007 | Expired access token triggers refresh | Functional | Critical | US-AUTH-002 | AC-2, AC-4 |
| TC-AUTH-008 | Logout invalidates tokens | Functional | Critical | US-AUTH-003 | AC-1, AC-2, AC-4, AC-5 |
| TC-AUTH-009 | Refresh token cannot be reused after logout | Security | Critical | US-AUTH-003 | AC-2, AC-3 |
| TC-AUTH-010 | Forgot password sends reset email | Functional | Critical | US-AUTH-004 | AC-1, AC-2 |
| TC-AUTH-011 | Reset password with valid token works | Functional | Critical | US-AUTH-004 | AC-3, AC-6 |
| TC-AUTH-012 | Reset with expired/invalid token fails | Security | Critical | US-AUTH-004 | AC-4, AC-5 |
| TC-AUTH-013 through TC-AUTH-112 | (See previous version -- all unchanged) | | | | |
| TC-AUTH-ISO-001 | Tenant A user cannot authenticate as Tenant B | Security | Critical | US-AUTH-001, US-AUTH-007 | -- |
| TC-AUTH-ISO-002 | JWT claims include correct tenant_id | Security | Critical | US-AUTH-002, US-AUTH-006 | -- |
| TC-AUTH-ISO-003 | API rejects requests with mismatched tenant context | Security | Critical | US-AUTH-002, US-AUTH-007 | -- |
| TC-AUTH-ISO-004 | RBAC cross-tenant isolation -- roles, permissions, and cache keys are tenant-scoped | Security | Critical | US-AUTH-006 | FR-2, FR-10, NFR-2, BR-1 |

### US-AUTH-010 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Failed login below threshold increments counter, returns generic 401, no remaining-count leak | AC | TC-AUTH-026, TC-AUTH-083, TC-AUTH-111 | Direct |
| AC-2: Lockout at threshold sets locked_until, returns lockout message, logs account_locked audit | AC | TC-AUTH-026, TC-AUTH-084, TC-AUTH-111 | Direct |
| AC-3: Correct credentials during lockout are still rejected | AC | TC-AUTH-027, TC-AUTH-085 | Direct |
| AC-4: Lockout expiry clears counters and login succeeds | AC | TC-AUTH-028, TC-AUTH-086 | Direct |
| AC-5: Admin manual unlock clears counters, logs account_unlocked_by_admin, immediate login | AC | TC-AUTH-028, TC-AUTH-087, TC-AUTH-112 | Direct |
| AC-6: Successful login below threshold resets failed_login_count | AC | TC-AUTH-028, TC-AUTH-088 | Direct |
| FR-1 through BR-7 | (unchanged -- see previous version) | | |

### US-AUTH-009, US-AUTH-008, US-AUTH-007, US-AUTH-006, US-AUTH-005 Detailed Requirements Traceability

(Unchanged from previous version -- all Auth detailed traceability tables remain as documented.)

### Coverage Summary

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 61/61 (100%) | >= 100% | PASS |
| US-AUTH-005 AC Coverage | 7/7 (100%) | >= 100% | PASS |
| US-AUTH-005 FR Coverage | 10/10 (100%) | >= 100% | PASS |
| US-AUTH-005 NFR Coverage | 3/3 covered (NFR-1, NFR-3, NFR-4) | >= 85% | PASS |
| US-AUTH-005 BR Coverage | 5/5 (100%) | >= 100% | PASS |
| US-AUTH-006 Requirement Coverage | 10/10 FR + 4/4 NFR + 7/7 BR = 100% | >= 85% | PASS |
| US-AUTH-007 Requirement Coverage | 10/10 FR + 5/5 NFR + 5/5 BR = 100% | >= 85% | PASS |
| US-AUTH-008 Requirement Coverage | 9/9 FR + 4/4 NFR + 5/5 BR = 100% | >= 85% | PASS |
| US-AUTH-009 Requirement Coverage | 10/10 FR + 5/5 NFR + 6/6 BR = 100% | >= 85% | PASS |
| US-AUTH-010 Requirement Coverage | 10/10 FR + 5/5 NFR + 7/7 BR = 100% | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 23 (4 dedicated + 19 embedded) | >= 3 | PASS |
| Security Test Cases | 50/116 (43%) | >= 30% | PASS |
| Critical Module Coverage | 100% | >= 85% | PASS |
| API Endpoint Coverage | 31/31 (100%) | >= 90% | PASS |

---

## Core HR Module

### Forward Traceability (User Stories --> Test Cases)

| User Story ID | User Story Title | Priority | Test Cases | TC Count | Coverage |
|---------------|-----------------|----------|------------|----------|----------|
| US-CHR-001 | Add New Employee with Personal Information | Must Have | TC-CHR-064, TC-CHR-065, TC-CHR-066, TC-CHR-067, TC-CHR-068, TC-CHR-069, TC-CHR-070, TC-CHR-071, TC-CHR-072, TC-CHR-073, TC-CHR-074, TC-CHR-075, TC-CHR-076, TC-CHR-077, TC-CHR-078, TC-CHR-079, TC-CHR-080, TC-CHR-081, TC-CHR-082, TC-CHR-083, TC-CHR-084, TC-CHR-085, TC-CHR-086, TC-CHR-087, TC-CHR-088, TC-CHR-089, TC-CHR-090, TC-CHR-091, TC-CHR-092, TC-CHR-093, TC-CHR-094, TC-CHR-095, TC-CHR-096, TC-CHR-097, TC-CHR-098, TC-CHR-099, TC-CHR-100, TC-CHR-101, TC-CHR-102, TC-CHR-103 | 40 | 6/6 AC covered |
| US-CHR-002 | View and Edit Employee Profile | Must Have | TC-CHR-104, TC-CHR-105, TC-CHR-106, TC-CHR-107, TC-CHR-108, TC-CHR-109, TC-CHR-110, TC-CHR-111, TC-CHR-112, TC-CHR-113, TC-CHR-114, TC-CHR-115, TC-CHR-116, TC-CHR-117, TC-CHR-118, TC-CHR-119, TC-CHR-120, TC-CHR-121, TC-CHR-122, TC-CHR-123, TC-CHR-124, TC-CHR-125, TC-CHR-126 | 23 | 6/6 AC covered |
| US-CHR-003 | Employee Directory with Search and Filters | Must Have | TC-CHR-127, TC-CHR-128, TC-CHR-129, TC-CHR-130, TC-CHR-131, TC-CHR-132, TC-CHR-133, TC-CHR-134, TC-CHR-135, TC-CHR-136, TC-CHR-137, TC-CHR-138, TC-CHR-139, TC-CHR-140, TC-CHR-141, TC-CHR-142, TC-CHR-143, TC-CHR-144, TC-CHR-145, TC-CHR-146, TC-CHR-147, TC-CHR-148, TC-CHR-149, TC-CHR-150 | 24 | 5/5 AC covered |
| US-CHR-004 | Create and Manage Departments | Must Have | TC-CHR-001 through TC-CHR-034 | 34 | 5/5 AC covered (all unblocked) |
| US-CHR-005 | Create and Manage Job Titles and Positions | Must Have | TC-CHR-035 through TC-CHR-063 | 29 | 5/5 AC covered (all unblocked) |
| US-CHR-006 | Organization Tree / Hierarchy Visualization | Should Have | TC-CHR-151, TC-CHR-152, TC-CHR-153, TC-CHR-154, TC-CHR-155, TC-CHR-156, TC-CHR-157, TC-CHR-158, TC-CHR-159, TC-CHR-160, TC-CHR-161, TC-CHR-162, TC-CHR-163, TC-CHR-164, TC-CHR-165, TC-CHR-166, TC-CHR-167, TC-CHR-168, TC-CHR-169, TC-CHR-170, TC-CHR-171 | 21 | 5/5 AC covered |
| US-CHR-007 | Manage Office Locations | Should Have | TC-CHR-172, TC-CHR-173, TC-CHR-174, TC-CHR-175, TC-CHR-176, TC-CHR-177, TC-CHR-178, TC-CHR-179, TC-CHR-180, TC-CHR-181, TC-CHR-182, TC-CHR-183, TC-CHR-184, TC-CHR-185, TC-CHR-186, TC-CHR-187, TC-CHR-188, TC-CHR-189, TC-CHR-190, TC-CHR-191 | 20 | 4/4 AC covered |
| US-CHR-008 | Employee Document Management (Upload, View, Download) | Should Have | TC-CHR-192, TC-CHR-193, TC-CHR-194, TC-CHR-195, TC-CHR-196, TC-CHR-197, TC-CHR-198, TC-CHR-199, TC-CHR-200, TC-CHR-201, TC-CHR-202, TC-CHR-203, TC-CHR-204, TC-CHR-205, TC-CHR-206, TC-CHR-207, TC-CHR-208, TC-CHR-209, TC-CHR-210, TC-CHR-211, TC-CHR-212, TC-CHR-213, TC-CHR-214, TC-CHR-215, TC-CHR-216 | 25 | 5/5 AC covered |
| Cross-cutting (CHR-001) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-009, TC-CHR-ISO-010, TC-CHR-ISO-011, TC-CHR-ISO-012 | 4 | -- |
| Cross-cutting (CHR-002) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-013, TC-CHR-ISO-014, TC-CHR-ISO-015, TC-CHR-ISO-016 | 4 | -- |
| Cross-cutting (CHR-003) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-017, TC-CHR-ISO-018, TC-CHR-ISO-019, TC-CHR-ISO-020 | 4 | -- |
| Cross-cutting (CHR-004) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 4 | -- |
| Cross-cutting (CHR-005) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 4 | -- |
| Cross-cutting (CHR-006) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-021, TC-CHR-ISO-022, TC-CHR-ISO-023, TC-CHR-ISO-024 | 4 | -- |
| Cross-cutting (CHR-007) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-025, TC-CHR-ISO-026, TC-CHR-ISO-027, TC-CHR-ISO-028 | 4 | -- |
| Cross-cutting (CHR-008) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-029, TC-CHR-ISO-030, TC-CHR-ISO-031, TC-CHR-ISO-032 | 4 | -- |
| US-CHR-009 | Employee Status Management (Active, Probation, Suspended, Terminated) | Must Have | TC-CHR-217, TC-CHR-218, TC-CHR-219, TC-CHR-220, TC-CHR-221, TC-CHR-222, TC-CHR-223, TC-CHR-224, TC-CHR-225, TC-CHR-226, TC-CHR-227, TC-CHR-228, TC-CHR-229, TC-CHR-230, TC-CHR-231, TC-CHR-232, TC-CHR-233, TC-CHR-234, TC-CHR-235, TC-CHR-236, TC-CHR-237, TC-CHR-238 | 22 | 5/5 AC covered |
| Cross-cutting (CHR-009) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-033, TC-CHR-ISO-034, TC-CHR-ISO-035, TC-CHR-ISO-036 | 4 | -- |
| US-CHR-010 | Bulk Employee Import via CSV/Excel | Should Have | TC-CHR-239, TC-CHR-240, TC-CHR-241, TC-CHR-242, TC-CHR-243, TC-CHR-244, TC-CHR-245, TC-CHR-246, TC-CHR-247, TC-CHR-248, TC-CHR-249, TC-CHR-250, TC-CHR-251, TC-CHR-252, TC-CHR-253, TC-CHR-254, TC-CHR-255, TC-CHR-256, TC-CHR-257, TC-CHR-258, TC-CHR-259, TC-CHR-260, TC-CHR-261, TC-CHR-262, TC-CHR-263, TC-CHR-264, TC-CHR-265, TC-CHR-266, TC-CHR-267 | 29 | 5/5 AC covered |
| US-CHR-011 | Employee Reporting Structure (Manager Assignment) | Must Have | TC-CHR-268, TC-CHR-269, TC-CHR-270, TC-CHR-271, TC-CHR-272, TC-CHR-273, TC-CHR-274, TC-CHR-275, TC-CHR-276, TC-CHR-277, TC-CHR-278, TC-CHR-279, TC-CHR-280, TC-CHR-281, TC-CHR-282, TC-CHR-283, TC-CHR-284, TC-CHR-285, TC-CHR-286, TC-CHR-287, TC-CHR-288, TC-CHR-289, TC-CHR-290, TC-CHR-291, TC-CHR-292, TC-CHR-293, TC-CHR-294 | 27 | 5/5 AC covered |
| US-CHR-012 | Custom Fields per Tenant | Could Have | TC-CHR-295, TC-CHR-296, TC-CHR-297, TC-CHR-298, TC-CHR-299, TC-CHR-300, TC-CHR-301, TC-CHR-302, TC-CHR-303, TC-CHR-304, TC-CHR-305, TC-CHR-306, TC-CHR-307, TC-CHR-308, TC-CHR-309, TC-CHR-310, TC-CHR-311, TC-CHR-312, TC-CHR-313, TC-CHR-314, TC-CHR-315, TC-CHR-316, TC-CHR-317, TC-CHR-318, TC-CHR-319, TC-CHR-320, TC-CHR-321, TC-CHR-322, TC-CHR-323, TC-CHR-324 | 30 | 5/5 AC covered |
| Cross-cutting (CHR-010) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-037, TC-CHR-ISO-038, TC-CHR-ISO-039, TC-CHR-ISO-040 | 4 | -- |
| Cross-cutting (CHR-011) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-041, TC-CHR-ISO-042, TC-CHR-ISO-043, TC-CHR-ISO-044 | 4 | -- |
| Cross-cutting (CHR-012) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-045, TC-CHR-ISO-046, TC-CHR-ISO-047, TC-CHR-ISO-048 | 4 | -- |
| **TOTAL** | | | **372 test cases** | **372** | **61/61 AC** |

### Backward Traceability (Test Cases --> User Stories)

| Test Case ID | Test Case Title | Type | Priority | User Story | Requirements Covered |
|-------------|----------------|------|----------|------------|---------------------|
| TC-CHR-001 | Create a root department successfully (happy path) | Functional | Critical | US-CHR-004 | AC-1, AC-2, FR-1, FR-8, NFR-5, BR-4 |
| TC-CHR-002 | Create a child department with parent assignment | Functional | Critical | US-CHR-004 | AC-1, AC-2, AC-4, FR-1, FR-3, FR-8, BR-3, BR-4 |
| TC-CHR-003 | Reject duplicate department name within same tenant | Functional | Critical | US-CHR-004 | AC-3, FR-2, BR-1 |
| TC-CHR-004 | Same department name allowed in different tenants | Security | Critical | US-CHR-004 | AC-3, FR-2, NFR-2, BR-1 |
| TC-CHR-005 | Build multi-level department hierarchy (3+ levels) | Functional | Critical | US-CHR-004 | AC-2, AC-4, FR-3, FR-8, BR-3, BR-4 |
| TC-CHR-006 | Prevent circular parent-child reference (direct cycle) | Functional | Critical | US-CHR-004 | AC-4, FR-3, FR-5 |
| TC-CHR-007 | Prevent circular parent-child reference (indirect cycle A->B->C->A) | Functional | Critical | US-CHR-004 | AC-4, FR-3, FR-5 |
| TC-CHR-008 | Edit department name and description | Functional | High | US-CHR-004 | AC-4, FR-1, NFR-5 |
| TC-CHR-009 | Edit department parent (reassign in hierarchy) | Functional | High | US-CHR-004 | AC-4, FR-1, FR-3, FR-8, BR-3 |
| TC-CHR-010 | Deactivate department blocked when active employees assigned | Functional | Critical | US-CHR-004 | AC-5, FR-6, BR-5 |
| TC-CHR-011 | Deactivate department with no active employees (success) | Functional | High | US-CHR-004 | AC-5, FR-6, FR-7, NFR-5, BR-5 |
| TC-CHR-012 through TC-CHR-150 | (See previous version -- all unchanged) | | | | |
| TC-CHR-151 | Department hierarchy tree renders with correct parent-child and employee counts | Functional | Critical | US-CHR-006 | AC-1, FR-1, FR-2, FR-5, FR-8, BR-1, BR-2 |
| TC-CHR-152 | Click department node opens detail panel with manager, employees, sub-departments | Functional | Critical | US-CHR-006 | AC-2, FR-1, FR-2, BR-5 |
| TC-CHR-153 | Toggle to reporting structure view shows manager-to-direct-report relationships | Functional | Critical | US-CHR-006 | AC-3, FR-1, FR-2, BR-3 |
| TC-CHR-154 | Search for employee at deepest level -- tree auto-expands, scrolls, highlights | Functional | Critical | US-CHR-006 | AC-4, FR-4, FR-6, BR-1 |
| TC-CHR-155 | Lazy loading -- only top 2 levels load; expanding triggers API call for children | Functional | Critical | US-CHR-006 | AC-5, FR-6, FR-2, NFR-1 |
| TC-CHR-156 | Expand and collapse tree nodes with smooth animation | Functional | High | US-CHR-006 | FR-2 |
| TC-CHR-157 | Pan and zoom interactions on desktop and mobile | Functional | High | US-CHR-006 | FR-3, NFR-2 |
| TC-CHR-158 | Export org chart as PNG contains visible tree structure | Functional | High | US-CHR-006 | FR-7 |
| TC-CHR-159 | Inactive toggle shows inactive departments and employees | Functional | High | US-CHR-006 | BR-4, FR-1, FR-8 |
| TC-CHR-160 | Expand a leaf node with no children -- empty state, no error | Functional | High | US-CHR-006 | FR-2, FR-6, AC-5 |
| TC-CHR-161 | Search with no match returns no highlight and informative empty state | Functional | High | US-CHR-006 | AC-4, FR-4 |
| TC-CHR-162 | Unauthenticated request to org-tree API returns 401 | Security | Critical | US-CHR-006 | FR-8, NFR-3 |
| TC-CHR-163 | Input sanitization -- XSS in org tree search bar | Security | High | US-CHR-006 | FR-4, NFR-3 |
| TC-CHR-164 | Initial top-2-level tree render within 2.5 seconds P95 | Performance | Critical | US-CHR-006 | NFR-1, AC-5 |
| TC-CHR-165 | 200-node tree smooth pan/zoom at approximately 60fps | Performance | Critical | US-CHR-006 | NFR-2, AC-5, FR-3 |
| TC-CHR-166 | WCAG 2.1 AA keyboard arrow-key navigation and screen reader | Accessibility | High | US-CHR-006 | NFR-5, FR-2 |
| TC-CHR-167 | Responsive layout at 360px falls back to accordion/vertical list | Functional | High | US-CHR-006 | NFR-4 |
| TC-CHR-168 | Cross-browser compatibility for org tree (Chrome, Edge, Firefox, Safari) | Functional | Medium | US-CHR-006 | NFR-2, NFR-4 |
| TC-CHR-169 | Tree is read-only -- no drag-and-drop; links to management pages | Functional | High | US-CHR-006 | BR-5, AC-2 |
| TC-CHR-170 | Root departments at top; employees without manager under department in reporting view | Functional | High | US-CHR-006 | BR-2, BR-3, AC-1, AC-3 |
| TC-CHR-171 | Org tree reflects current state -- not historical snapshots | Functional | High | US-CHR-006 | BR-1 |
| TC-CHR-172 | Create a new office location with all fields (happy path) | Functional | Critical | US-CHR-007 | AC-1, AC-2, FR-1, FR-3, FR-4, NFR-4, BR-1 |
| TC-CHR-173 | New location appears in employee assignment dropdowns and holiday calendar configuration | Functional | Critical | US-CHR-007 | AC-2, FR-1, BR-2, BR-4 |
| TC-CHR-174 | Edit location time zone -- saved correctly and audit log entry recorded | Functional | Critical | US-CHR-007 | AC-4, FR-1, FR-4, NFR-4, BR-3 |
| TC-CHR-175 | Duplicate location name within same tenant is rejected | Functional | Critical | US-CHR-007 | FR-2, BR-1 |
| TC-CHR-176 | Same location name allowed in different tenants | Security | Critical | US-CHR-007 | FR-2, NFR-2, BR-1 |
| TC-CHR-177 | Required field validation -- name and time zone missing triggers error | Functional | High | US-CHR-007 | AC-1, FR-3, FR-4 |
| TC-CHR-178 | Deactivation blocked when location has active employees assigned | Functional | Critical | US-CHR-007 | AC-3, FR-5, FR-7, BR-5 |
| TC-CHR-179 | Deactivate location with no active employees succeeds (soft delete) | Functional | High | US-CHR-007 | FR-5, FR-6, NFR-4, BR-5 |
| TC-CHR-180 | Boundary -- field length limits enforced (name 150, postal 20, etc.) | Functional | High | US-CHR-007 | FR-3, FR-4 |
| TC-CHR-181 | Tenant with zero locations operates without errors (BR-6) | Functional | High | US-CHR-007 | BR-6 |
| TC-CHR-182 | IANA time zone identifier stored and displayed correctly | Functional | High | US-CHR-007 | FR-4, BR-3 |
| TC-CHR-183 | Deactivated location cannot be assigned to new employees but remains on existing records | Functional | High | US-CHR-007 | BR-5, FR-6 |
| TC-CHR-184 | Employee count displayed per location with clickable badge | Functional | High | US-CHR-007 | FR-7 |
| TC-CHR-185 | Audit log entries created for create, update, and deactivate operations | Functional | High | US-CHR-007 | NFR-4, FR-1 |
| TC-CHR-186 | Unauthenticated request to location API returns 401 | Security | Critical | US-CHR-007 | FR-1, FR-8, NFR-2 |
| TC-CHR-187 | Role-based access -- only Tenant Admin and HR Officer can create, edit, deactivate locations | Security | Critical | US-CHR-007 | FR-1, FR-8 |
| TC-CHR-188 | Location API response times within SLA (read <= 400ms P95, write <= 800ms P95) | Performance | High | US-CHR-007 | NFR-1 |
| TC-CHR-189 | Locations management page meets WCAG 2.1 AA accessibility standards | Accessibility | High | US-CHR-007 | NFR-3 |
| TC-CHR-190 | Responsive layout -- 360px viewport collapses to card list | Functional | High | US-CHR-007 | NFR-3 |
| TC-CHR-191 | Cross-browser compatibility for Locations management page | Functional | Medium | US-CHR-007 | NFR-3 |
| TC-CHR-192 | Upload valid 5 MB PDF -- stored at tenant/employee-prefixed path with metadata row and appears in document list (happy path) | Functional | Critical | US-CHR-008 | AC-1, AC-2, FR-1, FR-2, FR-3, FR-9, NFR-2, BR-1 |
| TC-CHR-193 | Download document as owner employee via signed URL (happy path) | Functional | Critical | US-CHR-008 | AC-4, FR-6, FR-10, NFR-6, BR-2 |
| TC-CHR-194 | Upload .exe file is rejected (negative) | Functional | Critical | US-CHR-008 | AC-3, FR-2, BR-7 |
| TC-CHR-195 | Upload 15 MB file is rejected with size limit error (negative) | Functional | Critical | US-CHR-008 | AC-3, FR-2, BR-7 |
| TC-CHR-196 | Upload disallowed MIME type (e.g., .svg, .html) is rejected (negative) | Functional | Critical | US-CHR-008 | AC-3, FR-2, BR-7 |
| TC-CHR-197 | Cross-tenant download attempt returns 403 and triggers security alert | Security | Critical | US-CHR-008 | AC-4, FR-6, NFR-3, BR-2 |
| TC-CHR-198 | Tenant isolation on document list -- Tenant A documents not visible to Tenant B | Security | Critical | US-CHR-008 | FR-3, FR-9, NFR-2, BR-1 |
| TC-CHR-199 | Role-based access -- HR uploads/deletes, Employee views/downloads own only, Manager denied | Security | Critical | US-CHR-008 | FR-10, BR-1, BR-2, BR-3 |
| TC-CHR-200 | Unauthenticated request to document API returns 401 | Security | Critical | US-CHR-008 | FR-6, FR-9, FR-10, NFR-2 |
| TC-CHR-201 | Virus scan rejects EICAR test file on upload | Security | Critical | US-CHR-008 | FR-4, NFR-2 |
| TC-CHR-202 | Boundary -- exactly 10 MB file allowed, 10 MB + 1 byte rejected | Functional | High | US-CHR-008 | AC-3, FR-2 |
| TC-CHR-203 | Expiry badge thresholds -- green (>30d), amber (<30d), red (<7d), red/expired | Functional | High | US-CHR-008 | FR-8, FR-9 |
| TC-CHR-204 | Expiry notification -- background job generates notifications at 30/7/1 day marks | Functional | High | US-CHR-008 | AC-5, FR-8, BR-4 |
| TC-CHR-205 | Storage quota -- 80% warning and block at plan limit | Functional | High | US-CHR-008 | NFR-4, BR-6 |
| TC-CHR-206 | Soft delete -- is_deleted set to true, file retained in storage | Functional | High | US-CHR-008 | FR-7, BR-5 |
| TC-CHR-207 | Audit trail -- document view and download events logged | Security | High | US-CHR-008 | FR-6, FR-9, NFR-6 |
| TC-CHR-208 | Responsive layout -- 360px viewport shows card stack and file picker instead of drag-drop | Functional | High | US-CHR-008 | NFR-5 |
| TC-CHR-209 | Performance -- file upload within 5 seconds for 10 MB; API read/write within SLA | Performance | High | US-CHR-008 | NFR-1 |
| TC-CHR-210 | WCAG 2.1 AA accessibility for document management UI | Accessibility | High | US-CHR-008 | NFR-5 |
| TC-CHR-211 | Cross-browser compatibility for document management (Chrome, Edge, Firefox, Safari) | Functional | Medium | US-CHR-008 | NFR-5 |
| TC-CHR-212 | EXIF data stripped from image uploads | Security | High | US-CHR-008 | FR-5 |
| TC-CHR-213 | Document categorized list displays all metadata columns correctly | Functional | High | US-CHR-008 | FR-9 |
| TC-CHR-214 | Category filter tabs (All, Contracts, IDs, Certificates, Other) filter the document list | Functional | High | US-CHR-008 | FR-9 |
| TC-CHR-215 | Input sanitization -- XSS in document description field | Security | High | US-CHR-008 | FR-1, NFR-2 |
| TC-CHR-216 | Upload form displays all required fields (AC-1 detail) | Functional | Critical | US-CHR-008 | AC-1, FR-1 |
| TC-CHR-ISO-001 | Tenant A cannot see Tenant B's departments | Security | Critical | US-CHR-004 | NFR-2, BR-1 |
| TC-CHR-ISO-002 | API rejects department requests without valid tenant context | Security | Critical | US-CHR-004 | NFR-2 |
| TC-CHR-ISO-003 | RLS blocks direct DB queries across tenants for departments | Security | Critical | US-CHR-004 | NFR-2, BR-1, BR-3 |
| TC-CHR-ISO-004 | Cache keys for departments are tenant-scoped | Security | Critical | US-CHR-004 | NFR-2 |
| TC-CHR-ISO-005 | Tenant A cannot see Tenant B's job titles | Security | Critical | US-CHR-005 | NFR-2, BR-1, BR-4 |
| TC-CHR-ISO-006 | API rejects job title requests without valid tenant context | Security | Critical | US-CHR-005 | NFR-2 |
| TC-CHR-ISO-007 | RLS blocks direct DB queries across tenants for job titles | Security | Critical | US-CHR-005 | NFR-2, BR-1, BR-4 |
| TC-CHR-ISO-008 | Cache keys for job titles are tenant-scoped | Security | Critical | US-CHR-005 | NFR-2 |
| TC-CHR-ISO-009 | Tenant A cannot see Tenant B's employees | Security | Critical | US-CHR-001 | NFR-2, BR-1, BR-2 |
| TC-CHR-ISO-010 | API rejects employee requests without valid tenant context | Security | Critical | US-CHR-001 | NFR-2, FR-4 |
| TC-CHR-ISO-011 | RLS blocks direct DB queries across tenants for employees | Security | Critical | US-CHR-001 | NFR-2 |
| TC-CHR-ISO-012 | Cache keys for employees are tenant-scoped | Security | Critical | US-CHR-001 | NFR-2 |
| TC-CHR-ISO-013 | Tenant A cannot view or edit Tenant B's employee profiles | Security | Critical | US-CHR-002 | FR-7, NFR-3 |
| TC-CHR-ISO-014 | API rejects employee profile requests without valid tenant context | Security | Critical | US-CHR-002 | FR-7, NFR-3 |
| TC-CHR-ISO-015 | RLS blocks direct DB queries across tenants for employee profiles | Security | Critical | US-CHR-002 | FR-7, NFR-3 |
| TC-CHR-ISO-016 | Cache keys for employee profiles are tenant-scoped | Security | Critical | US-CHR-002 | NFR-3 |
| TC-CHR-ISO-017 | Tenant A directory shows zero Tenant B employees | Security | Critical | US-CHR-003 | FR-7, NFR-3 |
| TC-CHR-ISO-018 | API rejects directory requests without valid tenant context | Security | Critical | US-CHR-003 | FR-7, NFR-3 |
| TC-CHR-ISO-019 | RLS blocks direct DB queries across tenants for directory data | Security | Critical | US-CHR-003 | FR-7, NFR-3, BR-1 |
| TC-CHR-ISO-020 | Cache keys for directory queries are tenant-scoped | Security | Critical | US-CHR-003 | FR-7, NFR-3 |
| TC-CHR-ISO-021 | Tenant A org tree shows zero Tenant B departments and employees | Security | Critical | US-CHR-006 | FR-8, NFR-3 |
| TC-CHR-ISO-022 | API rejects org-tree requests without valid tenant context | Security | Critical | US-CHR-006 | FR-8, NFR-3 |
| TC-CHR-ISO-023 | RLS blocks direct DB queries across tenants for org-tree data | Security | Critical | US-CHR-006 | FR-8, NFR-3 |
| TC-CHR-ISO-024 | Cache keys for org-tree data are tenant-scoped | Security | Critical | US-CHR-006 | FR-8, NFR-3 |
| TC-CHR-ISO-025 | Tenant A cannot see Tenant B's locations | Security | Critical | US-CHR-007 | FR-1, FR-8, NFR-2 |
| TC-CHR-ISO-026 | API rejects location requests without valid tenant context | Security | Critical | US-CHR-007 | FR-8, NFR-2 |
| TC-CHR-ISO-027 | RLS blocks direct DB queries across tenants for locations | Security | Critical | US-CHR-007 | FR-8, NFR-2 |
| TC-CHR-ISO-028 | Cache keys for locations are tenant-scoped | Security | Critical | US-CHR-007 | NFR-2 |
| TC-CHR-ISO-029 | Tenant A cannot see Tenant B's employee documents | Security | Critical | US-CHR-008 | FR-3, NFR-2 |
| TC-CHR-ISO-030 | API rejects document requests without valid tenant context | Security | Critical | US-CHR-008 | NFR-2 |
| TC-CHR-ISO-031 | RLS blocks direct DB queries across tenants for employee documents | Security | Critical | US-CHR-008 | NFR-2 |
| TC-CHR-ISO-032 | Document storage paths and cache keys are tenant-scoped | Security | Critical | US-CHR-008 | FR-3, NFR-2 |
| TC-CHR-217 | Change active to suspended -- status updated, history entry, audit log, portal access disabled (happy path) | Functional | Critical | US-CHR-009 | AC-1, AC-2, FR-3, FR-4, FR-5, NFR-5, BR-1, BR-2 |
| TC-CHR-218 | Status transition form shows only valid transitions based on current status | Functional | Critical | US-CHR-009 | AC-1, FR-2, BR-1 |
| TC-CHR-219 | Invalid transition terminated to probation via API returns 400 with exact error message | Functional | Critical | US-CHR-009 | AC-5, FR-2, BR-1, BR-3 |
| TC-CHR-220 | Status change without reason rejected with validation error | Functional | High | US-CHR-009 | FR-3 |
| TC-CHR-221 | Status change without effective date rejected with validation error | Functional | High | US-CHR-009 | FR-3 |
| TC-CHR-222 | Terminate employee -- login disabled, headcount excluded, payroll exclusion hook | Functional | Critical | US-CHR-009 | AC-3, FR-5, BR-3, BR-5 |
| TC-CHR-223 | State machine boundary -- all allowed transitions succeed, terminated is terminal | Functional | Critical | US-CHR-009 | FR-1, FR-2, BR-1, BR-3 |
| TC-CHR-224 | Probation reminder -- daily job sends HR notification, no auto-transition | Functional | High | US-CHR-009 | AC-4, FR-6, BR-6 |
| TC-CHR-225 | Future-dated status change -- not applied today, background job applies on effective date | Functional | Critical | US-CHR-009 | BR-4, FR-3, FR-4 |
| TC-CHR-226 | Idempotency -- duplicate request with same Idempotency-Key yields one transition | Security | High | US-CHR-009 | NFR-3 |
| TC-CHR-227 | Manager role blocked from changing employee status | Security | Critical | US-CHR-009 | BR-2 |
| TC-CHR-228 | Employee role blocked from changing any employee status | Security | Critical | US-CHR-009 | BR-2 |
| TC-CHR-229 | Unauthenticated request to status change API returns 401 | Security | Critical | US-CHR-009 | BR-2, NFR-2 |
| TC-CHR-230 | Audit log records before/after snapshot for status change | Functional | High | US-CHR-009 | NFR-5, FR-4 |
| TC-CHR-231 | Employment history -- 3 status changes produce 3 timeline entries | Functional | High | US-CHR-009 | FR-4, AC-2 |
| TC-CHR-232 | Responsive -- 360px viewport shows bottom sheet instead of modal | Functional | High | US-CHR-009 | NFR-4 |
| TC-CHR-233 | Status badge color-coded on employee profile and directory | Functional | High | US-CHR-009 | FR-7 |
| TC-CHR-234 | Status change API response time within 800ms P95 | Performance | High | US-CHR-009 | NFR-1 |
| TC-CHR-235 | Status change form and timeline meet WCAG 2.1 AA accessibility | Accessibility | High | US-CHR-009 | NFR-4 |
| TC-CHR-236 | Cross-browser compatibility for status change flow | Functional | Medium | US-CHR-009 | NFR-4 |
| TC-CHR-237 | Suspended employee excluded from active headcount but data retained | Functional | High | US-CHR-009 | BR-5, FR-5 |
| TC-CHR-238 | Reactivating to Active re-enables portal access and resumes leave accrual | Functional | High | US-CHR-009 | FR-5 |
| TC-CHR-ISO-033 | Tenant A status change and employment history not visible to Tenant B | Security | Critical | US-CHR-009 | NFR-2 |
| TC-CHR-ISO-034 | API rejects status change requests without valid tenant context | Security | Critical | US-CHR-009 | NFR-2 |
| TC-CHR-ISO-035 | RLS blocks direct DB queries across tenants for status and history data | Security | Critical | US-CHR-009 | NFR-2 |
| TC-CHR-ISO-036 | Cache keys for employee status and employment history are tenant-scoped | Security | Critical | US-CHR-009 | NFR-2 |
| TC-CHR-239 | Download import template -- CSV and Excel with correct headers and sample data | Functional | Critical | US-CHR-010 | AC-1, FR-2 |
| TC-CHR-240 | Upload valid CSV with 10 rows -- all employees created with correct tenant_id and employee_no | Functional | Critical | US-CHR-010 | AC-2, FR-1, FR-3, FR-5, FR-6, FR-10, BR-1, BR-4 |
| TC-CHR-241 | Upload valid Excel (.xlsx) file -- all employees created | Functional | Critical | US-CHR-010 | AC-2, FR-1, BR-6 |
| TC-CHR-242 | Partial failure -- 8 valid + 2 invalid rows; 8 created, 2 in error report | Functional | Critical | US-CHR-010 | AC-3, FR-3, FR-4, FR-8, BR-3 |
| TC-CHR-243 | Duplicate email within file -- second occurrence flagged | Functional | Critical | US-CHR-010 | AC-3, FR-3, BR-2 |
| TC-CHR-244 | Non-existent department_name -- row rejected | Functional | Critical | US-CHR-010 | AC-3, FR-3, BR-3 |
| TC-CHR-245 | Missing required field -- row rejected with field-level error | Functional | Critical | US-CHR-010 | AC-3, FR-3, FR-4, FR-8 |
| TC-CHR-246 | File > 25 MB rejected | Functional | Critical | US-CHR-010 | BR-7 |
| TC-CHR-247 | Disallowed file type (.pdf) rejected | Functional | Critical | US-CHR-010 | FR-1 |
| TC-CHR-248 | Plan limit pre-validation warning with import-up-to-limit or cancel | Functional | Critical | US-CHR-010 | AC-5, FR-9 |
| TC-CHR-249 | Async large file (1000+ rows) queued as Hangfire job with progress | Functional | Critical | US-CHR-010 | AC-4, FR-7, NFR-1 |
| TC-CHR-250 | Idempotency -- re-upload same file no duplicates | Functional | High | US-CHR-010 | NFR-3, FR-3, BR-2 |
| TC-CHR-251 | Audit log records import with file name and counts | Functional | High | US-CHR-010 | FR-10 |
| TC-CHR-252 | tenant_id from session not file -- file column ignored | Security | Critical | US-CHR-010 | AC-2, FR-6, BR-1 |
| TC-CHR-253 | Role check -- only HR Officer and Tenant Admin can import | Security | Critical | US-CHR-010 | Precondition (Section 2) |
| TC-CHR-254 | Unauthenticated request returns 401 | Security | Critical | US-CHR-010 | Precondition (Section 2) |
| TC-CHR-255 | Default status active when no status column | Functional | High | US-CHR-010 | BR-4, FR-3 |
| TC-CHR-256 | Import does not create user accounts | Functional | High | US-CHR-010 | BR-5 |
| TC-CHR-257 | Non-existent job_title_name -- row rejected | Functional | High | US-CHR-010 | AC-3, FR-3, BR-3 |
| TC-CHR-258 | Invalid email format -- row rejected | Functional | High | US-CHR-010 | AC-3, FR-3 |
| TC-CHR-259 | Download error report CSV -- correct format and content | Functional | High | US-CHR-010 | AC-3, FR-8 |
| TC-CHR-260 | Async completion notification (email DEFERRED) | Functional | High | US-CHR-010 | AC-4, FR-7 |
| TC-CHR-261 | Transaction behavior -- sync all-or-nothing; async per-batch rollback | Functional | High | US-CHR-010 | NFR-4 |
| TC-CHR-262 | Performance -- 10,000 rows within 5 minutes, bounded memory | Performance | High | US-CHR-010 | NFR-1, NFR-6 |
| TC-CHR-263 | Responsive UI -- 360px stacked wizard with file picker | Functional | High | US-CHR-010 | NFR-5 |
| TC-CHR-264 | WCAG 2.1 AA accessibility for bulk import wizard | Accessibility | High | US-CHR-010 | NFR-5 |
| TC-CHR-265 | Cross-browser compatibility (Chrome, Edge, Firefox, Safari) | Functional | Medium | US-CHR-010 | NFR-5 |
| TC-CHR-266 | XSS payload in import field values does not execute | Security | High | US-CHR-010 | FR-3 |
| TC-CHR-267 | Custom field column mapping (DEFERRED to US-CHR-012) | Functional | Medium | US-CHR-010 | FR-11 |
| TC-CHR-ISO-037 | Tenant A imported employees not visible to Tenant B | Security | Critical | US-CHR-010 | NFR-2, FR-6, BR-1 |
| TC-CHR-ISO-038 | API rejects import without valid tenant context | Security | Critical | US-CHR-010 | NFR-2, BR-1 |
| TC-CHR-ISO-039 | RLS blocks cross-tenant queries for imported data | Security | Critical | US-CHR-010 | NFR-2 |
| TC-CHR-ISO-040 | Cache keys for import operations are tenant-scoped | Security | Critical | US-CHR-010 | NFR-2 |
| TC-CHR-268 | Assign reporting manager to employee -- happy path | Functional | Critical | US-CHR-011 | AC-1, AC-2, FR-1, FR-2, FR-6, NFR-5 |
| TC-CHR-269 | Reporting Manager field displays current manager or "Not Assigned" | Functional | High | US-CHR-011 | AC-1, FR-1, FR-2 |
| TC-CHR-270 | My Team / direct reports view lists all reports with correct fields | Functional | Critical | US-CHR-011 | AC-4, FR-5 |
| TC-CHR-271 | Bulk assign manager to 5 employees via employee directory | Functional | Critical | US-CHR-011 | AC-5, FR-4, FR-6, NFR-5 |
| TC-CHR-272 | Circular reporting chain detection -- direct cycle A->B then B->A | Functional | Critical | US-CHR-011 | AC-3, FR-3 |
| TC-CHR-273 | Circular reporting chain detection -- indirect cycle A->B->C then C->A | Functional | Critical | US-CHR-011 | AC-3, FR-3 |
| TC-CHR-274 | Self-assignment rejected -- employee cannot report to themselves | Functional | Critical | US-CHR-011 | BR-7, FR-3 |
| TC-CHR-275 | Inactive/terminated employee cannot be assigned as manager | Functional | Critical | US-CHR-011 | BR-3 |
| TC-CHR-276 | Employee with no manager (null FK) works and appears as org-tree root | Functional | High | US-CHR-011 | FR-8, BR-1 |
| TC-CHR-277 | Manager termination triggers HR reassignment reminder notification | Functional | High | US-CHR-011 | BR-4 (notification dispatch DEFERRED) |
| TC-CHR-278 | Assign then reassign manager -- 2 employment history entries with before/after | Functional | Critical | US-CHR-011 | AC-2, FR-6, NFR-5 |
| TC-CHR-279 | Manager from different department can be assigned (cross-department reporting) | Functional | High | US-CHR-011 | BR-5 |
| TC-CHR-280 | Employee can have at most one direct reporting manager | Functional | High | US-CHR-011 | BR-1, FR-2 |
| TC-CHR-281 | Manager can have unlimited direct reports (no system-enforced limit) | Functional | High | US-CHR-011 | BR-2 |
| TC-CHR-282 | Org tree reporting structure view shows real manager-to-report hierarchy | Functional | High | US-CHR-011 | FR-5, FR-8 (US-CHR-006 integration) |
| TC-CHR-283 | Reporting chain breadcrumb displayed on employee profile | Functional | High | US-CHR-011 | UI/UX Section 8 |
| TC-CHR-284 | Unauthenticated request to manager assignment and direct-reports APIs returns 401 | Security | Critical | US-CHR-011 | NFR-3 |
| TC-CHR-285 | Only HR Officer and Tenant Admin can assign reporting managers | Security | Critical | US-CHR-011 | Precondition Section 2 |
| TC-CHR-286 | Manager role cannot assign reporting managers via API | Security | Critical | US-CHR-011 | Precondition Section 2 |
| TC-CHR-287 | Employee role cannot assign reporting managers via API | Security | Critical | US-CHR-011 | Precondition Section 2 |
| TC-CHR-288 | Input sanitization -- XSS in manager search autocomplete | Security | High | US-CHR-011 | NFR-3 |
| TC-CHR-289 | Manager assignment API response time within 800ms P95 including cycle detection | Performance | Critical | US-CHR-011 | NFR-1 |
| TC-CHR-290 | Deep hierarchy (10 levels) cycle detection completes within 200ms | Performance | Critical | US-CHR-011 | NFR-2, FR-3 |
| TC-CHR-291 | Bulk manager assignment for 100 employees completes within 5 seconds | Performance | High | US-CHR-011 | NFR-6, FR-4 |
| TC-CHR-292 | Manager assignment UI meets WCAG 2.1 AA accessibility standards | Accessibility | High | US-CHR-011 | NFR-4 |
| TC-CHR-293 | Responsive layout at 360px -- manager selector overlay and My Team stack | Functional | High | US-CHR-011 | NFR-4 |
| TC-CHR-294 | Cross-browser compatibility for manager assignment and My Team features | Functional | Medium | US-CHR-011 | NFR-4 |
| TC-CHR-ISO-041 | Tenant A cannot see Tenant B's direct reports or reporting structure | Security | Critical | US-CHR-011 | NFR-3, FR-9 |
| TC-CHR-ISO-042 | API rejects manager assignment requests without valid tenant context | Security | Critical | US-CHR-011 | NFR-3, FR-9 |
| TC-CHR-ISO-043 | RLS blocks direct DB queries across tenants for reporting structure data | Security | Critical | US-CHR-011 | NFR-3, FR-9 |
| TC-CHR-ISO-044 | Cache keys for reporting structure and direct-reports are tenant-scoped | Security | Critical | US-CHR-011 | NFR-3 |
| TC-CHR-295 | Create a "T-Shirt Size" dropdown custom field -- happy path | Functional | Critical | US-CHR-012 | AC-1, AC-2, FR-1, FR-2, FR-3, FR-9, NFR-5 |
| TC-CHR-296 | Custom field dynamically rendered on employee create and profile edit forms | Functional | Critical | US-CHR-012 | AC-2, FR-9, NFR-6 |
| TC-CHR-297 | Store and retrieve custom field value on employee JSONB column | Functional | Critical | US-CHR-012 | AC-3, FR-4, FR-5 |
| TC-CHR-298 | Usage count displayed on custom fields management page | Functional | High | US-CHR-012 | AC-1 |
| TC-CHR-299 | Reorder custom fields via display_order | Functional | High | US-CHR-012 | FR-8, NFR-4 |
| TC-CHR-300 | Plan limit reached -- 6th field blocked with upgrade message (DEFERRED) | Functional | Critical | US-CHR-012 | AC-4, FR-6, BR-4 |
| TC-CHR-301 | Number field rejects non-numeric value "abc" -- type validation | Functional | Critical | US-CHR-012 | FR-5 |
| TC-CHR-302 | Required custom field missing on employee save -- validation error | Functional | Critical | US-CHR-012 | FR-5 |
| TC-CHR-303 | Duplicate field name within tenant+entity rejected | Functional | Critical | US-CHR-012 | BR-1, FR-3 |
| TC-CHR-304 | Deactivate custom field hides from forms but preserves JSONB data | Functional | Critical | US-CHR-012 | AC-5, FR-7, BR-3 |
| TC-CHR-305 | Reactivate custom field restores visibility with stored values intact | Functional | Critical | US-CHR-012 | AC-5, FR-7, BR-3 |
| TC-CHR-306 | Dropdown options -- adding succeeds; removing in-use option shows warning | Functional | High | US-CHR-012 | BR-6 |
| TC-CHR-307 | Field type immutable after data exists | Functional | Critical | US-CHR-012 | BR-5 |
| TC-CHR-308 | Only Tenant Admin can manage custom fields -- role-based access | Security | Critical | US-CHR-012 | Precondition Section 2 |
| TC-CHR-309 | Unauthenticated request to custom fields API returns 401 | Security | Critical | US-CHR-012 | NFR-2 |
| TC-CHR-310 | Input sanitization -- XSS in custom field name and dropdown options | Security | High | US-CHR-012 | NFR-2 |
| TC-CHR-311 | Custom field configuration API response times within SLA | Performance | High | US-CHR-012 | NFR-1 |
| TC-CHR-312 | JSONB query by custom field value within 500ms at 10,000 employees with GIN index | Performance | High | US-CHR-012 | NFR-3, FR-11 |
| TC-CHR-313 | Custom field definition changes are audited | Functional | High | US-CHR-012 | NFR-5 |
| TC-CHR-314 | Responsive 360px management page with arrow-button reorder | Functional | High | US-CHR-012 | NFR-4 |
| TC-CHR-315 | Custom field columns in directory export and bulk import (DEFERRED) | Functional | Medium | US-CHR-012 | FR-10 |
| TC-CHR-316 | WCAG 2.1 AA accessibility for custom fields management page | Accessibility | High | US-CHR-012 | NFR-4 |
| TC-CHR-317 | Cross-browser compatibility for custom fields management | Functional | Medium | US-CHR-012 | NFR-4 |
| TC-CHR-318 | All supported field types can be created and rendered | Functional | High | US-CHR-012 | FR-2 |
| TC-CHR-319 | field_key auto-generated from field name and immutable after creation | Functional | High | US-CHR-012 | Section 7, Section 10 |
| TC-CHR-320 | Custom field rendering on forms does not degrade page load by more than 200ms | Performance | High | US-CHR-012 | NFR-6 |
| TC-CHR-321 | Multi-select dropdown stores array value in JSONB | Functional | High | US-CHR-012 | FR-2, FR-4, FR-5 |
| TC-CHR-322 | Checkbox boolean custom field stores true/false in JSONB | Functional | High | US-CHR-012 | FR-2, FR-4 |
| TC-CHR-323 | Plan limit indicator displayed on management page (DEFERRED) | Functional | High | US-CHR-012 | BR-4, FR-6 |
| TC-CHR-324 | Same custom field name allowed in different tenants | Security | Critical | US-CHR-012 | BR-2 |
| TC-CHR-ISO-045 | Tenant A custom fields not visible to Tenant B | Security | Critical | US-CHR-012 | NFR-2, BR-2, FR-3 |
| TC-CHR-ISO-046 | API rejects custom field requests without valid tenant context | Security | Critical | US-CHR-012 | NFR-2 |
| TC-CHR-ISO-047 | RLS blocks direct DB queries across tenants for custom field definitions | Security | Critical | US-CHR-012 | NFR-2, FR-3 |
| TC-CHR-ISO-048 | Cache keys for custom field definitions are tenant-scoped | Security | Critical | US-CHR-012 | NFR-2 |


### US-CHR-011 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Reporting Manager field shows current manager or "Not Assigned"; edit allows search/autocomplete | AC | TC-CHR-268, TC-CHR-269, TC-CHR-292 | Direct |
| AC-2: Assign manager updates FK, employment history, audit log with before/after | AC | TC-CHR-268, TC-CHR-278, TC-CHR-280 | Direct |
| AC-3: Circular reporting chain detected and rejected with exact error message | AC | TC-CHR-272, TC-CHR-273, TC-CHR-290 | Direct |
| AC-4: Manager team dashboard with direct reports (name, title, dept, status, quick actions) | AC | TC-CHR-270 | Direct |
| AC-5: Bulk assign managers from directory; all updated; changes logged individually | AC | TC-CHR-271 | Direct |
| FR-1: Store reporting manager as FK (reports_to_employee_id), nullable | FR | TC-CHR-268, TC-CHR-276, TC-CHR-280 | Direct |
| FR-2: One direct reporting manager per employee | FR | TC-CHR-268, TC-CHR-280 | Direct |
| FR-3: Detect and prevent circular chains at any depth | FR | TC-CHR-272, TC-CHR-273, TC-CHR-274, TC-CHR-290 | Direct |
| FR-4: Bulk manager assignment for multiple employees | FR | TC-CHR-271, TC-CHR-291 | Direct |
| FR-5: My Team / direct reports view for managers | FR | TC-CHR-270, TC-CHR-282 | Direct |
| FR-6: Record every assignment change in employment history | FR | TC-CHR-268, TC-CHR-271, TC-CHR-278 | Direct |
| FR-7: Propagate to approval workflows | FR | -- | Deferred (pending Leave/Attendance/Performance modules) |
| FR-8: Allow no manager (nullable FK, org-tree root) | FR | TC-CHR-276 | Direct |
| FR-9: All queries tenant-scoped via RLS and EF Core | FR | TC-CHR-ISO-041, TC-CHR-ISO-042, TC-CHR-ISO-043, TC-CHR-ISO-044 | Direct |
| NFR-1: Assignment API <= 800ms P95 incl. cycle detection | NFR | TC-CHR-289 | Direct |
| NFR-2: Cycle detection within 200ms for hierarchies up to 500 deep | NFR | TC-CHR-290 | Direct |
| NFR-3: Tenant-isolated via RLS and EF Core global query filters | NFR | TC-CHR-ISO-041, TC-CHR-ISO-042, TC-CHR-ISO-043, TC-CHR-ISO-044 | Direct |
| NFR-4: UI fully responsive 360px to 4K | NFR | TC-CHR-293, TC-CHR-294 | Direct |
| NFR-5: Changes audited with before/after snapshots | NFR | TC-CHR-268, TC-CHR-278 | Direct |
| NFR-6: Bulk 100 employees within 5 seconds | NFR | TC-CHR-291 | Direct |
| BR-1: At most one direct reporting manager per employee | BR | TC-CHR-280 | Direct |
| BR-2: Unlimited direct reports per manager | BR | TC-CHR-281 | Direct |
| BR-3: Only active employees as managers | BR | TC-CHR-275 | Direct |
| BR-4: Manager termination triggers HR reassignment reminder | BR | TC-CHR-277 | Direct (notification dispatch DEFERRED) |
| BR-5: Cross-department reporting allowed | BR | TC-CHR-279 | Direct |
| BR-6: Manager assignment determines approval chain | BR | -- | Deferred (pending respective modules) |
| BR-7: Self-assignment not allowed | BR | TC-CHR-274 | Direct |

n### US-CHR-010 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Download template with headers matching schema, sample data, field descriptions (CSV + Excel) | AC | TC-CHR-239 | Direct |
| AC-2: Upload valid file, all rows imported with tenant_id from session and auto employee_no, success summary | AC | TC-CHR-240, TC-CHR-241, TC-CHR-252 | Direct |
| AC-3: Partial import with error report listing row number, field, error; downloadable CSV | AC | TC-CHR-242, TC-CHR-243, TC-CHR-244, TC-CHR-245, TC-CHR-257, TC-CHR-258, TC-CHR-259 | Direct |
| AC-4: Large file (>500 rows) queued as async Hangfire job, progress shown, user notified | AC | TC-CHR-249, TC-CHR-260 | Direct (email notification DEFERRED) |
| AC-5: Plan limit pre-validation warning with import-up-to-limit or cancel | AC | TC-CHR-248 | Direct |
| FR-1: Accept CSV and Excel uploads | FR | TC-CHR-240, TC-CHR-241, TC-CHR-247 | Direct |
| FR-2: Downloadable template with headers, sample data, descriptions | FR | TC-CHR-239 | Direct |
| FR-3: Row-level validation (required fields, types, email, dept/job title existence) | FR | TC-CHR-240, TC-CHR-242, TC-CHR-243, TC-CHR-244, TC-CHR-245, TC-CHR-257, TC-CHR-258 | Direct |
| FR-4: Partial import -- valid imported, invalid skipped and reported | FR | TC-CHR-242, TC-CHR-243, TC-CHR-244 | Direct |
| FR-5: Auto-generate employee_no per tenant pattern | FR | TC-CHR-240 | Direct |
| FR-6: tenant_id from session for all imported records | FR | TC-CHR-240, TC-CHR-252, TC-CHR-ISO-037 | Direct |
| FR-7: Files >500 rows async via Hangfire | FR | TC-CHR-249, TC-CHR-260 | Direct |
| FR-8: Downloadable error report CSV | FR | TC-CHR-242, TC-CHR-259 | Direct |
| FR-9: Plan-level employee count limits enforced | FR | TC-CHR-248 | Direct |
| FR-10: Import logged in audit trail with file name and counts | FR | TC-CHR-251 | Direct |
| FR-11: Custom field column mapping | FR | TC-CHR-267 | DEFERRED (US-CHR-012) |
| NFR-1: 10,000-row import within 5 minutes (async) | NFR | TC-CHR-249, TC-CHR-262 | Direct |
| NFR-2: All imported records tenant-isolated via RLS and EF Core | NFR | TC-CHR-252, TC-CHR-ISO-037, TC-CHR-ISO-038, TC-CHR-ISO-039, TC-CHR-ISO-040 | Direct |
| NFR-3: Idempotent -- re-upload same file no duplicates | NFR | TC-CHR-250 | Direct |
| NFR-4: Transaction behavior (sync all-or-nothing; async per-batch rollback) | NFR | TC-CHR-261 | Direct |
| NFR-5: Import UI responsive (360px to 4K) | NFR | TC-CHR-263, TC-CHR-264, TC-CHR-265 | Direct |
| NFR-6: Memory bounded -- stream/chunk-read large files | NFR | TC-CHR-262 | Direct (observational) |
| BR-1: tenant_id from session, never from file | BR | TC-CHR-252, TC-CHR-240 | Direct |
| BR-2: Duplicate emails within file flagged | BR | TC-CHR-243 | Direct |
| BR-3: Non-existent dept/job title causes row failure | BR | TC-CHR-244, TC-CHR-257 | Direct |
| BR-4: Default status active unless provided | BR | TC-CHR-255 | Direct |
| BR-5: Import does not create user accounts | BR | TC-CHR-256 | Direct |
| BR-6: ClosedXML for Excel, CsvHelper for CSV | BR | TC-CHR-241 | Indirect |
| BR-7: Max file size 25 MB | BR | TC-CHR-246 | Direct |
### US-CHR-009 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Status transition form shows available transitions based on current status; invalid transitions not shown | AC | TC-CHR-217, TC-CHR-218 | Direct |
| AC-2: Status change recorded in employment history with reason, effective date, and officer; audit log created | AC | TC-CHR-217, TC-CHR-230, TC-CHR-231 | Direct |
| AC-3: Terminated employee: login deactivated, removed from headcount, excluded from payroll, portal disabled, data retained | AC | TC-CHR-222, TC-CHR-237 | Direct |
| AC-4: Daily background job sends HR notification for approaching probation end; does NOT auto-transition | AC | TC-CHR-224 | Direct |
| AC-5: Invalid status transition rejected with exact error message | AC | TC-CHR-219, TC-CHR-223 | Direct |
| FR-1: System supports statuses: active, probation, suspended, terminated, inactive | FR | TC-CHR-218, TC-CHR-223, TC-CHR-233 | Direct |
| FR-2: Valid state machine enforced | FR | TC-CHR-218, TC-CHR-219, TC-CHR-223 | Direct |
| FR-3: Every status change requires reason and effective date | FR | TC-CHR-217, TC-CHR-220, TC-CHR-221 | Direct |
| FR-4: All status changes recorded in employment history | FR | TC-CHR-217, TC-CHR-230, TC-CHR-231 | Direct |
| FR-5: Side effects based on new status (portal access, leave accrual, payroll) | FR | TC-CHR-217, TC-CHR-222, TC-CHR-238 | Direct (leave/payroll deferred) |
| FR-6: Daily background job checks probation end dates within 7 days | FR | TC-CHR-224 | Direct (notification dispatch deferred if module not built) |
| FR-7: Status displayed as color-coded badge on profile and directory | FR | TC-CHR-233 | Direct |
| NFR-1: Status change API response time <= 800ms P95 | NFR | TC-CHR-234 | Direct |
| NFR-2: All status data tenant-isolated via RLS and EF Core global query filters | NFR | TC-CHR-ISO-033, TC-CHR-ISO-034, TC-CHR-ISO-035, TC-CHR-ISO-036 | Direct |
| NFR-3: Status changes idempotent via Idempotency-Key header | NFR | TC-CHR-226 | Direct |
| NFR-4: Status change UI fully responsive (360px to 4K) | NFR | TC-CHR-232, TC-CHR-236 | Direct |
| NFR-5: Status change operations fully audited with before/after snapshots | NFR | TC-CHR-217, TC-CHR-230 | Direct |
| BR-1: State machine enforced server-side; UI only presents valid transitions | BR | TC-CHR-218, TC-CHR-219, TC-CHR-223 | Direct |
| BR-2: Only HR Officers and Tenant Admins can change status | BR | TC-CHR-227, TC-CHR-228, TC-CHR-229 | Direct |
| BR-3: Terminated is terminal state; rehired employees get new record | BR | TC-CHR-219, TC-CHR-223 | Direct |
| BR-4: Future effective date stored but not applied until that date | BR | TC-CHR-225 | Direct |
| BR-5: Suspended employees excluded from active headcount; records retained | BR | TC-CHR-237 | Direct |
| BR-6: Probation periods configured per tenant (default 90 days) | BR | TC-CHR-224 | Direct |

### US-CHR-008 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Upload Document form with file selection (drag-and-drop or file picker), category, optional description, optional expiry date | AC | TC-CHR-192, TC-CHR-216 | Direct |
| AC-2: File stored in tenant-isolated object storage at `{tenantId}/core-hr/{employeeId}/{yyyy}/{mm}/{filename}`, metadata record with `tenant_id`, appears in list | AC | TC-CHR-192 | Direct |
| AC-3: Upload rejected for files exceeding 10 MB or with disallowed MIME type, with clear error message | AC | TC-CHR-194, TC-CHR-195, TC-CHR-196, TC-CHR-202 | Direct |
| AC-4: Download via short-lived signed URL (5-minute expiry) with authorization check; cross-tenant download returns 403 | AC | TC-CHR-193, TC-CHR-197 | Direct |
| AC-5: Expiry date stored; background job checks at 30/7/1 days and sends notifications to HR Officer and employee | AC | TC-CHR-204 | Direct |
| FR-1: Document upload with metadata (file, category, description, expiry date) | FR | TC-CHR-192, TC-CHR-215, TC-CHR-216 | Direct |
| FR-2: File size limits (default 10 MB) and MIME type whitelists | FR | TC-CHR-194, TC-CHR-195, TC-CHR-196, TC-CHR-202 | Direct |
| FR-3: Tenant-isolated object storage paths `{tenantId}/core-hr/{employeeId}/{yyyy}/{mm}/{filename}` | FR | TC-CHR-192, TC-CHR-198, TC-CHR-ISO-029, TC-CHR-ISO-032 | Direct |
| FR-4: Malware scan (ClamAV) before persisting storage reference | FR | TC-CHR-201 | Direct |
| FR-5: EXIF data stripped from image uploads | FR | TC-CHR-212 | Direct |
| FR-6: Short-lived signed download URLs (5-minute expiry) with authorization check | FR | TC-CHR-193, TC-CHR-197, TC-CHR-200, TC-CHR-207 | Direct |
| FR-7: Soft delete by HR Officer with audit trail | FR | TC-CHR-206 | Direct |
| FR-8: Document expiry tracking and notification jobs | FR | TC-CHR-203, TC-CHR-204 | Direct |
| FR-9: Categorized document list with file name, category, upload date, size, uploader, expiry date | FR | TC-CHR-192, TC-CHR-198, TC-CHR-200, TC-CHR-207, TC-CHR-213, TC-CHR-214 | Direct |
| FR-10: Employees view/download own docs; only HR Officers can upload/delete | FR | TC-CHR-193, TC-CHR-199, TC-CHR-200 | Direct |
| NFR-1: File upload within 5 seconds for 10 MB on stable connection | NFR | TC-CHR-209 | Direct |
| NFR-2: All document metadata and storage paths tenant-isolated via RLS, EF Core filters, storage path prefixing | NFR | TC-CHR-192, TC-CHR-198, TC-CHR-200, TC-CHR-201, TC-CHR-215, TC-CHR-ISO-029, TC-CHR-ISO-030, TC-CHR-ISO-031, TC-CHR-ISO-032 | Direct |
| NFR-3: Cross-tenant download attempts return 403 and trigger security alert | NFR | TC-CHR-197 | Direct |
| NFR-4: Storage usage counts toward tenant plan quota; uploads blocked at threshold with 80% warning | NFR | TC-CHR-205 | Direct (deferred pending Subscription module) |
| NFR-5: Document management UI fully responsive (360px to 4K) | NFR | TC-CHR-208, TC-CHR-210, TC-CHR-211 | Direct |
| NFR-6: Document access (view/download) logged in audit trail for compliance | NFR | TC-CHR-193, TC-CHR-207 | Direct |
| BR-1: Only HR Officers can upload and delete documents on any employee's record | BR | TC-CHR-192, TC-CHR-198, TC-CHR-199 | Direct |
| BR-2: Employees can view and download documents on their own record only | BR | TC-CHR-193, TC-CHR-197, TC-CHR-199 | Direct |
| BR-3: Managers cannot access employee documents unless explicitly granted permission | BR | TC-CHR-199 | Direct |
| BR-4: Document expiry notifications sent at 30, 7, and 1 days before expiry | BR | TC-CHR-204 | Direct |
| BR-5: Deleted documents are soft-deleted; file retained for configured retention period | BR | TC-CHR-206 | Direct |
| BR-6: System tracks total storage usage per tenant against plan limits | BR | TC-CHR-205 | Direct (deferred pending Subscription module) |
| BR-7: Supported file types: PDF, JPEG, PNG, DOCX, XLSX; executables always rejected | BR | TC-CHR-194, TC-CHR-195, TC-CHR-196 | Direct |

### US-CHR-007 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Add Location form with fields (name, address, time zone, phone, status) | AC | TC-CHR-172, TC-CHR-177, TC-CHR-189 | Direct |
| AC-2: New location created with tenant_id from session, appears in list and employee dropdowns | AC | TC-CHR-172, TC-CHR-173, TC-CHR-176 | Direct |
| AC-3: Deactivation blocked when active employees assigned with "X active employees" warning | AC | TC-CHR-178 | Direct |
| AC-4: Edited time zone saved, used for attendance/shift/holiday, recorded in audit log | AC | TC-CHR-174, TC-CHR-182, TC-CHR-185 | Direct |
| FR-1: CRUD operations on locations scoped to current tenant | FR | TC-CHR-172, TC-CHR-174, TC-CHR-179, TC-CHR-185, TC-CHR-186, TC-CHR-187 | Direct |
| FR-2: Unique location names within a tenant | FR | TC-CHR-175, TC-CHR-176 | Direct |
| FR-3: Structured address fields (street, city, state/province, country, postal code) | FR | TC-CHR-172, TC-CHR-177, TC-CHR-180 | Direct |
| FR-4: Required time zone in IANA format per location | FR | TC-CHR-174, TC-CHR-177, TC-CHR-180, TC-CHR-182 | Direct |
| FR-5: Prevent deactivation of locations with active employee assignments | FR | TC-CHR-178, TC-CHR-179 | Direct |
| FR-6: Soft delete for locations | FR | TC-CHR-179, TC-CHR-183 | Direct |
| FR-7: Display employee count per location | FR | TC-CHR-178, TC-CHR-184 | Direct |
| FR-8: All location data tenant-isolated via RLS and EF Core global query filters | FR | TC-CHR-176, TC-CHR-186, TC-CHR-ISO-025, TC-CHR-ISO-026, TC-CHR-ISO-027, TC-CHR-ISO-028 | Direct |
| NFR-1: Location CRUD API response time <= 400ms reads, <= 800ms writes (P95) | NFR | TC-CHR-188 | Direct |
| NFR-2: All location data tenant-isolated via RLS and EF Core global query filters | NFR | TC-CHR-176, TC-CHR-ISO-025, TC-CHR-ISO-026, TC-CHR-ISO-027, TC-CHR-ISO-028 | Direct |
| NFR-3: Management page fully responsive (360px to 4K) | NFR | TC-CHR-189, TC-CHR-190, TC-CHR-191 | Direct |
| NFR-4: Audit log entries for all location create, update, and deactivate operations | NFR | TC-CHR-172, TC-CHR-174, TC-CHR-179, TC-CHR-185 | Direct |
| BR-1: Location names unique within tenant, may repeat cross-tenant | BR | TC-CHR-175, TC-CHR-176 | Direct |
| BR-2: Each employee can be assigned to one primary location | BR | TC-CHR-173 | Direct |
| BR-3: Time zone drives attendance clock-in/out and shift boundaries | BR | TC-CHR-174, TC-CHR-182 | Direct |
| BR-4: Holiday calendars can be scoped to specific locations | BR | TC-CHR-173 | Direct |
| BR-5: Deactivated locations cannot be assigned to new employees but remain on existing records | BR | TC-CHR-178, TC-CHR-179, TC-CHR-183 | Direct |
| BR-6: A tenant can operate with zero locations defined | BR | TC-CHR-181 | Direct |

### US-CHR-006 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Interactive org chart with department hierarchy, manager avatars/names, employee counts | AC | TC-CHR-151, TC-CHR-170 | Direct |
| AC-2: Click department node shows detail panel with manager, employees, sub-departments, link to management page | AC | TC-CHR-152, TC-CHR-169 | Direct |
| AC-3: Toggle to Reporting Structure view shows manager-to-direct-report relationships | AC | TC-CHR-153, TC-CHR-170 | Direct |
| AC-4: Search for employee, tree highlights and auto-scrolls to matching node, path expanded | AC | TC-CHR-154, TC-CHR-161 | Direct |
| AC-5: Large tree uses lazy loading, smooth 60fps pan/zoom, no browser freeze | AC | TC-CHR-155, TC-CHR-160, TC-CHR-164, TC-CHR-165 | Direct |
| FR-1 through BR-5 | (unchanged -- see TEST-MATRIX.md for full detail) | | |

### US-CHR-003 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Paginated card/grid directory sorted by name ascending | AC | TC-CHR-127, TC-CHR-135, TC-CHR-146, TC-CHR-147 | Direct |
| AC-2: Search by partial name, email, employee_no, phone with 300ms debounce | AC | TC-CHR-128, TC-CHR-130, TC-CHR-142, TC-CHR-144 | Direct |
| AC-3: Filter by department + status with chips and URL params | AC | TC-CHR-129, TC-CHR-149, TC-CHR-150 | Direct |
| AC-4: Paginated results (default 20/page) with page controls and total count | AC | TC-CHR-127, TC-CHR-131, TC-CHR-132, TC-CHR-143 | Direct |
| AC-5: Export filtered list as CSV or Excel with matching columns, tenant-scoped | AC | TC-CHR-133, TC-CHR-134, TC-CHR-140, TC-CHR-145 | Direct |
| FR-1 through BR-5 | (unchanged -- see TEST-MATRIX.md for full detail) | | |

### US-CHR-002, US-CHR-001, US-CHR-004, US-CHR-005 Detailed Requirements Traceability

(Unchanged from previous version -- all detailed traceability tables for these stories remain as documented.)

### Coverage Summary (Core HR -- US-CHR-011)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 8/9 (89%) -- FR-7 deferred to Leave/Attendance/Performance modules | >= 85% | PASS |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/7 (86%) -- BR-6 deferred to respective modules | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 4 dedicated ISO (TC-CHR-ISO-041 through TC-CHR-ISO-044) | >= 3 | PASS |
| Security Test Cases | 9/31 (29%) + 4 ISO = 13/31 (41.9%) | >= 30% | PASS |
| Performance Test Cases | 3/31 | >= 1 | PASS |
| Accessibility Test Cases | 1/31 | >= 1 | PASS |
| Cross-Browser Test Cases | 2/31 | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-CHR-277 notification dispatch (pending Notification module), FR-7 approval workflow propagation (pending respective modules) | -- | NOTE |

n### Coverage Summary (Core HR -- US-CHR-010)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 10/11 (91%) -- FR-11 deferred to US-CHR-012 | >= 85% | PASS |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 7/7 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded TC-CHR-252) | >= 3 | PASS |
| Security Test Cases | 12/33 (36.4%) including ISO | >= 30% | PASS |
| Performance Test Cases | 2/33 | >= 1 | PASS |
| Accessibility Test Cases | 1/33 | >= 1 | PASS |
| Cross-Browser Test Cases | 2/33 | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-CHR-267 (custom field mapping pending US-CHR-012), TC-CHR-260 (email notification pending Notification module) | -- | NOTE |
### Coverage Summary (Core HR -- US-CHR-009)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 4 dedicated ISO (TC-CHR-ISO-033 through TC-CHR-ISO-036) | >= 3 | PASS |
| Security Test Cases | 9/26 (34.6%) | >= 30% | PASS |
| Performance Test Cases | 1/26 (TC-CHR-234) | >= 1 | PASS |
| Accessibility Test Cases | 1/26 (TC-CHR-235) | >= 1 | PASS |
| Cross-Browser Test Cases | 1/26 (TC-CHR-236) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | Payroll exclusion hook, notification dispatch, leave accrual resume (pending respective modules) | -- | NOTE |

### Coverage Summary (Core HR -- US-CHR-008)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 10/10 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 7/7 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 7 (4 dedicated ISO + 3 embedded in TC-CHR-192, TC-CHR-197, TC-CHR-198) | >= 3 | PASS |
| Security Test Cases | 12/29 (41.4%) | >= 30% | PASS |
| Performance Test Cases | 1/29 | >= 1 | PASS |
| Accessibility Test Cases | 1/29 | >= 1 | PASS |
| Cross-Browser Test Cases | 1/29 (TC-CHR-211) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-CHR-205 (storage quota -- pending Subscription module) | -- | NOTE |

### Coverage Summary (Core HR -- US-CHR-007)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 4/4 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 8/8 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded in TC-CHR-176) | >= 3 | PASS |
| Security Test Cases | 8/24 (33.3%) | >= 30% | PASS |
| Performance Test Cases | 1/24 | >= 1 | PASS |
| Accessibility Test Cases | 1/24 | >= 1 | PASS |
| Cross-Browser Test Cases | 2/24 | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |

### Coverage Summary (Core HR -- US-CHR-006)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 8/8 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 4 dedicated ISO | >= 3 | PASS |
| Security Test Cases | 6/25 (24%) + 4 ISO = 10/25 (40%) | >= 30% | PASS |
| Performance Test Cases | 2/25 | >= 1 | PASS |
| Accessibility Test Cases | 1/25 | >= 1 | PASS |
| Cross-Browser Test Cases | 2/25 | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |

### Coverage Summary (Core HR -- US-CHR-003)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 9/9 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) -- BR-2 scope deferred, BR-5 search ILIKE | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded) | >= 3 | PASS |
| Security Test Cases | 9/28 (32.1%) | >= 30% | PASS |
| Performance Test Cases | 3/28 | >= 1 | PASS |
| Accessibility Test Cases | 1/28 | >= 1 | PASS |
| Cross-Browser Test Cases | 2/28 | >= 1 | PASS |

### Coverage Summary (Core HR -- US-CHR-002)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 6/6 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/6 (83.3%) -- BR-5 deferred (configurable approval) | >= 85% | NOTE |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded) | >= 3 | PASS |
| Security Test Cases | 13/27 (48.1%) | >= 30% | PASS |
| Performance Test Cases | 2/27 | >= 1 | PASS |
| Accessibility Test Cases | 1/27 | >= 1 | PASS |
| Cross-Browser Test Cases | 2/27 | >= 1 | PASS |

### Coverage Summary (Core HR -- US-CHR-001)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 6/6 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 9/9 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 11 (4 dedicated ISO + 7 embedded) | >= 3 | PASS |
| Security Test Cases | 15/44 (34.1%) | >= 30% | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |

### Coverage Summary (Core HR -- US-CHR-004)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 8/8 (100%) -- FR-4 now unblocked | >= 85% | PASS |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) -- BR-2 now unblocked | >= 85% | PASS |
| Blocked Test Cases | 0 (TC-CHR-020 unblocked by US-CHR-001) | -- | CLEAR |

### Coverage Summary (Core HR -- US-CHR-005)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-4 and FR-7 now unblocked | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Blocked Test Cases | 0 (TC-CHR-043, TC-CHR-049, TC-CHR-063 unblocked by US-CHR-001) | -- | CLEAR |

### US-CHR-012 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Custom fields management page shows field list with name, type, required/optional, usage count; Add button | AC | TC-CHR-295, TC-CHR-298, TC-CHR-316 | Direct |
| AC-2: Created custom field immediately appears on employee creation and profile edit forms | AC | TC-CHR-295, TC-CHR-296, TC-CHR-318 | Direct |
| AC-3: Custom field value stored in JSONB column, retrievable and editable on profile | AC | TC-CHR-297, TC-CHR-321, TC-CHR-322 | Direct |
| AC-4: Plan limit reached blocks new field with upgrade message | AC | TC-CHR-300 | DEFERRED (Subscription module) |
| AC-5: Deactivate hides field; data preserved; reactivate restores with values intact | AC | TC-CHR-304, TC-CHR-305 | Direct |
| FR-1: Define custom fields per entity type (Employee Phase 1) | FR | TC-CHR-295, TC-CHR-308 | Direct |
| FR-2: 10 field types supported | FR | TC-CHR-318, TC-CHR-295, TC-CHR-321, TC-CHR-322 | Direct |
| FR-3: Definitions stored in tenant-scoped configuration table | FR | TC-CHR-295, TC-CHR-303, TC-CHR-ISO-047 | Direct |
| FR-4: Values stored in custom_fields JSONB column | FR | TC-CHR-297, TC-CHR-321, TC-CHR-322 | Direct |
| FR-5: Validation against type, required status, dropdown options | FR | TC-CHR-301, TC-CHR-302 | Direct |
| FR-6: Plan-level limits enforced | FR | TC-CHR-300 | DEFERRED (Subscription module) |
| FR-7: Deactivate without deleting stored data | FR | TC-CHR-304, TC-CHR-305 | Direct |
| FR-8: Reorder custom fields for display order | FR | TC-CHR-299 | Direct |
| FR-9: Dynamic rendering on relevant forms | FR | TC-CHR-295, TC-CHR-296, TC-CHR-318 | Direct |
| FR-10: Include in directory export and bulk import | FR | TC-CHR-315 | DEFERRED (pending integration) |
| FR-11: GIN index on JSONB column | FR | TC-CHR-312 | Direct (observational) |
| NFR-1: Config API read <= 400ms, write <= 800ms (P95) | NFR | TC-CHR-311 | Direct |
| NFR-2: Tenant-isolated via RLS and EF Core global query filters | NFR | TC-CHR-324, TC-CHR-ISO-045, TC-CHR-ISO-046, TC-CHR-ISO-047, TC-CHR-ISO-048 | Direct |
| NFR-3: JSONB query within 500ms for 10k employees with GIN index | NFR | TC-CHR-312 | Direct (observational) |
| NFR-4: Management page fully responsive (360px to 4K) | NFR | TC-CHR-314, TC-CHR-317 | Direct |
| NFR-5: Definition changes audited | NFR | TC-CHR-295, TC-CHR-313 | Direct |
| NFR-6: Form rendering does not degrade page load by more than 200ms | NFR | TC-CHR-320 | Direct (observational) |
| BR-1: Field names unique within tenant + entity | BR | TC-CHR-303 | Direct |
| BR-2: Definitions are tenant-specific | BR | TC-CHR-324, TC-CHR-ISO-045 | Direct |
| BR-3: Deactivating does not remove stored JSONB values | BR | TC-CHR-304, TC-CHR-305 | Direct |
| BR-4: Plan limits: 5 (Starter), 20 (Professional), unlimited (Enterprise) | BR | TC-CHR-300, TC-CHR-323 | DEFERRED (Subscription module) |
| BR-5: Field types cannot be changed after data exists | BR | TC-CHR-307 | Direct |
| BR-6: Dropdown options removable only if not in use | BR | TC-CHR-306 | Direct |
| BR-7: Custom fields not in full-text search (Phase 1); filterable via advanced filters | BR | TC-CHR-312 | Indirect |

### Coverage Summary (Core HR -- US-CHR-012)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 9/11 (82%) -- FR-6 deferred (Subscription), FR-10 deferred (export/import) | >= 85% | NOTE (cross-module deps) |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/7 (71%) -- BR-4 deferred (Subscription), BR-7 indirect | >= 85% | NOTE (BR-4 cross-module) |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded TC-CHR-324) | >= 3 | PASS |
| Security Test Cases | 12/34 (35.3%) including 4 ISO | >= 30% | PASS |
| Performance Test Cases | 3/34 (TC-CHR-311, TC-CHR-312, TC-CHR-320) | >= 1 | PASS |
| Accessibility Test Cases | 1/34 (TC-CHR-316) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/34 (TC-CHR-314, TC-CHR-317) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-CHR-300, TC-CHR-323 (plan limits -- Subscription module), TC-CHR-315 (export/import integration) | -- | NOTE |


---


## Leave Management Module

### Forward Traceability (User Stories --> Test Cases)

| User Story ID | User Story Title | Priority | Test Cases | TC Count | Coverage |
|---------------|-----------------|----------|------------|----------|----------|
| US-LV-001 | Configure Leave Types Per Tenant | Must Have | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-009, TC-LV-010, TC-LV-011, TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-016, TC-LV-017, TC-LV-018, TC-LV-019, TC-LV-020, TC-LV-021, TC-LV-022, TC-LV-023, TC-LV-024, TC-LV-025 | 25 | 5/5 AC covered |
| Cross-cutting (LV-001) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 4 | -- |
| US-LV-002 | Set Yearly Leave Entitlements by Job Level/Department | Must Have | TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-033, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-038, TC-LV-039, TC-LV-040, TC-LV-041, TC-LV-042, TC-LV-043, TC-LV-044, TC-LV-045, TC-LV-046, TC-LV-047 | 22 | 5/5 AC covered |
| Cross-cutting (LV-002) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 4 | -- |
| US-LV-003 | Employee Applies for Leave | Must Have | TC-LV-048, TC-LV-049, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-055, TC-LV-056, TC-LV-057, TC-LV-058, TC-LV-059, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-063, TC-LV-064, TC-LV-065 | 18 | 6/6 AC covered |
| Cross-cutting (LV-003) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | 4 | -- |
| US-LV-004 | Manager Views Pending Leave Queue with Balance Inline | Must Have | TC-LV-066, TC-LV-067, TC-LV-068, TC-LV-069, TC-LV-070, TC-LV-071, TC-LV-072, TC-LV-073, TC-LV-074, TC-LV-075, TC-LV-076, TC-LV-077, TC-LV-078, TC-LV-079, TC-LV-080, TC-LV-081, TC-LV-082, TC-LV-083, TC-LV-084, TC-LV-085, TC-LV-086, TC-LV-087, TC-LV-088 | 23 | 5/5 AC covered |
| Cross-cutting (LV-004) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015, TC-LV-ISO-016 | 4 | -- |
| US-LV-005 | Manager Approves or Rejects Leave Request | Must Have | TC-LV-089, TC-LV-090, TC-LV-091, TC-LV-092, TC-LV-093, TC-LV-094, TC-LV-095, TC-LV-096, TC-LV-097, TC-LV-098, TC-LV-099, TC-LV-100, TC-LV-101, TC-LV-102, TC-LV-103, TC-LV-104, TC-LV-105, TC-LV-106, TC-LV-107, TC-LV-108 | 20 | 5/5 AC covered |
| Cross-cutting (LV-005) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020 | 4 | -- |
| US-LV-006 | Leave Balance Dashboard for Employee | Must Have | TC-LV-109, TC-LV-110, TC-LV-111, TC-LV-112, TC-LV-113, TC-LV-114, TC-LV-115, TC-LV-116, TC-LV-117, TC-LV-118, TC-LV-119, TC-LV-120, TC-LV-121, TC-LV-122, TC-LV-123, TC-LV-124, TC-LV-125, TC-LV-126, TC-LV-127, TC-LV-128 | 20 | 5/5 AC covered |
| Cross-cutting (LV-006) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024 | 4 | -- |
| US-LV-007 | Holiday Calendar Management Per Tenant | Must Have | TC-LV-129, TC-LV-130, TC-LV-131, TC-LV-132, TC-LV-133, TC-LV-134, TC-LV-135, TC-LV-136, TC-LV-137, TC-LV-138, TC-LV-139, TC-LV-140, TC-LV-141, TC-LV-142, TC-LV-143, TC-LV-144, TC-LV-145, TC-LV-146, TC-LV-147, TC-LV-148 | 20 | 4/4 AC covered |
| Cross-cutting (LV-007) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-025, TC-LV-ISO-026, TC-LV-ISO-027, TC-LV-ISO-028 | 4 | -- |
| US-LV-008 | Leave Carry-Forward and Expiry Rules | Should Have | TC-LV-149, TC-LV-150, TC-LV-151, TC-LV-152, TC-LV-153, TC-LV-154, TC-LV-155, TC-LV-156, TC-LV-157, TC-LV-158, TC-LV-159, TC-LV-160, TC-LV-161, TC-LV-162, TC-LV-163, TC-LV-164, TC-LV-165, TC-LV-166, TC-LV-167, TC-LV-168 | 20 | 5/5 AC covered |
| Cross-cutting (LV-008) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-029, TC-LV-ISO-030, TC-LV-ISO-031, TC-LV-ISO-032 | 4 | -- |
| US-LV-009 | Team Leave Calendar View | Should Have | TC-LV-169, TC-LV-170, TC-LV-171, TC-LV-172, TC-LV-173, TC-LV-174, TC-LV-175, TC-LV-176, TC-LV-177, TC-LV-178, TC-LV-179, TC-LV-180, TC-LV-181, TC-LV-182, TC-LV-183, TC-LV-184, TC-LV-185, TC-LV-186, TC-LV-187, TC-LV-188 | 20 | 4/4 AC covered |
| Cross-cutting (LV-009) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-033, TC-LV-ISO-034, TC-LV-ISO-035, TC-LV-ISO-036 | 4 | -- |
| US-LV-010 | Leave Cancellation by Employee | Must Have | TC-LV-189, TC-LV-190, TC-LV-191, TC-LV-192, TC-LV-193, TC-LV-194, TC-LV-195, TC-LV-196, TC-LV-197, TC-LV-198, TC-LV-199, TC-LV-200, TC-LV-201, TC-LV-202, TC-LV-203, TC-LV-204, TC-LV-205, TC-LV-206, TC-LV-207, TC-LV-208, TC-LV-209 | 21 | 4/4 AC covered |
| Cross-cutting (LV-010) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-037, TC-LV-ISO-038, TC-LV-ISO-039, TC-LV-ISO-040 | 4 | -- |
| US-LV-011 | Compulsory Leave / Loss of Pay (LOP) Handling | Should Have | TC-LV-210, TC-LV-211, TC-LV-212, TC-LV-213, TC-LV-214, TC-LV-215, TC-LV-216, TC-LV-217, TC-LV-218, TC-LV-219, TC-LV-220, TC-LV-221, TC-LV-222, TC-LV-223, TC-LV-224, TC-LV-225, TC-LV-226, TC-LV-227, TC-LV-228, TC-LV-229, TC-LV-230, TC-LV-231 | 22 | 4/4 AC covered |
| Cross-cutting (LV-011) | Multi-tenant isolation (mandatory) | Critical | TC-LV-ISO-041, TC-LV-ISO-042, TC-LV-ISO-043, TC-LV-ISO-044 | 4 | -- |
| **TOTAL** | | | **275 test cases** | **275** | **52/52 AC** |

### Backward Traceability (Test Cases --> User Stories)

| Test Case ID | Test Case Title | Type | Priority | User Story | Requirements Covered |
|-------------|----------------|------|----------|------------|---------------------|
| TC-LV-001 | Create a leave type with full configuration (happy path) | Functional | Critical | US-LV-001 | AC-1, FR-1, FR-2, NFR-2, NFR-3, BR-1 |
| TC-LV-002 | Edit leave type entitlement and carry-forward with audit trail | Functional | Critical | US-LV-001 | AC-2, FR-1, FR-2, NFR-3, BR-5 |
| TC-LV-003 | Duplicate leave type name rejected (case-insensitive) | Functional | Critical | US-LV-001 | AC-3, FR-1, BR-1 |
| TC-LV-004 | Deactivate leave type -- hidden from apply dropdown, existing requests unaffected | Functional | Critical | US-LV-001 | AC-4, FR-1, FR-5, BR-5 |
| TC-LV-005 | Configure documents-required threshold and enforcement on apply | Functional | Critical | US-LV-001 | AC-5, FR-1, FR-2 |
| TC-LV-006 | Negative entitlement rejected; zero allowed for unpaid leave | Functional | Critical | US-LV-001 | AC-1, FR-1, FR-2, BR-3 |
| TC-LV-007 | Invalid color, gender, and accrual frequency values rejected | Functional | High | US-LV-001 | AC-1, FR-2 |
| TC-LV-008 | Boundary -- max field values and name/code length limits | Functional | High | US-LV-001 | AC-1, FR-2, Section 7 |
| TC-LV-009 | Reorder leave types via display_order | Functional | High | US-LV-001 | FR-3, Section 8 |
| TC-LV-010 | Gender-specific leave type only shown to matching gender employees | Functional | High | US-LV-001 | FR-2, BR-4 |
| TC-LV-011 | Cannot hard-delete a leave type referenced by requests (soft delete only) | Functional | Critical | US-LV-001 | FR-1, FR-5, BR-2 |
| TC-LV-012 | Same leave type name allowed in different tenants (cross-tenant uniqueness) | Security | Critical | US-LV-001 | AC-1, FR-1, NFR-2, BR-1 |
| TC-LV-013 | Only Leave.Configure / Tenant Admin can manage leave types (role check) | Security | Critical | US-LV-001 | Preconditions Section 2, NFR-2 |
| TC-LV-014 | Unauthenticated request to leave types API returns 401 | Security | Critical | US-LV-001 | Preconditions Section 2, US-AUTH-* |
| TC-LV-015 | Input sanitization -- XSS in leave type name and description | Security | High | US-LV-001 | NFR-2 |
| TC-LV-016 | Leave type list API response within 200ms P95 | Performance | High | US-LV-001 | NFR-1 |
| TC-LV-017 | Audit trail captures before/after JSON on configuration changes | Functional | High | US-LV-001 | AC-2, NFR-3 |
| TC-LV-018 | Responsive UI at 360px -- stacked form and accordion Advanced section | Functional | High | US-LV-001 | NFR-4, Section 8 |
| TC-LV-019 | WCAG 2.1 AA accessibility for leave type configuration page | Accessibility | High | US-LV-001 | NFR-4 |
| TC-LV-020 | Cross-browser compatibility for leave types management page | Functional | Medium | US-LV-001 | NFR-4 |
| TC-LV-021 | New tenant gets default leave types on provisioning (DEFERRED) | Functional | High | US-LV-001 | FR-4, Section 10 |
| TC-LV-022 | Required fields validation -- name, code, entitlement missing | Functional | High | US-LV-001 | AC-1, FR-1, FR-2 |
| TC-LV-023 | Leave type write API response within 800ms P95 | Performance | High | US-LV-001 | NFR-1 |
| TC-LV-024 | Create all accrual frequency types (monthly, quarterly, yearly, upfront) | Functional | High | US-LV-001 | AC-1, FR-2 |
| TC-LV-025 | Negative balance configuration -- allowed with limit and disallowed | Functional | High | US-LV-001 | FR-2, BR-3 |
| TC-LV-026 | Create entitlement rule mapping leave type to department and job level (happy path) | Functional | Critical | US-LV-002 | AC-1, FR-1, FR-2, NFR-2, BR-1 |
| TC-LV-027 | Rule priority -- most specific rule wins when overlapping rules exist | Functional | Critical | US-LV-002 | AC-2, FR-2, BR-1 |
| TC-LV-028 | Per-employee override takes precedence over all rule-based entitlements | Functional | Critical | US-LV-002 | AC-3, FR-2 |
| TC-LV-029 | Pro-rata entitlement calculation for mid-year joiners | Functional | Critical | US-LV-002 | AC-4, FR-3 |
| TC-LV-030 | Modify entitlement rule triggers Hangfire recalculation and audit log | Functional | Critical | US-LV-002 | AC-5, FR-5, NFR-1 |
| TC-LV-031 | Part-time FTE proration (DEFERRED -- FTE field pending) | Functional | High | US-LV-002 | BR-2, FR-1 |
| TC-LV-032 | Probation employee only accrues probation_eligible leave types | Functional | Critical | US-LV-002 | BR-3, FR-1 |
| TC-LV-033 | Entitlement cannot be negative -- minimum clamped to zero | Functional | High | US-LV-002 | BR-4 |
| TC-LV-034 | Department transfer mid-year triggers pro-rata recalculation for both periods | Functional | High | US-LV-002 | BR-5, FR-3 |
| TC-LV-035 | Leave year configuration -- calendar year vs fiscal year per tenant | Functional | High | US-LV-002 | BR-1 |
| TC-LV-036 | Hangfire accrual job creates correct leave_ledger entries | Functional | Critical | US-LV-002 | AC-1, FR-5, Section 7 |
| TC-LV-037 | Bulk entitlement assignment UI for mass updates | Functional | High | US-LV-002 | FR-4 |
| TC-LV-038 | Entitlement rule CRUD validation -- invalid inputs rejected | Functional | High | US-LV-002 | FR-1, BR-4 |
| TC-LV-039 | Only Leave.Configure permission can manage entitlement rules | Security | Critical | US-LV-002 | Preconditions Section 2, NFR-2 |
| TC-LV-040 | Unauthenticated request to entitlement API returns 401 | Security | Critical | US-LV-002 | Preconditions Section 2 |
| TC-LV-041 | Entitlement recalculation for 5,000 employees within 60 seconds | Performance | Critical | US-LV-002 | NFR-1 |
| TC-LV-042 | Redis cache for leave balances with 24h TTL and invalidation (DEFERRED) | Performance | High | US-LV-002 | NFR-3, FR-6 |
| TC-LV-043 | Responsive UI -- entitlement matrix collapses to card list on mobile | Functional | High | US-LV-002 | Section 8 |
| TC-LV-044 | WCAG 2.1 AA accessibility for entitlement configuration page | Accessibility | High | US-LV-002 | Section 8 |
| TC-LV-045 | Cross-browser compatibility for entitlement configuration page | Functional | Medium | US-LV-002 | Section 8 |
| TC-LV-046 | Job-level dimension in entitlement rules (DEFERRED) | Functional | High | US-LV-002 | FR-1, Section 7 |
| TC-LV-047 | Input sanitization -- XSS in entitlement rule and override fields | Security | High | US-LV-002 | NFR-2 |
| TC-LV-ISO-001 | Tenant A cannot see Tenant B's leave types | Security | Critical | US-LV-001 | NFR-2, BR-1 |
| TC-LV-ISO-002 | API rejects leave type requests without valid tenant context | Security | Critical | US-LV-001 | NFR-2 |
| TC-LV-ISO-003 | RLS blocks direct DB queries across tenants for leave types | Security | Critical | US-LV-001 | NFR-2, Section 7 |
| TC-LV-ISO-004 | Cache keys for leave types are tenant-scoped | Security | Critical | US-LV-001 | NFR-1, NFR-2 |
| TC-LV-ISO-005 | Tenant A cannot see Tenant B's entitlement rules or overrides | Security | Critical | US-LV-002 | NFR-2 |
| TC-LV-ISO-006 | API rejects entitlement requests without valid tenant context | Security | Critical | US-LV-002 | NFR-2 |
| TC-LV-ISO-007 | RLS blocks direct DB queries across tenants for entitlement data | Security | Critical | US-LV-002 | NFR-2 |
| TC-LV-ISO-008 | Cache keys for leave balances are tenant-scoped (DEFERRED -- partial) | Security | Critical | US-LV-002 | NFR-2, NFR-3, FR-6 |
| TC-LV-048 | Submit a valid leave request (happy path) -- Pending, confirmation, notification queued | Functional | Critical | US-LV-003 | AC-1, FR-1, FR-5, FR-6, BR-6 |
| TC-LV-049 | Real-time balance display on leave type and date selection | Functional | High | US-LV-003 | AC-2, FR-2, FR-3, NFR-2 |
| TC-LV-050 | Submission blocked when balance insufficient and negative balance not allowed | Functional | Critical | US-LV-003 | AC-2, FR-2, NFR-2 |
| TC-LV-051 | Sick leave over document threshold without attachment is rejected | Functional | Critical | US-LV-003 | AC-3, FR-1 |
| TC-LV-052 | Overlapping dates with existing Pending/Approved request are rejected | Functional | Critical | US-LV-003 | AC-5, FR-4 |
| TC-LV-053 | Leave request for past date beyond lookback window is rejected | Functional | High | US-LV-003 | BR-1 |
| TC-LV-054 | Leave request for future date beyond future window is rejected | Functional | High | US-LV-003 | BR-2 |
| TC-LV-055 | Half-day leave created as 0.5 days and decrements balance accordingly | Functional | Critical | US-LV-003 | AC-4, FR-1 |
| TC-LV-056 | Public holidays and weekends excluded from leave day count | Functional | Critical | US-LV-003 | AC-6, FR-3 (holiday exclusion depends on US-LV-007) |
| TC-LV-057 | Maximum consecutive leave days enforced per leave type config | Functional | High | US-LV-003 | BR-3 |
| TC-LV-058 | Gender-restricted leave type not visible/appliable to ineligible employees | Security | Critical | US-LV-003 | BR-4 |
| TC-LV-059 | Probation employee can only see/apply for probation_eligible leave types | Functional | High | US-LV-003 | BR-5 |
| TC-LV-060 | User without Leave.Apply permission is denied leave submission | Security | Critical | US-LV-003 | Preconditions Section 2 |
| TC-LV-061 | Unauthenticated request to leave submission API returns 401 | Security | Critical | US-LV-003 | Preconditions Section 2, US-AUTH-* |
| TC-LV-062 | Input sanitization -- XSS payload in the leave reason field | Security | High | US-LV-003 | NFR-4 |
| TC-LV-063 | Attachment validation (type, size, count) and tenant-scoped storage path | Functional | High | US-LV-003 | FR-1, NFR-3, Section 10 |
| TC-LV-064 | Leave submission API responds within 500ms P95 | Performance | High | US-LV-003 | NFR-1, NFR-2 |
| TC-LV-065 | Leave application form usable on mobile 360px+ and WCAG 2.1 AA accessible | Accessibility | High | US-LV-003 | NFR-5, Section 8 |
| TC-LV-ISO-009 | Employee in Tenant A cannot submit/view leave via Tenant B's context | Security | Critical | US-LV-003 | NFR-4 |
| TC-LV-ISO-010 | API rejects leave requests without a valid tenant context | Security | Critical | US-LV-003 | NFR-4 |
| TC-LV-ISO-011 | EF global query filters block cross-tenant access to leave_request rows | Security | Critical | US-LV-003 | NFR-4, Section 7 |
| TC-LV-ISO-012 | Balance cache keys and attachment storage paths are tenant-scoped | Security | Critical | US-LV-003 | NFR-2, NFR-3 |
| TC-LV-066 | Pending queue loads sorted oldest-first with inline balance (happy path) | Functional | Critical | US-LV-004 | AC-1, FR-1, FR-2, BR-1, BR-4 |
| TC-LV-067 | Manager with no direct reports / no pending requests sees empty queue | Functional | High | US-LV-004 | AC-1, FR-1, FR-4, BR-1 |
| TC-LV-068 | Pagination boundary -- 25 requests return 20 on page 1, 5 on page 2 | Functional | Critical | US-LV-004 | AC-2, FR-4 |
| TC-LV-069 | Page size capped at 50 | Functional | High | US-LV-004 | AC-2, FR-4, Section 10 |
| TC-LV-070 | Invalid/out-of-range pagination parameters handled safely | Functional | High | US-LV-004 | AC-2, FR-4, Section 10 |
| TC-LV-071 | Filter the queue by leave type returns only matching requests | Functional | High | US-LV-004 | AC-3, FR-3, BR-1 |
| TC-LV-072 | Filter the queue by employee returns only that employee's requests | Functional | High | US-LV-004 | AC-3, FR-3, BR-1 |
| TC-LV-073 | Filter the queue by date range returns only overlapping requests | Functional | High | US-LV-004 | AC-3, FR-3 |
| TC-LV-074 | Filter returning no matches shows empty state, not an error | Functional | High | US-LV-004 | AC-3, FR-3, FR-4 |
| TC-LV-075 | Sort the queue by requested date or start date | Functional | Medium | US-LV-004 | AC-1, AC-3, FR-3 |
| TC-LV-076 | Overdue boundary -- 31-day request flagged overdue, 29-day not | Functional | High | US-LV-004 | AC-1, BR-3, Section 8 |
| TC-LV-077 | Detail panel shows full details, attachments, balance, history, team-calendar | Functional | Critical | US-LV-004 | AC-4, FR-2, FR-5 (history/team-calendar depend on US-LV-009) |
| TC-LV-078 | Team conflict count shown on overlapping request | Functional | High | US-LV-004 | AC-4, FR-5 |
| TC-LV-079 | New request appears on queue refresh; SignalR push deferred | Functional | High | US-LV-004 | AC-5, FR-6 (real-time push depends on notifications module) |
| TC-LV-080 | Inline balance pill matches current balance with color thresholds | Functional | High | US-LV-004 | AC-1, FR-2, NFR-2, BR-4, Section 8 |
| TC-LV-081 | Manager scope -- Manager A sees only direct reports, not Manager B's team | Security | Critical | US-LV-004 | FR-1, NFR-3, BR-1 |
| TC-LV-082 | User without Leave.Approve.Team is denied the pending queue | Security | Critical | US-LV-004 | Preconditions Section 2, NFR-3 |
| TC-LV-083 | Unauthenticated request to pending queue API returns 401 | Security | Critical | US-LV-004 | Preconditions Section 2, US-AUTH-* |
| TC-LV-084 | Input sanitization -- malicious filter/query params (SQLi/XSS) | Security | High | US-LV-004 | NFR-3, FR-3 |
| TC-LV-085 | Pending queue API responds within 300ms P95 using ix_leave_pending | Performance | High | US-LV-004 | NFR-1, Section 7 |
| TC-LV-086 | Queue and detail panel usable on mobile 360px+ and WCAG 2.1 AA accessible | Accessibility | High | US-LV-004 | NFR-4, Section 8 |
| TC-LV-087 | Cross-browser compatibility for the queue and detail panel | Functional | Medium | US-LV-004 | NFR-4, Section 8 |
| TC-LV-088 | Multi-level approval -- queue shows requests at manager's approval level | Functional | Medium | US-LV-004 | BR-1, BR-2 (multi-level workflow forward-looking) |
| TC-LV-ISO-013 | Manager in Tenant A cannot see Tenant B's pending requests | Security | Critical | US-LV-004 | NFR-3, BR-1 |
| TC-LV-ISO-014 | API rejects pending-queue requests without valid tenant context | Security | Critical | US-LV-004 | NFR-3, US-AUTH-007 |
| TC-LV-ISO-015 | EF global query filters block cross-tenant access to pending leave_request rows | Security | Critical | US-LV-004 | NFR-3, Section 7 |
| TC-LV-ISO-016 | Inline-balance cache keys for the pending queue are tenant-scoped (DEFERRED -- partial) | Security | Critical | US-LV-004 | NFR-2, Section 7 |
| TC-LV-089 | Approve -- status Approved, used-ledger entry, balance decreased, audit, notification queued (happy path) | Functional | Critical | US-LV-005 | AC-1, FR-1, FR-3, FR-7, BR-5 |
| TC-LV-090 | Reject with mandatory reason -- status Rejected, no ledger, audit, notification with reason, reason in approval history | Functional | Critical | US-LV-005 | AC-2, FR-2, FR-4, FR-7, BR-2 |
| TC-LV-091 | Optional approval comment persisted; approval succeeds without a comment | Functional | High | US-LV-005 | AC-1, FR-1, FR-5, BR-2 |
| TC-LV-092 | Approval blocked when balance insufficient and negative not allowed | Functional | Critical | US-LV-005 | AC-3, BR-5, FR-3 |
| TC-LV-093 | Approval with insufficient balance prompts confirmation when negative allowed | Functional | High | US-LV-005 | AC-3, BR-5, FR-3 |
| TC-LV-094 | Rejection with empty/missing reason rejected with validation error | Functional | Critical | US-LV-005 | BR-2, FR-2 |
| TC-LV-095 | Already-actioned (Rejected/Approved) request cannot be re-actioned | Functional | Critical | US-LV-005 | BR-3, FR-1, FR-2 |
| TC-LV-096 | Concurrent approve/reject -- only first succeeds, second gets 409 (xmin) | Functional | Critical | US-LV-005 | AC-5, FR-6, NFR-4 |
| TC-LV-097 | Multi-level approval -- first approval -> Pending L2, notifies next approver (CONDITIONAL on US-ADM-007) | Functional | High | US-LV-005 | AC-4, FR-5 (multi-level CONDITIONAL on approval-workflow config US-ADM-007) |
| TC-LV-098 | Approving leave for a payroll-locked period is blocked (CONDITIONAL on payroll module) | Functional | High | US-LV-005 | BR-4 (CONDITIONAL on payroll module period-lock) |
| TC-LV-099 | Only the designated approver can action -- another manager denied | Security | Critical | US-LV-005 | BR-1, FR-1, FR-2 |
| TC-LV-100 | User without Leave.Approve.Team is denied approve/reject | Security | Critical | US-LV-005 | Preconditions Section 2, NFR-3 |
| TC-LV-101 | Unauthenticated request to approve/reject API returns 401 | Security | Critical | US-LV-005 | Preconditions Section 2, US-AUTH-* |
| TC-LV-102 | Input sanitization -- XSS/SQLi in approval comment and rejection reason | Security | High | US-LV-005 | NFR-3, FR-1, FR-2 |
| TC-LV-103 | Approve/Reject API responds within 500ms P95 | Performance | High | US-LV-005 | NFR-1, NFR-2 |
| TC-LV-104 | Approve/Reject detail-panel actions usable on mobile 360px+ and WCAG 2.1 AA (labeled mandatory-reason, error announced) | Accessibility | High | US-LV-005 | Section 8, BR-2 |
| TC-LV-105 | Audit log records Leave.Approved/Leave.Rejected with before/after JSON | Functional | High | US-LV-005 | FR-7, NFR-3 |
| TC-LV-106 | A request cancelled by the employee cannot be approved or rejected | Functional | High | US-LV-005 | Preconditions Section 2, BR-3 |
| TC-LV-107 | Notification queuing asynchronous and best-effort -- decision commits even if queuing fails | Functional | Medium | US-LV-005 | NFR-2, Section 10 (notification dispatch DEFERRED) |
| TC-LV-108 | Cross-browser compatibility for the approve/reject flow | Functional | Medium | US-LV-005 | Section 8, NFR-1 |
| TC-LV-ISO-017 | Manager in Tenant A cannot approve or reject Tenant B's request | Security | Critical | US-LV-005 | NFR-3, BR-1 |
| TC-LV-ISO-018 | API rejects approve/reject requests without a valid tenant context | Security | Critical | US-LV-005 | NFR-3, US-AUTH-007 |
| TC-LV-ISO-019 | EF global query filters block cross-tenant access to leave_request/approval_history/ledger rows during approval | Security | Critical | US-LV-005 | NFR-3, Section 7 |
| TC-LV-ISO-020 | Balance-cache keys invalidated on approval are tenant-scoped (DEFERRED -- partial) | Security | Critical | US-LV-005 | NFR-2, NFR-3, FR-3 |
| TC-LV-109 | Dashboard loads a summary card per active leave type (entitlement/used/pending/balance + progress bar) (happy path) | Functional | Critical | US-LV-006 | AC-1, FR-1, FR-2 |
| TC-LV-110 | Summary card values and progress bar are accurate | Functional | Critical | US-LV-006 | AC-1, FR-2, BR-1, BR-2 |
| TC-LV-111 | Clicking a balance card opens the ledger/transaction history (happy path) | Functional | Critical | US-LV-006 | AC-2, FR-3 |
| TC-LV-112 | Ledger renders all transaction types (accrual/used/adjusted/carry-forward/expired) for the year | Functional | High | US-LV-006 | AC-2, FR-3, BR-1 |
| TC-LV-113 | Upcoming Leaves lists approved and pending future requests with dates/type/status/days (happy path) | Functional | High | US-LV-006 | AC-3, FR-4 |
| TC-LV-114 | Submitting a leave increases "pending" but not "balance" until approval | Functional | Critical | US-LV-006 | AC-1, FR-2, BR-2 |
| TC-LV-115 | Balance correctness across carry-forward, expiry, and adjustments (BR-1 formula) | Functional | Critical | US-LV-006 | AC-1, FR-2, FR-5, BR-1 |
| TC-LV-116 | Only active leave types shown; deactivated-with-balance in collapsed Archived section | Functional | High | US-LV-006 | AC-1, FR-1, BR-3 |
| TC-LV-117 | Year selector switches to a previous leave year (read-only) | Functional | High | US-LV-006 | AC-1, AC-2, FR-2, FR-3, BR-5 |
| TC-LV-118 | Leave-year boundary respects tenant calendar vs fiscal-year config | Functional | High | US-LV-006 | AC-1, AC-2, FR-2, FR-3, BR-4 |
| TC-LV-119 | New joiner with no ledger data sees a friendly empty state | Functional | High | US-LV-006 | AC-5, FR-1 |
| TC-LV-120 | Leave history section lists and filters past requests (approved/rejected/cancelled) | Functional | High | US-LV-006 | AC-3, FR-6 |
| TC-LV-121 | Cache miss -- balance computed from ledger and re-cached (Redis DEFERRED; DB-fallback verified) | Functional | High | US-LV-006 | FR-5, NFR-1 (Redis cache DEFERRED) |
| TC-LV-122 | Self-scope -- employee cannot view another employee's balance/ledger/upcoming | Security | Critical | US-LV-006 | NFR-3, FR-1, FR-3, FR-4 |
| TC-LV-123 | Unauthenticated request to balance/ledger/upcoming APIs returns 401 | Security | Critical | US-LV-006 | Preconditions Section 2, NFR-3, US-AUTH-* |
| TC-LV-124 | Input sanitization -- malicious year/leaveTypeId params (SQLi/XSS) rejected/neutralized | Security | High | US-LV-006 | NFR-3, FR-3 |
| TC-LV-125 | Balance API responds within 200ms P95 (Redis DEFERRED; DB-fallback path measured) | Performance | High | US-LV-006 | NFR-1, FR-5 (Redis cache DEFERRED) |
| TC-LV-126 | Dashboard achieves LCP under 2.5 seconds | Performance | High | US-LV-006 | NFR-2, Section 8 |
| TC-LV-127 | Mobile 360px -- cards stack, remain readable, progress bars scale | Functional | High | US-LV-006 | AC-4, NFR-2, Section 8 |
| TC-LV-128 | WCAG 2.1 AA -- progress bars have aria-labels; color not the sole indicator | Accessibility | High | US-LV-006 | NFR-4, Section 8 |
| TC-LV-ISO-021 | Employee in Tenant A sees only their own balance data; Tenant B invisible | Security | Critical | US-LV-006 | NFR-3, FR-1, FR-3, FR-4 |
| TC-LV-ISO-022 | API rejects balance/ledger/upcoming requests without a valid tenant context | Security | Critical | US-LV-006 | NFR-3, US-AUTH-007 |
| TC-LV-ISO-023 | EF global query filters block cross-tenant access to leave_ledger/leave_request rows | Security | Critical | US-LV-006 | NFR-3, Section 7 |
| TC-LV-ISO-024 | Balance cache keys are tenant- and employee-scoped (Redis DEFERRED -- partial) | Security | Critical | US-LV-006 | NFR-1, NFR-3, FR-5 |
| TC-LV-129 | Add a holiday (name, date, type, locations) -- saved, tenant-scoped (happy path) | Functional | Critical | US-LV-007 | AC-1, FR-1, FR-2 |
| TC-LV-130 | Holiday visible to employees, location-filtered | Functional | High | US-LV-007 | AC-1, FR-1 |
| TC-LV-131 | Holiday excludes its date from leave-day count (Mon-Fri spanning a Wed holiday = 4 days) | Integration | Critical | US-LV-007 | AC-2, FR-6 |
| TC-LV-132 | Only Public holidays auto-excluded (restricted/optional are not) | Functional | High | US-LV-007 | AC-2, FR-6, BR-2 |
| TC-LV-133 | Location-scoped holiday does not reduce another location's leave count | Functional | High | US-LV-007 | AC-2, FR-6, BR-2 |
| TC-LV-134 | CSV import -- valid rows created (happy path) | Functional | Critical | US-LV-007 | AC-3, FR-4 |
| TC-LV-135 | CSV import -- duplicate dates flagged/skipped | Functional | High | US-LV-007 | AC-3, FR-4, BR-1 |
| TC-LV-136 | CSV import handles up to 100 rows within 5 seconds | Performance | High | US-LV-007 | NFR-3, FR-4 |
| TC-LV-137 | Dual view -- color-coded month/year calendar + list | Functional | High | US-LV-007 | AC-4, FR-2 |
| TC-LV-138 | Duplicate same-date/location holiday rejected (location-specific unique index) | Functional | High | US-LV-007 | BR-1, FR-1 |
| TC-LV-139 | Duplicate tenant-wide (null-location) holiday rejected (partial unique index) | Functional | High | US-LV-007 | BR-1, FR-1 |
| TC-LV-140 | Holiday in finalized payroll period cannot be deleted, only deactivated (CONDITIONAL) | Functional | High | US-LV-007 | BR-4 (delete-lock CONDITIONAL on payroll module) |
| TC-LV-141 | Recurring holidays auto-generate next year (Hangfire, idempotent) | Functional | High | US-LV-007 | FR-3, BR-5 |
| TC-LV-142 | Restricted/optional holiday semantics and optional-leave-type linkage | Functional | High | US-LV-007 | BR-2, BR-3, FR-2 |
| TC-LV-143 | Deactivate/reactivate holiday retains record; tenant-scoped | Functional | High | US-LV-007 | AC-1, FR-1, BR-4 |
| TC-LV-144 | Onboarding holiday seeding with country template (DEFERRED -- wizard UNWIRED) | Functional | High | US-LV-007 | FR-5 (DEFERRED; seeding service verified) |
| TC-LV-145 | Only authorized roles can manage holidays (authz) | Security | Critical | US-LV-007 | NFR-2, Preconditions Section 2 |
| TC-LV-146 | Unauthenticated request to holidays API returns 401 | Security | Critical | US-LV-007 | NFR-2, US-AUTH-* |
| TC-LV-147 | Holiday list API for a year within 200ms P95 (Redis DEFERRED; DB-fallback measured) | Performance | High | US-LV-007 | NFR-1, FR-6 (Redis cache DEFERRED) |
| TC-LV-148 | Calendar view responsive/accessible on mobile (WCAG 2.1 AA) | Accessibility | High | US-LV-007 | AC-4, NFR-4 |
| TC-LV-ISO-025 | Holidays in Tenant A invisible to Tenant B | Security | Critical | US-LV-007 | NFR-2 |
| TC-LV-ISO-026 | API rejects holiday requests without a valid tenant context | Security | Critical | US-LV-007 | NFR-2 |
| TC-LV-ISO-027 | EF global query filters block cross-tenant access to holiday rows | Security | Critical | US-LV-007 | NFR-2, Section 7 |
| TC-LV-ISO-028 | Holiday cache keys are tenant-scoped (Redis DEFERRED -- partial) | Security | Critical | US-LV-007 | NFR-1, NFR-2 |
| TC-LV-149 | Year-end carry-forward applies up to the configured limit (happy path) | Functional | Critical | US-LV-008 | AC-1, FR-1, FR-2 |
| TC-LV-150 | Carry-forward capped at the configured limit; excess forfeited | Functional | Critical | US-LV-008 | AC-1, FR-2, BR-1 |
| TC-LV-151 | Year-end/expiry job writes ledger entries; Redis invalidation (DEFERRED; DB/ledger verified) | Integration | Critical | US-LV-008 | FR-7 (Redis invalidation DEFERRED) |
| TC-LV-152 | Carry-forward expiry forfeits unused carried days after the expiry window | Functional | Critical | US-LV-008 | AC-2, FR-3, BR-2 |
| TC-LV-153 | Expiry timing respects carry_forward_expiry_months config | Functional | High | US-LV-008 | AC-2, FR-3, BR-2 |
| TC-LV-154 | Encashment-on-expiry path (CONDITIONAL on leave-type config) | Functional | High | US-LV-008 | FR-4 (CONDITIONAL on encashable leave-type config) |
| TC-LV-155 | Zero/negative carry-forward limit handled (no carry-forward) | Functional | High | US-LV-008 | FR-2, BR-1 |
| TC-LV-156 | Idempotent year-end job -- re-run does not double-apply carry-forward | Functional | Critical | US-LV-008 | FR-1, FR-5 |
| TC-LV-157 | Carry-forward/expiry reflected in balance and ledger | Functional | High | US-LV-008 | AC-1, AC-2, FR-6 |
| TC-LV-158 | Preview report projects carry-forward/forfeiture before running | Functional | High | US-LV-008 | AC-5, FR-6 |
| TC-LV-159 | Year-end job processes large employee population within SLA | Performance | High | US-LV-008 | NFR-1 |
| TC-LV-160 | Boundary -- carry-forward exactly at the limit | Functional | High | US-LV-008 | FR-2, BR-1 |
| TC-LV-161 | Only authorized roles can configure/run carry-forward rules (authz) | Security | Critical | US-LV-008 | NFR-2, Preconditions Section 2 |
| TC-LV-162 | Unauthenticated request to carry-forward APIs returns 401 | Security | Critical | US-LV-008 | NFR-2, US-AUTH-* |
| TC-LV-163 | Input sanitization on carry-forward/preview params | Security | High | US-LV-008 | NFR-2 |
| TC-LV-164 | Preview/dashboard accessibility (WCAG 2.1 AA) | Accessibility | High | US-LV-008 | NFR-4, Section 8 |
| TC-LV-165 | Carry-forward interaction with mid-year adjustments | Functional | High | US-LV-008 | FR-6, BR-1 |
| TC-LV-166 | Multiple leave types each carry-forward per their own config | Functional | High | US-LV-008 | FR-1, FR-2 |
| TC-LV-167 | Leave-year boundary -- fiscal-year (CONDITIONAL; calendar-year verified) | Functional | High | US-LV-008 | BR-3 (fiscal-year CONDITIONAL on tenant config) |
| TC-LV-168 | Preview filters + dashboard line items (carry-forward/expired, expiring-soon) | Functional | High | US-LV-008 | AC-5, Section 8 (US-LV-006 integration) |
| TC-LV-ISO-029 | Carry-forward data in Tenant A invisible to Tenant B | Security | Critical | US-LV-008 | NFR-2 |
| TC-LV-ISO-030 | Year-end job processes each tenant in isolation (no cross-tenant carry-forward) | Security | Critical | US-LV-008 | NFR-2 |
| TC-LV-ISO-031 | EF global query filters block cross-tenant ledger/balance access | Security | Critical | US-LV-008 | NFR-2, Section 7 |
| TC-LV-ISO-032 | Carry-forward balance cache keys are tenant- and employee-scoped (Redis DEFERRED -- partial) | Security | Critical | US-LV-008 | NFR-2, FR-7 |
| TC-LV-169 | Manager month view shows direct reports' approved + pending leaves as colored blocks (happy path) | Functional | Critical | US-LV-009 | AC-1, FR-1, FR-2, FR-4, FR-5, BR-2 |
| TC-LV-170 | Month grid -- one color-coded block per employee/type with leave-type legend | Functional | High | US-LV-009 | AC-1, FR-4, FR-5, Section 8 |
| TC-LV-171 | Employee view -- approved department leaves only, no pending, no leave-type ("on leave") | Functional | Critical | US-LV-009 | AC-2, FR-3, NFR-3, BR-1 |
| TC-LV-172 | Employee API payload excludes pending + leave-type (server-side data-leak probe) | Security | Critical | US-LV-009 | AC-2, FR-3, FR-4, NFR-3, BR-1 |
| TC-LV-173 | Manager week view -- Gantt-like grid (employees Y-axis, days X-axis) | Functional | High | US-LV-009 | AC-3, FR-5, Section 8 |
| TC-LV-174 | Mobile 360px -- compact list grouped by date (employee, type, status) | Functional | High | US-LV-009 | AC-4, FR-5, NFR-4, Section 8 |
| TC-LV-175 | Manager scope limited to direct reports -- other managers' teams not shown | Security | Critical | US-LV-009 | BR-2, FR-1, FR-2, NFR-3 |
| TC-LV-176 | HR Officer with Leave.ViewAll sees the entire organization's calendar | Security | High | US-LV-009 | BR-3, FR-1, FR-2, NFR-3 |
| TC-LV-177 | Cancelled leaves are not shown on the calendar | Functional | High | US-LV-009 | BR-4, FR-1, FR-2, FR-3 |
| TC-LV-178 | Half-day leaves visually differentiated (half-block / AM-PM) | Functional | High | US-LV-009 | BR-5, FR-4 |
| TC-LV-179 | Public holidays appear as background highlights (US-LV-007 integration) | Integration | High | US-LV-009 | FR-7, Section 8 (depends on US-LV-007) |
| TC-LV-180 | Filters by employee, leave type, status (status manager-only) | Functional | High | US-LV-009 | FR-6, BR-1, BR-2, Section 8 |
| TC-LV-181 | Team-calendar API carries documented item fields (manager full / employee suppressed) | Functional | High | US-LV-009 | FR-4, FR-1, BR-1 |
| TC-LV-182 | Date-range (from/to) and boundary handling | Functional | High | US-LV-009 | FR-1, FR-4, Section 7 |
| TC-LV-183 | Auth/authz on the team-calendar endpoint; unauthenticated denied | Security | Critical | US-LV-009 | NFR-3, BR-1, BR-2, BR-3, US-AUTH-* |
| TC-LV-184 | Input sanitization on team-calendar query params | Security | High | US-LV-009 | NFR-3, FR-1, FR-6 |
| TC-LV-185 | Employee cannot escalate scope via parameter tampering | Security | Critical | US-LV-009 | AC-2, BR-1, BR-2, NFR-3 |
| TC-LV-186 | Month-range API within 300ms P95 (Redis DEFERRED; DB-backed path measured) | Performance | High | US-LV-009 | NFR-1, Section 7 (Redis cache DEFERRED) |
| TC-LV-187 | Renders smoothly with 50 employees / 200 entries | Performance | High | US-LV-009 | NFR-4 |
| TC-LV-188 | Keyboard/screen-reader; non-color cues; usable at 360px+ (WCAG 2.1 AA) | Accessibility | High | US-LV-009 | AC-4, NFR-4, Section 8 |
| TC-LV-ISO-033 | Calendar data from Tenant A must not appear in Tenant B | Security | Critical | US-LV-009 | NFR-2 |
| TC-LV-ISO-034 | API rejects team-calendar requests without a valid tenant context | Security | Critical | US-LV-009 | NFR-2 |
| TC-LV-ISO-035 | EF global query filters block cross-tenant leave_request/employee rows in the calendar | Security | Critical | US-LV-009 | NFR-2, Section 7 |
| TC-LV-ISO-036 | Team-calendar cache keys are tenant- and scope-scoped (Redis DEFERRED -- partial) | Security | Critical | US-LV-009 | NFR-1, NFR-2 |
| TC-LV-189 | Cancel a PENDING request -- Cancelled, no ledger entry, manager notification, audit (happy path) | Functional | Critical | US-LV-010 | AC-1, FR-1, FR-2, FR-5, FR-6, BR-5 |
| TC-LV-190 | Cancel an APPROVED future request with reason -- reversal `adjusted` (+) ledger restores balance, notification, audit | Functional | Critical | US-LV-010 | AC-2, FR-1, FR-3, FR-4, FR-5, FR-6, BR-5 (Redis invalidation DEFERRED) |
| TC-LV-191 | Reversal restores the exact deducted amount (incl. half-day); balance + dashboard agree | Functional | High | US-LV-010 | AC-2, FR-3 (US-LV-006 integration) |
| TC-LV-192 | Cancelling an already-STARTED approved leave is blocked with the contact-HR message | Functional | Critical | US-LV-010 | AC-3, BR-3, FR-7 |
| TC-LV-193 | Start-date boundary -- today=started (blocked), tomorrow=cancellable, past=blocked | Functional | High | US-LV-010 | AC-3, BR-3, FR-7 |
| TC-LV-194 | Cancelling in a payroll-locked period is blocked (CONDITIONAL on payroll module) | Functional | High | US-LV-010 | AC-4 (CONDITIONAL on payroll module) |
| TC-LV-195 | A REJECTED leave cannot be cancelled | Functional | High | US-LV-010 | BR-2, FR-1, FR-2 |
| TC-LV-196 | An ALREADY-CANCELLED leave cannot be cancelled again -- no double reversal | Functional | High | US-LV-010 | BR-2, FR-2, FR-3 |
| TC-LV-197 | Reason MANDATORY for an approved leave -- missing/blank reason rejected | Functional | High | US-LV-010 | BR-5, FR-1 |
| TC-LV-198 | Reason OPTIONAL for a pending leave -- cancel succeeds with or without a reason | Functional | Medium | US-LV-010 | BR-5, FR-2, FR-6 |
| TC-LV-199 | A MANAGER cannot cancel a leave on behalf of an employee -- 403 | Security | Critical | US-LV-010 | BR-1, NFR-2, Section 10 |
| TC-LV-200 | Another employee cannot cancel someone else's leave -- 403/404, no IDOR | Security | Critical | US-LV-010 | BR-1, NFR-2 |
| TC-LV-201 | Concurrent manager-approve vs employee-cancel -- only one succeeds (xmin 409) | Functional | Critical | US-LV-010 | NFR-3, Section 10 |
| TC-LV-202 | Cancelling a carry-forward-consuming leave restores the carry-forward pool (CONDITIONAL) | Functional | High | US-LV-010 | BR-4, FR-3 (CONDITIONAL; US-LV-008 integration) |
| TC-LV-203 | Tenant-configurable cancellation window -- allow up to N days before start (CONDITIONAL) | Functional | Medium | US-LV-010 | FR-7, AC-3 (N>0 CONDITIONAL on tenant-settings; default verified) |
| TC-LV-204 | Audit log captures before/after state of the cancelled request | Functional | High | US-LV-010 | NFR-4, FR-6, AC-1, AC-2 |
| TC-LV-205 | Cancel API contract -- body, response envelope, 404 for unknown id | Functional | High | US-LV-010 | FR-1, FR-2, FR-6, Section 7 |
| TC-LV-206 | Unauthenticated cancellation request returns 401 | Security | Critical | US-LV-010 | NFR-2, US-AUTH-* |
| TC-LV-207 | Cancellation reason sanitized -- XSS/SQL payloads stored + rendered safely | Security | High | US-LV-010 | NFR-2, FR-2, FR-5, FR-6 |
| TC-LV-208 | Cancellation API within 500ms P95 | Performance | High | US-LV-010 | NFR-1 (Redis invalidation DEFERRED) |
| TC-LV-209 | Cancel confirm dialog -- keyboard/screen-reader, labeled mandatory reason, 360px+ (WCAG 2.1 AA) | Accessibility | High | US-LV-010 | Section 8, BR-5 |
| TC-LV-ISO-037 | An employee in Tenant A cannot cancel a leave request in Tenant B | Security | Critical | US-LV-010 | NFR-2 |
| TC-LV-ISO-038 | API rejects a cancellation request without a valid tenant context | Security | Critical | US-LV-010 | NFR-2 |
| TC-LV-ISO-039 | EF global query filters block cross-tenant leave_request/leave_ledger access on cancel | Security | Critical | US-LV-010 | NFR-2, Section 7 |
| TC-LV-ISO-040 | Balance cache keys invalidated on cancel are tenant- and employee-scoped (Redis DEFERRED -- partial) | Security | Critical | US-LV-010 | NFR-2, FR-4 |
| TC-LV-210 | Zero-balance application offered as LOP; on confirm creates request leave_type=LOP, is_lop=true (happy path) | Functional | Critical | US-LV-011 | AC-1, FR-1, FR-4, BR-1 |
| TC-LV-211 | Declining the LOP prompt creates NO leave request (negative path) | Functional | High | US-LV-011 | AC-1, FR-4 |
| TC-LV-212 | LOP prompt suppressed when the leave type allows negative balance (boundary) | Functional | High | US-LV-011 | AC-1, BR-1 |
| TC-LV-213 | Absenteeism job auto-generates a System-Generated LOP entry (CONDITIONAL on Attendance) | Integration | High | US-LV-011 | AC-2, FR-2, FR-4 (CONDITIONAL on US-ATTENDANCE-*) |
| TC-LV-214 | Absenteeism job idempotent -- no duplicate LOP entries on re-run (CONDITIONAL on Attendance) | Integration | High | US-LV-011 | AC-2, FR-2 (CONDITIONAL on US-ATTENDANCE-*) |
| TC-LV-215 | HR manually assigns LOP -- leave_request (HR-Assigned) + ledger + notification (happy path) | Functional | Critical | US-LV-011 | AC-3, FR-3, FR-4, BR-6 |
| TC-LV-216 | assign-lop accepts multiple dates and validates them (boundary / input validation) | Functional | High | US-LV-011 | AC-3, FR-3, FR-4 |
| TC-LV-217 | lop-summary returns the data payroll consumes; deduction calc (CONDITIONAL on Payroll) | Integration | Critical | US-LV-011 | AC-4, FR-5, BR-2 (CONDITIONAL on US-PAYROLL-*) |
| TC-LV-218 | LOP is a system leave type -- auto-created, non-deletable, renamable | Functional | High | US-LV-011 | FR-1 |
| TC-LV-219 | Compulsory leave bulk-assign -- deduct balance first, LOP only on shortfall | Functional | High | US-LV-011 | FR-6, BR-4 |
| TC-LV-220 | LOP has no entitlement/balance -- pure deduction mechanism | Functional | High | US-LV-011 | BR-1 |
| TC-LV-221 | HR overrides a System-Generated LOP -- convert to another type or remove | Functional | High | US-LV-011 | AC-2, BR-3 |
| TC-LV-222 | LOP entries immutable once payroll finalized (CONDITIONAL on payroll lock) | Functional | High | US-LV-011 | NFR-3, BR-5, BR-3 (CONDITIONAL on US-PAYROLL-*) |
| TC-LV-223 | Audit trail + notification for ALL LOP assignments (auto/manual/compulsory) | Security | High | US-LV-011 | NFR-4, BR-6 (notification dispatch DEFERRED) |
| TC-LV-224 | Authz -- user without Leave.Manage/HR.Officer cannot assign/override LOP (403) | Security | Critical | US-LV-011 | §2, FR-3, FR-6, US-AUTH-* |
| TC-LV-225 | Unauthenticated requests to LOP endpoints return 401 | Security | Critical | US-LV-011 | NFR-2, US-AUTH-* |
| TC-LV-226 | Input sanitization -- LOP reason fields safe (XSS / SQL injection) | Security | High | US-LV-011 | NFR-2, FR-3, FR-6 |
| TC-LV-227 | assign-lop / lop-summary reject a cross-tenant employeeId -- no IDOR | Security | Critical | US-LV-011 | NFR-2, FR-3, FR-5 |
| TC-LV-228 | Auto-LOP job 5,000 employees within 3 minutes (CONDITIONAL on Attendance source) | Performance | High | US-LV-011 | NFR-1, FR-2 (CONDITIONAL on US-ATTENDANCE-*) |
| TC-LV-229 | assign-lop (write) and lop-summary (read) within platform API SLAs | Performance | Medium | US-LV-011 | FR-3, FR-5 |
| TC-LV-230 | LOP management screen keyboard/SR accessible; bulk actions navigable; non-color LOP cue (WCAG 2.1 AA) | Accessibility | High | US-LV-011 | §8, WCAG 2.1 AA |
| TC-LV-231 | LOP management screen cross-browser + responsive 360px--1920px | E2E | Medium | US-LV-011 | §8 |
| TC-LV-ISO-041 | LOP data in Tenant A not visible to / affecting Tenant B | Security | Critical | US-LV-011 | NFR-2 |
| TC-LV-ISO-042 | API rejects LOP requests without a valid tenant context | Security | Critical | US-LV-011 | NFR-2 |
| TC-LV-ISO-043 | EF global query filters block cross-tenant leave_request/leave_ledger during LOP ops | Security | Critical | US-LV-011 | NFR-2, §7 |
| TC-LV-ISO-044 | LOP/balance cache keys are tenant- and employee-scoped (Redis DEFERRED -- partial) | Security | Critical | US-LV-011 | NFR-2 |
| TC-LV-232 | Balance Summary report -- per-employee balance per leave type, filterable by dept/job level/employment type, CSV/Excel exportable | Functional | Critical | US-LV-012 | AC-1, FR-1, FR-2, FR-4, FR-6 |
| TC-LV-233 | Balance Summary values reconcile with the individual employee dashboard (US-LV-006) | Integration | Critical | US-LV-012 | AC-1, BR-3 |
| TC-LV-234 | Utilization report -- totals by type, average utilization %, department breakdown with bar/pie charts | Functional | Critical | US-LV-012 | AC-2, FR-1, FR-7, NFR-4 |
| TC-LV-235 | Utilization math -- 200 entitlement / 80 used -> 40% (zero-entitlement guarded) | Functional | High | US-LV-012 | AC-2, §7 |
| TC-LV-236 | Absenteeism report -- top absentees (unplanned + LOP), trend lines, flagged over threshold | Functional | High | US-LV-012 | AC-3, FR-1, FR-7, BR-4 |
| TC-LV-237 | Absenteeism flag -- 4 unplanned vs threshold 3 -> flagged; threshold tenant-configurable | Functional | High | US-LV-012 | AC-3, BR-4 |
| TC-LV-238 | Trend Analysis -- 12-month monthly trends by type with year-over-year comparison | Functional | High | US-LV-012 | AC-4, FR-1, FR-7, NFR-4 |
| TC-LV-239 | Synchronous CSV/Excel export -- 100-row report has correct headers + data, honors filters | Functional | Critical | US-LV-012 | AC-5, FR-4, NFR-2 |
| TC-LV-240 | Large export >5,000 rows -> Hangfire background job + notify (blob/notification CONDITIONAL) | Integration | High | US-LV-012 | AC-5, FR-5 (blob + notification CONDITIONAL/DEFERRED) |
| TC-LV-241 | Filter by department "Engineering" -> only Engineering employees | Functional | High | US-LV-012 | FR-2, FR-6, AC-1, AC-2 |
| TC-LV-242 | Full FR-2 filter set -- date range, job level, employment type, leave type, employee search | Functional | High | US-LV-012 | FR-2, FR-6 (job-level CONDITIONAL on JobLevel entity) |
| TC-LV-243 | Reports support sorting + server-side pagination | Functional | High | US-LV-012 | FR-3, FR-6, NFR-1 |
| TC-LV-244 | Remaining pre-built reports -- Carry-Forward Summary, LOP Summary, Dept Calendar Coverage | Functional | Medium | US-LV-012 | FR-1, FR-6 |
| TC-LV-245 | Role-based access -- HR sees all tenant employees | Functional | Critical | US-LV-012 | BR-2, AC-1, AC-2, AC-3 |
| TC-LV-246 | Role-based access -- manager sees only their team; tampering cannot widen scope | Security | Critical | US-LV-012 | BR-2 |
| TC-LV-247 | Role-based access -- employee sees only their own data; no cross-employee IDOR | Security | Critical | US-LV-012 | BR-2 |
| TC-LV-248 | Report API ≤2s P95 for ≤1,000 rows (read-replica/materialized-view CONDITIONAL) | Performance | High | US-LV-012 | NFR-1, FR-8, §7 (CONDITIONAL/DEFERRED) |
| TC-LV-249 | Synchronous export of ≤5,000 rows completes ≤10s | Performance | High | US-LV-012 | NFR-2, FR-4, FR-5 |
| TC-LV-250 | Authz -- user without Leave.Reports/HR.Officer denied (403) | Security | Critical | US-LV-012 | §2, BR-2, FR-6, FR-7, US-AUTH-* |
| TC-LV-251 | Unauthenticated requests to report/analytics/export endpoints return 401 | Security | Critical | US-LV-012 | NFR-2, NFR-3, US-AUTH-* |
| TC-LV-252 | Input sanitization on filters + cross-tenant/cross-team IDOR on employeeId/departmentId | Security | High | US-LV-012 | NFR-2, NFR-3, BR-1, BR-2, FR-2 |
| TC-LV-253 | Real-time balances (Redis DEFERRED, DB-fallback) + prior-year historical reports | Functional | Medium | US-LV-012 | BR-3, BR-5 (Redis DEFERRED) |
| TC-LV-254 | Reports accessible + print-friendly; charts carry non-color cues / data labels (WCAG 2.1 AA) | Accessibility | High | US-LV-012 | NFR-4, NFR-5, §8 |
| TC-LV-255 | Reports cross-browser + responsive 360px--1920px | E2E | Medium | US-LV-012 | §8, NFR-4, NFR-5 |
| TC-LV-ISO-045 | HR in Tenant A cannot see Tenant B data in any report/analytics/export | Security | Critical | US-LV-012 | NFR-3, BR-1 |
| TC-LV-ISO-046 | API rejects report/analytics requests without a valid tenant context | Security | Critical | US-LV-012 | NFR-3 |
| TC-LV-ISO-047 | EF global query filters block cross-tenant aggregation in report queries | Security | Critical | US-LV-012 | NFR-3, §7 (materialized-view filtering CONDITIONAL) |
| TC-LV-ISO-048 | Export blob path + cache keys tenant-scoped (Blob/Redis DEFERRED -- partial) | Security | Critical | US-LV-012 | NFR-3, FR-5 |

### US-LV-001 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Create leave type with full config, tenant-scoped | AC | TC-LV-001, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-012, TC-LV-022, TC-LV-024 | Direct |
| AC-2: Edit entitlement/carry-forward with audit trail, effective next cycle | AC | TC-LV-002, TC-LV-017 | Direct |
| AC-3: Duplicate name rejected case-insensitive | AC | TC-LV-003 | Direct |
| AC-4: Deactivate hides from dropdown, existing requests unaffected | AC | TC-LV-004 | Direct |
| AC-5: Documents-required threshold enforced on apply | AC | TC-LV-005 | Direct |
| FR-1: CRUD operations for leave types scoped to tenant_id | FR | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-011, TC-LV-012, TC-LV-022 | Direct |
| FR-2: All configurable fields supported | FR | TC-LV-001, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-010, TC-LV-022, TC-LV-024, TC-LV-025 | Direct |
| FR-3: Leave types orderable via display_order | FR | TC-LV-009 | Direct |
| FR-4: Default leave types seeded during tenant onboarding | FR | TC-LV-021 | DEFERRED (onboarding wizard not implemented) |
| FR-5: Soft delete -- deactivated types hidden from forms but retained | FR | TC-LV-004, TC-LV-011 | Direct |
| NFR-1: Leave type list API <= 200ms P95 with Redis cache; cache invalidation on write | NFR | TC-LV-016, TC-LV-023, TC-LV-ISO-004 | Direct (cache steps DEFERRED if not implemented) |
| NFR-2: Tenant-isolated via EF Core global query filters and PostgreSQL RLS | NFR | TC-LV-012, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | Direct |
| NFR-3: Config changes audit-logged with before/after JSON | NFR | TC-LV-002, TC-LV-017 | Direct |
| NFR-4: UI fully responsive 360px to 4K | NFR | TC-LV-018, TC-LV-019, TC-LV-020 | Direct |
| BR-1: Leave type names unique within tenant (case-insensitive) | BR | TC-LV-003, TC-LV-012 | Direct |
| BR-2: Cannot hard-delete if leave requests reference it; deactivate only | BR | TC-LV-011 | Direct (forward-looking; leave-request module pending) |
| BR-3: Entitlement must be positive; zero allowed for unpaid | BR | TC-LV-006, TC-LV-025 | Direct |
| BR-4: Gender-specific types shown only to matching gender employees | BR | TC-LV-010 | Direct (employee-facing filtering forward-looking) |
| BR-5: Config changes do not retroactively affect approved requests | BR | TC-LV-002, TC-LV-004 | Direct |

### US-LV-002 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Create entitlement rule, matching employees get correct days on next accrual | AC | TC-LV-026, TC-LV-036 | Direct |
| AC-2: Overlapping rules resolved by specificity (most specific wins) | AC | TC-LV-027 | Direct |
| AC-3: Per-employee override takes precedence over all rules | AC | TC-LV-028 | Direct |
| AC-4: Mid-year joiner entitlement pro-rated based on joining date | AC | TC-LV-029 | Direct |
| AC-5: Rule modification triggers Hangfire recalculation and audit log | AC | TC-LV-030 | Direct |
| FR-1: Entitlement rules support dimensions: leave type, department, job level, job title, employment type, tenure brackets | FR | TC-LV-026, TC-LV-027, TC-LV-038, TC-LV-046 | Direct (tenure and job-level as standalone dimension DEFERRED in TC-LV-046) |
| FR-2: Rule priority/specificity engine | FR | TC-LV-027, TC-LV-028 | Direct |
| FR-3: Pro-rata calculation for mid-year joiners | FR | TC-LV-029, TC-LV-034 | Direct |
| FR-4: Bulk entitlement assignment UI | FR | TC-LV-037 | Direct |
| FR-5: Hangfire recurring job for accrual processing | FR | TC-LV-030, TC-LV-036, TC-LV-041 | Direct |
| FR-6: Computed balances cached in Redis with tenant-scoped key pattern | FR | TC-LV-042 | DEFERRED (Redis caching not implemented) |
| NFR-1: Recalculation for 5,000 employees within 60 seconds (Hangfire) | NFR | TC-LV-041 | Direct |
| NFR-2: All entitlement data tenant-isolated via EF Core filters and PostgreSQL RLS | NFR | TC-LV-039, TC-LV-040, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | Direct |
| NFR-3: Redis cache for leave balances with 24h TTL and event-driven invalidation | NFR | TC-LV-042, TC-LV-ISO-008 | DEFERRED (Redis caching not implemented) |
| BR-1: Entitlement rules effective per leave year (calendar or fiscal per tenant) | BR | TC-LV-035 | Direct |
| BR-2: Part-time employees receive entitlement proportional to FTE ratio | BR | TC-LV-031 | DEFERRED (FTE field not on Employee entity) |
| BR-3: Probation employees only accrue probation_eligible leave types | BR | TC-LV-032 | Direct |
| BR-4: Entitlement cannot be negative; minimum is zero | BR | TC-LV-033 | Direct |
| BR-5: Department transfer mid-year triggers pro-rata recalculation for both periods | BR | TC-LV-034 | Direct |

### US-LV-003 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Submit valid request -> Pending, leave-requested notification queued, confirmation shown | AC | TC-LV-048 | Direct |
| AC-2: Inline balance shown; insufficient balance (no negative allowed) blocks submission | AC | TC-LV-049, TC-LV-050 | Direct |
| AC-3: Sick leave over document threshold without attachment rejected | AC | TC-LV-051 | Direct |
| AC-4: Half-day creates 0.5-day request and decrements balance accordingly | AC | TC-LV-055 | Direct |
| AC-5: Overlapping dates with existing Pending/Approved request rejected | AC | TC-LV-052 | Direct |
| AC-6: Public holidays excluded from leave day count; adjusted count shown | AC | TC-LV-056 | Direct (holiday exclusion depends on US-LV-007) |
| FR-1: Leave application form fields (type, dates, half-day, reason, attachment) | FR | TC-LV-048, TC-LV-051, TC-LV-055, TC-LV-063 | Direct |
| FR-2: Real-time balance display (current, requested, projected remaining) | FR | TC-LV-049, TC-LV-050 | Direct |
| FR-3: Working-days calc -- exclude weekends and public holidays | FR | TC-LV-056 | Direct (holiday exclusion depends on US-LV-007) |
| FR-4: Overlap detection against existing Pending/Approved requests | FR | TC-LV-052 | Direct |
| FR-5: API endpoint POST /api/v1/leaves with documented body | FR | TC-LV-048, TC-LV-055, TC-LV-061, TC-LV-064 | Direct |
| FR-6: Insert leave_request status=Pending and queue notification | FR | TC-LV-048 | Direct |
| FR-7: Multi-level approval routing per tenant workflow config | FR | -- | NOT COVERED (downstream of submission; belongs to leave-approval story) |
| NFR-1: Submission API responds within 500ms P95 | NFR | TC-LV-064 | Direct |
| NFR-2: Balance check uses Redis-cached values; DB fallback on cache miss | NFR | TC-LV-049, TC-LV-050, TC-LV-064, TC-LV-ISO-012 | Direct (cache layer DEFERRED; DB-fallback path tested) |
| NFR-3: Attachments stored in tenant-scoped blob path {tenantId}/leaves/{requestId}/ | NFR | TC-LV-063, TC-LV-ISO-012 | Direct |
| NFR-4: All operations tenant-isolated via EF Core filters + PostgreSQL RLS | NFR | TC-LV-062, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | Direct |
| NFR-5: Form usable on mobile 360px+ with touch-friendly date pickers | NFR | TC-LV-065 | Direct |
| BR-1: Cannot apply for past dates beyond configurable lookback window | BR | TC-LV-053 | Direct |
| BR-2: Cannot apply for dates beyond configurable future window | BR | TC-LV-054 | Direct |
| BR-3: Maximum consecutive leave days enforced per leave type config | BR | TC-LV-057 | Direct |
| BR-4: Gender-restricted leave types only shown to eligible employees | BR | TC-LV-058 | Direct |
| BR-5: Probation employees only see/apply for probation_eligible leave types | BR | TC-LV-059 | Direct |
| BR-6: Manager/approver determined by employee reporting line (manager_employee_id) | BR | TC-LV-048 | Direct (notification target; full routing in approval story) |

### US-LV-004 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Queue lists direct reports' pending requests, oldest-first, with inline fields and balance | AC | TC-LV-066, TC-LV-067, TC-LV-075, TC-LV-076, TC-LV-080 | Direct |
| AC-2: Server-side pagination (default 20), total count shown | AC | TC-LV-068, TC-LV-069, TC-LV-070 | Direct |
| AC-3: Filter by leave type, employee, or date range | AC | TC-LV-071, TC-LV-072, TC-LV-073, TC-LV-074, TC-LV-075 | Direct |
| AC-4: Detail panel -- full details, attachments, balance, history summary, team-calendar snippet | AC | TC-LV-077, TC-LV-078 | Direct (history/team-calendar subsections deferred on US-LV-009) |
| AC-5: New request included on queue refresh (real-time push) | AC | TC-LV-079 | Direct on API-reload; real-time push DEFERRED on notifications module |
| FR-1: GET /api/v1/leaves/pending scoped to direct reports within tenant | FR | TC-LV-066, TC-LV-067, TC-LV-081, TC-LV-085 | Direct |
| FR-2: Result item fields (employee, type/color, dates, days, reason, hasAttachments, currentBalance, requestedAt) | FR | TC-LV-066, TC-LV-077, TC-LV-080 | Direct |
| FR-3: Server-side filtering and sorting | FR | TC-LV-071, TC-LV-072, TC-LV-073, TC-LV-074, TC-LV-075, TC-LV-084 | Direct |
| FR-4: Server-side pagination with page, pageSize, totalCount | FR | TC-LV-068, TC-LV-069, TC-LV-070, TC-LV-074 | Direct |
| FR-5: Team conflict count (approved overlapping leave) per request | FR | TC-LV-077, TC-LV-078 | Direct |
| FR-6: Real-time SignalR notification of new requests to the queue | FR | TC-LV-079 | DEFERRED (notifications module; API-reload path verified) |
| NFR-1: Pending queue API responds within 300ms P95 using ix_leave_pending | NFR | TC-LV-085 | Direct |
| NFR-2: Inline balances from Redis cache; DB fallback on cache miss | NFR | TC-LV-080, TC-LV-ISO-016 | Direct (Redis DEFERRED; DB-fallback and tenant-scoped key pattern verified) |
| NFR-3: Tenant-isolated via EF Core filters; manager scope limited to direct reports | NFR | TC-LV-081, TC-LV-082, TC-LV-084, TC-LV-ISO-013, TC-LV-ISO-014, TC-LV-ISO-015 | Direct |
| NFR-4: Page fully responsive and usable on mobile 360px+ | NFR | TC-LV-086, TC-LV-087 | Direct |
| BR-1: Managers see only their direct reports (not skip-level unless multi-level configured) | BR | TC-LV-066, TC-LV-067, TC-LV-071, TC-LV-072, TC-LV-081, TC-LV-088, TC-LV-ISO-013 | Direct |
| BR-2: Multi-level approval shows requests at the manager's approval level | BR | TC-LV-088 | Direct (Scenario A now; multi-level workflow forward-looking) |
| BR-3: Requests older than 30 days without action highlighted as overdue | BR | TC-LV-076 | Direct |
| BR-4: Balance shown is current real-time balance, not balance at request time | BR | TC-LV-066, TC-LV-080 | Direct |

### US-LV-005 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Approve -> Approved, used-ledger entry, balance decreased, audit, leave-approved notification queued, Redis cache invalidated | AC | TC-LV-089, TC-LV-091, TC-LV-105 | Direct (Redis invalidation DEFERRED; notification seam DEFERRED; ledger/balance/audit verified) |
| AC-2: Reject with mandatory reason -> Rejected, no ledger entry, audit, leave-rejected notification with reason, reason in approval history | AC | TC-LV-090, TC-LV-094, TC-LV-105 | Direct |
| AC-3: Insufficient balance at approval -> block (negative not allowed) or confirm (negative allowed) | AC | TC-LV-092, TC-LV-093 | Direct |
| AC-4: Multi-level approval -> first approval moves to Pending L2 and notifies next approver | AC | TC-LV-097 | CONDITIONAL on approval-workflow config (US-ADM-007); single-level default verified now |
| AC-5: Two simultaneous decisions -> only first succeeds, second gets 409 (xmin optimistic concurrency) | AC | TC-LV-096 | Direct |
| FR-1: POST /api/v1/leaves/{id}/approve with optional comment | FR | TC-LV-089, TC-LV-091, TC-LV-095, TC-LV-099 | Direct |
| FR-2: POST /api/v1/leaves/{id}/reject with required reason | FR | TC-LV-090, TC-LV-094, TC-LV-095, TC-LV-099 | Direct |
| FR-3: On approval insert leave_ledger 'used' entry; invalidate Redis balance cache | FR | TC-LV-089, TC-LV-092, TC-LV-093, TC-LV-ISO-020 | Direct (Redis invalidation DEFERRED; ledger + DB-fallback balance verified) |
| FR-4: On rejection no ledger entry; only status update and audit | FR | TC-LV-090 | Direct |
| FR-5: Multi-level approval chain (1-3 levels); track approval history | FR | TC-LV-091, TC-LV-097 | Direct for history; multi-level CONDITIONAL on US-ADM-007 |
| FR-6: Optimistic concurrency via PostgreSQL xmin (UseXminAsConcurrencyToken) | FR | TC-LV-096 | Direct |
| FR-7: Audit log Leave.Approved/Leave.Rejected, resource_type LeaveRequest, before/after JSON | FR | TC-LV-089, TC-LV-090, TC-LV-105 | Direct |
| NFR-1: Approve/Reject API responds within 500ms P95 | NFR | TC-LV-103 | Direct |
| NFR-2: Notification queuing asynchronous and non-blocking | NFR | TC-LV-089, TC-LV-090, TC-LV-103, TC-LV-107 | Direct (notification dispatch DEFERRED on notifications module; non-blocking/best-effort verified) |
| NFR-3: All operations tenant-isolated via EF Core filters (RLS-equivalent per vault) | NFR | TC-LV-099, TC-LV-102, TC-LV-105, TC-LV-ISO-017, TC-LV-ISO-018, TC-LV-ISO-019, TC-LV-ISO-020 | Direct |
| NFR-4: Concurrency handling prevents double-approval / approve-then-reject races | NFR | TC-LV-096 | Direct |
| BR-1: Only the designated approver (or current-level approver) can approve/reject | BR | TC-LV-099, TC-LV-ISO-017 | Direct |
| BR-2: Rejection reason mandatory; approval comment optional | BR | TC-LV-090, TC-LV-091, TC-LV-094 | Direct |
| BR-3: A rejected (already-actioned) request cannot be re-approved | BR | TC-LV-095, TC-LV-106 | Direct |
| BR-4: Approving leave for a payroll-locked period is blocked | BR | TC-LV-098 | CONDITIONAL on payroll module period-lock (non-locked path verified) |
| BR-5: Approval deducts balance at approval time, not request time | BR | TC-LV-089, TC-LV-092, TC-LV-093 | Direct |

### US-LV-006 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Summary card per active leave type showing entitlement/used/pending/balance + progress bar | AC | TC-LV-109, TC-LV-110, TC-LV-114, TC-LV-115, TC-LV-116 | Direct |
| AC-2: Click a balance card -> ledger/transaction history for the current leave year | AC | TC-LV-111, TC-LV-112, TC-LV-117 | Direct |
| AC-3: Upcoming Leaves lists approved + pending future requests with dates/type/status/days | AC | TC-LV-113, TC-LV-120 | Direct |
| AC-4: Mobile 360px -- cards stack, remain readable, progress bars scale | AC | TC-LV-127 | Direct |
| AC-5: New joiner with no ledger data -> friendly empty state | AC | TC-LV-119 | Direct |
| FR-1: GET /api/v1/leaves/my-balance returns all leave-type balances for the authenticated employee within tenant | FR | TC-LV-109, TC-LV-116, TC-LV-119, TC-LV-122 | Direct |
| FR-2: Response per leave type (leaveTypeId, leaveTypeName, color, entitlement, used, pending, balance, carryForward, expired) | FR | TC-LV-109, TC-LV-110, TC-LV-114, TC-LV-115 | Direct |
| FR-3: GET /api/v1/leaves/my-ledger?leaveTypeId&year returns the full transaction log | FR | TC-LV-111, TC-LV-112, TC-LV-117 | Direct |
| FR-4: GET /api/v1/leaves/my-upcoming returns approved and pending future leaves | FR | TC-LV-113 | Direct |
| FR-5: Balance from Redis cache (tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}); DB fallback on cache miss | FR | TC-LV-115, TC-LV-121, TC-LV-125, TC-LV-ISO-024 | Direct (Redis cache DEFERRED; DB-fallback computation verified) |
| FR-6: Leave history section with filterable list of past requests (approved/rejected/cancelled) | FR | TC-LV-120 | Direct |
| NFR-1: Balance API responds within 200ms P95 using Redis cache | NFR | TC-LV-125, TC-LV-121 | Direct (Redis cache DEFERRED; DB-fallback path measured against 200ms) |
| NFR-2: Dashboard achieves LCP under 2.5s | NFR | TC-LV-126 | Direct |
| NFR-3: All data tenant-isolated via EF Core filters + PostgreSQL RLS (RLS-equivalent per vault) | NFR | TC-LV-122, TC-LV-123, TC-LV-124, TC-LV-ISO-021, TC-LV-ISO-022, TC-LV-ISO-023, TC-LV-ISO-024 | Direct |
| NFR-4: Accessible WCAG 2.1 AA -- progress bars have aria-labels; color not the sole indicator | NFR | TC-LV-128 | Direct |
| BR-1: Balance = Entitlement + Carry Forward - Used - Expired + Adjustments | BR | TC-LV-110, TC-LV-112, TC-LV-115 | Direct |
| BR-2: "Pending" days shown separately and not deducted from "balance" until approved | BR | TC-LV-110, TC-LV-114 | Direct |
| BR-3: Only active leave types shown; deactivated-with-balance in collapsed Archived section | BR | TC-LV-116 | Direct |
| BR-4: Leave-year boundaries tenant-configurable (calendar or fiscal year) | BR | TC-LV-118 | Direct |
| BR-5: Employee can view previous leave years (read-only, via year selector) | BR | TC-LV-117 | Direct |

### Coverage Summary (Leave Management -- US-LV-001)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 4/5 (80%) -- FR-4 deferred (onboarding wizard) | >= 85% | NOTE (FR-4 is cross-module dependency) |
| Non-Functional Requirements Coverage | 4/4 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded TC-LV-012) | >= 3 | PASS |
| Security Test Cases | 8/29 (27.6%) | >= 30% | NOTE (close; all critical security vectors covered) |
| Performance Test Cases | 2/29 (TC-LV-016, TC-LV-023) | >= 1 | PASS |
| Accessibility Test Cases | 1/29 (TC-LV-019) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/29 (TC-LV-018, TC-LV-020) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-LV-021 (onboarding seeding -- pending US-TENANT-*), TC-LV-ISO-004 partial (cache -- pending Redis implementation) | -- | NOTE |

### Coverage Summary (Leave Management -- US-LV-002)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 5/6 (83%) -- FR-6 deferred (Redis caching) | >= 85% | NOTE (FR-6 is infrastructure dependency) |
| Non-Functional Requirements Coverage | 2/3 (67%) -- NFR-3 deferred (Redis caching) | >= 85% | NOTE (NFR-3 is infrastructure dependency) |
| Business Rules Coverage | 4/5 (80%) -- BR-2 deferred (FTE field) | >= 85% | NOTE (BR-2 is entity-level dependency) |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded TC-LV-042) | >= 3 | PASS |
| Security Test Cases | 7/26 (26.9%) including ISO | >= 30% | NOTE (close; all critical security vectors covered: auth, authz, tenant isolation, XSS) |
| Performance Test Cases | 2/26 (TC-LV-041, TC-LV-042) | >= 1 | PASS |
| Accessibility Test Cases | 1/26 (TC-LV-044) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/26 (TC-LV-043, TC-LV-045) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-LV-031 (FTE proration -- FTE field pending), TC-LV-042 (Redis cache -- pending implementation), TC-LV-046 (job-level/tenure dimensions -- pending entity), TC-LV-ISO-008 partial (cache keys -- pending Redis) | -- | NOTE |

### Coverage Summary (Leave Management -- US-LV-003)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 6/6 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 6/7 (86%) -- FR-7 (multi-level approval routing) downstream of submission | >= 85% | PASS (FR-7 belongs to approval story) |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 6 (4 dedicated ISO-009..012 + embedded in TC-LV-058, TC-LV-063) | >= 3 | PASS |
| Security Test Cases | 8/22 (36%) including ISO | >= 30% | PASS |
| Performance Test Cases | 1/22 (TC-LV-064) | >= 1 | PASS |
| Accessibility Test Cases | 1/22 (TC-LV-065) | >= 1 | PASS |
| Cross-Browser Test Cases | 1/22 (TC-LV-065) | >= 1 | PASS |
| Blocked Test Cases | 0 (TC-LV-056 holiday-exclusion steps conditionally blocked on US-LV-007) | -- | NOTE |
| Deferred Test Cases | TC-LV-ISO-012 partial (balance cache keys -- pending Redis); FR-7 approval routing out of scope | -- | NOTE |

### Coverage Summary (Leave Management -- US-LV-004)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 6/6 (100%) -- FR-6 real-time push DEFERRED (API-reload verified) | >= 85% | PASS (FR-6 push depends on notifications module) |
| Non-Functional Requirements Coverage | 4/4 (100%) -- NFR-2 Redis cache DEFERRED (DB-fallback verified) | >= 85% | PASS |
| Business Rules Coverage | 4/4 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO-013..016 + embedded intra-tenant scope in TC-LV-081) | >= 3 | PASS |
| Security Test Cases | 8/27 (30%) including ISO | >= 30% | PASS |
| Performance Test Cases | 2/27 (TC-LV-085, TC-LV-069) | >= 1 | PASS |
| Accessibility Test Cases | 1/27 (TC-LV-086) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/27 (TC-LV-086, TC-LV-087) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-LV-079 (SignalR real-time push -- notifications module), TC-LV-077 (history/team-calendar subsections -- US-LV-009), TC-LV-088 (multi-level approval -- approval workflow story), TC-LV-ISO-016 partial (balance cache keys -- pending Redis) | -- | NOTE |

### Coverage Summary (Leave Management -- US-LV-005)

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

### Coverage Summary (Leave Management -- US-LV-006)

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

### US-LV-009 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Manager month view -- direct reports' approved + pending as colored blocks | AC | TC-LV-169, TC-LV-170, TC-LV-178, TC-LV-181 | Direct |
| AC-2: Employee view -- approved department leaves only, no pending, no leave-type | AC | TC-LV-171, TC-LV-172, TC-LV-185 | Direct (KEY data-leak prevention, verified UI + server-side payload) |
| AC-3: Manager week view -- Gantt (employees Y-axis, days X-axis) | AC | TC-LV-173 | Direct |
| AC-4: Mobile 360px -- compact list grouped by date | AC | TC-LV-174, TC-LV-188 | Direct |
| FR-1: GET /api/v1/leaves/team-calendar?from&to scoped to team/department | FR | TC-LV-169, TC-LV-181, TC-LV-182, TC-LV-ISO-034 | Direct |
| FR-2: Manager view shows approved + pending for direct reports | FR | TC-LV-169, TC-LV-175, TC-LV-176 | Direct |
| FR-3: Employee view shows only approved department leaves (no pending) | FR | TC-LV-171, TC-LV-172, TC-LV-185 | Direct |
| FR-4: Response fields (employeeId/name, leaveTypeName, color, dates, status, totalDays) | FR | TC-LV-170, TC-LV-181, TC-LV-178 | Direct (employee subset suppressed per BR-1) |
| FR-5: Views -- month, week, list | FR | TC-LV-169, TC-LV-173, TC-LV-174 | Direct |
| FR-6: Filter by employee/leave type/status (status manager-only) | FR | TC-LV-180, TC-LV-175 | Direct |
| FR-7: Public holidays as background highlights | FR | TC-LV-179 | Direct (depends on US-LV-007, implemented) |
| NFR-1: Month-range API within 300ms P95 | NFR | TC-LV-186 | Direct (Redis cache DEFERRED; DB-backed path measured against 300ms) |
| NFR-2: Tenant-isolated via EF Core filters (RLS-equivalent per vault) | NFR | TC-LV-ISO-033, TC-LV-ISO-034, TC-LV-ISO-035, TC-LV-ISO-036 | Direct |
| NFR-3: Employee/manager/HR access control | NFR | TC-LV-171, TC-LV-172, TC-LV-175, TC-LV-176, TC-LV-183, TC-LV-185 | Direct |
| NFR-4: Renders smoothly with 50 employees / 200 entries | NFR | TC-LV-174, TC-LV-187, TC-LV-188 | Direct |
| BR-1: Employees see approved department leaves only -- no pending, no leave types ("on leave") | BR | TC-LV-171, TC-LV-172, TC-LV-180, TC-LV-181, TC-LV-185 | Direct |
| BR-2: Managers see full detail for their direct reports only | BR | TC-LV-169, TC-LV-175, TC-LV-185 | Direct |
| BR-3: HR with Leave.ViewAll sees the whole organization | BR | TC-LV-176 | Direct |
| BR-4: Cancelled leaves not shown | BR | TC-LV-177 | Direct |
| BR-5: Half-day leaves visually differentiated | BR | TC-LV-178, TC-LV-181 | Direct |

### Coverage Summary (Leave Management -- US-LV-007)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 4/4 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 6/6 (100%) -- FR-5 onboarding-seeding trigger DEFERRED (wizard UNWIRED; service verified) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) -- NFR-1 Redis-cached latency DEFERRED (DB-fallback measured against 200ms) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) -- BR-4 payroll-period delete-lock CONDITIONAL on payroll module | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 6 (4 dedicated ISO-025..028 + embedded scope in TC-LV-145, TC-LV-146) | >= 3 | PASS |
| Security Test Cases | 6/24 including ISO | >= 30% | NOTE (close; all critical vectors covered: authz, tenant isolation, injection/CSV) |
| Performance Test Cases | 2/24 (TC-LV-136, TC-LV-147) | >= 1 | PASS |
| Accessibility Test Cases | 1/24 (TC-LV-148) | >= 1 | PASS |
| Cross-Browser Test Cases | 1/24 (TC-LV-148) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred / Conditional Test Cases | TC-LV-140 (payroll-period delete-lock -- CONDITIONAL), TC-LV-144 (onboarding seeding -- DEFERRED), TC-LV-147 (200ms cached read -- DEFERRED on Redis), TC-LV-ISO-028 partial (holiday cache keys) | -- | NOTE |

### Coverage Summary (Leave Management -- US-LV-008)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-7 Redis invalidation DEFERRED (DB/ledger verified) | >= 85% | PASS |
| Non-Functional Requirements Coverage | covered -- Redis-cached latency DEFERRED (DB-fallback verified) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) -- fiscal-year boundary CONDITIONAL on tenant fiscal-year config (calendar-year verified) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO-029..032 + embedded scope in TC-LV-161) | >= 3 | PASS |
| Security Test Cases | 7/24 (TC-LV-161..163, TC-LV-ISO-029..032) | >= 30% | NOTE (close; all critical vectors covered) |
| Performance Test Cases | 1/24 (TC-LV-159) | >= 1 | PASS |
| Accessibility Test Cases | 1/24 (TC-LV-164) | >= 1 | PASS |
| Cross-Browser Test Cases | embedded in TC-LV-164 | >= 1 | NOTE |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred / Conditional Test Cases | TC-LV-151 (carry-forward-expiry Redis invalidation -- DEFERRED), TC-LV-154 (encashment-on-expiry -- CONDITIONAL), TC-LV-167 (fiscal-year boundary -- CONDITIONAL; calendar-year verified), TC-LV-ISO-032 partial (balance cache keys) | -- | NOTE |

### Coverage Summary (Leave Management -- US-LV-009)

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
| Deferred / Conditional Test Cases | TC-LV-179 (holiday-background highlight -- depends on US-LV-007, implemented), TC-LV-186 (300ms cached read -- DEFERRED on Redis; DB-fallback measured), TC-LV-ISO-036 partial (calendar cache keys -- pending Redis) | -- | NOTE |

### US-LV-010 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Cancel a PENDING request -- Cancelled, no ledger, manager notification, audit | AC | TC-LV-189, TC-LV-198, TC-LV-204 | Direct |
| AC-2: Cancel an APPROVED future request with reason -- reversal `adjusted` (+) ledger restores balance, Redis invalidated, notification, audit | AC | TC-LV-190, TC-LV-191, TC-LV-204 | Direct (Redis invalidation DEFERRED; reversal ledger/balance verified) |
| AC-3: Cancel an approved leave already started/passed -- blocked with contact-HR message | AC | TC-LV-192, TC-LV-193, TC-LV-203 | Direct |
| AC-4: Cancel a leave in a payroll-locked period -- blocked | AC | TC-LV-194 | CONDITIONAL on payroll module (non-locked path verified) |
| FR-1: POST /api/v1/leaves/{id}/cancel with required `reason` body | FR | TC-LV-197, TC-LV-205, TC-LV-189, TC-LV-190 | Direct |
| FR-2: Pending -- status -> Cancelled, no ledger entry | FR | TC-LV-189, TC-LV-198, TC-LV-205 | Direct |
| FR-3: Approved -- reversal `leave_ledger` entry (type `adjusted`, positive) restores balance | FR | TC-LV-190, TC-LV-191, TC-LV-196, TC-LV-202 | Direct |
| FR-4: Redis cache invalidation for tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId} | FR | TC-LV-190, TC-LV-ISO-040 | Direct (Redis DEFERRED module-wide; DB-fallback verified) |
| FR-5: Notification queued to manager for both pending + approved cancellations | FR | TC-LV-189, TC-LV-190 | Direct (dispatch DEFERRED on notifications module; non-blocking seam verified) |
| FR-6: Cancellation recorded in leave_approval_history (action = Cancelled, actor = employee) | FR | TC-LV-189, TC-LV-198, TC-LV-204, TC-LV-205 | Direct |
| FR-7: Tenant-configurable policy -- cancel up to N days before start (default 0 = anytime before start) | FR | TC-LV-203, TC-LV-192 | Direct for default; N>0 CONDITIONAL on tenant-settings |
| NFR-1: Cancellation API within 500ms P95 | NFR | TC-LV-208 | Direct (Redis-invalidation DEFERRED; DB path measured) |
| NFR-2: Tenant-isolated via EF Core filters (RLS-equivalent per vault) | NFR | TC-LV-199, TC-LV-200, TC-LV-206, TC-LV-207, TC-LV-ISO-037, TC-LV-ISO-038, TC-LV-ISO-039, TC-LV-ISO-040 | Direct |
| NFR-3: Optimistic concurrency via PostgreSQL xmin (approve vs cancel race) | NFR | TC-LV-201 | Direct |
| NFR-4: Audit log captures before/after state | NFR | TC-LV-204, TC-LV-189, TC-LV-190 | Direct |
| BR-1: Only the requesting employee can cancel; managers cannot cancel on behalf | BR | TC-LV-199, TC-LV-200, TC-LV-ISO-037 | Direct (KEY ownership/authz) |
| BR-2: Rejected or already-cancelled leaves cannot be cancelled again | BR | TC-LV-195, TC-LV-196 | Direct |
| BR-3: Cancellation of approved leave after start date not allowed by default | BR | TC-LV-192, TC-LV-193 | Direct |
| BR-4: Carry-forward days consumed by the cancelled leave restored to the carry-forward pool | BR | TC-LV-202 | CONDITIONAL (general `adjusted` reversal recorded if pool-split untracked) |
| BR-5: Cancellation reason mandatory for approved, optional for pending | BR | TC-LV-197, TC-LV-198 | Direct |

### Coverage Summary (Leave Management -- US-LV-010)

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
| Deferred / Conditional Test Cases | TC-LV-190 (Redis balance-cache invalidation -- DEFERRED), TC-LV-194 (payroll-lock block -- CONDITIONAL on payroll module), TC-LV-202 (carry-forward-pool restoration -- CONDITIONAL), TC-LV-203 (N-day cancellation window -- CONDITIONAL on tenant-settings), TC-LV-ISO-040 partial (balance cache keys -- pending Redis) | -- | NOTE |

### US-LV-011 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Zero balance (no negative allowed) -> "processed as LOP" prompt; on confirm request leave_type=LOP, is_lop=true | AC | TC-LV-210, TC-LV-211, TC-LV-212 | Direct |
| AC-2: Absent (no clock-in, no approved leave) -> absenteeism job auto-generates a System-Generated LOP entry | AC | TC-LV-213, TC-LV-214, TC-LV-221 | CONDITIONAL on Attendance module (no-op seam + System-Generated LOP-entry shape verified) |
| AC-3: HR manually assigns LOP -> leave_request (HR-Assigned) + ledger + employee notified | AC | TC-LV-215, TC-LV-216, TC-LV-223 | Direct |
| AC-4: Payroll run calculates LOP deduction = (salary/working_days)*lop_days as a payslip line item | AC | TC-LV-217 | CONDITIONAL on Payroll module (lop-summary contract verified live) |
| FR-1: LOP system leave type auto-created at tenant setup; non-deletable, renamable | FR | TC-LV-210, TC-LV-218 | Direct (onboarding-seeding call site DEFERRED per vault) |
| FR-2: Auto-LOP via Hangfire ProcessAbsenteeismJob (daily/on-demand) | FR | TC-LV-213, TC-LV-214, TC-LV-228 | Direct for job/no-op seam; attendance-driven entry CONDITIONAL on US-ATTENDANCE-* |
| FR-3: Manual LOP assignment POST /api/v1/leaves/assign-lop {employeeId, dates[], reason} | FR | TC-LV-215, TC-LV-216, TC-LV-226 | Direct |
| FR-4: LOP stored in leave_request with is_lop=true + statuses System-Generated/HR-Assigned (lop_source) | FR | TC-LV-210, TC-LV-213, TC-LV-215, TC-LV-219 | Direct |
| FR-5: LOP data exposed to payroll via GET /api/v1/leaves/lop-summary?employeeId&from&to | FR | TC-LV-217, TC-LV-229, TC-LV-ISO-041 | Direct (deduction calc CONDITIONAL on Payroll) |
| FR-6: Compulsory leave -- HR bulk-assigns a leave type for all employees for specific dates | FR | TC-LV-219, TC-LV-224, TC-LV-226 | Direct |
| NFR-1: Auto-LOP job for 5,000 employees within 3 minutes | NFR | TC-LV-228 | Direct (batched-iteration path measured; attendance-driven entry CONDITIONAL) |
| NFR-2: All LOP data tenant-isolated via EF Core filters (RLS-equivalent per vault) | NFR | TC-LV-224, TC-LV-225, TC-LV-227, TC-LV-ISO-041, TC-LV-ISO-042, TC-LV-ISO-043, TC-LV-ISO-044 | Direct |
| NFR-3: LOP entries immutable once payroll finalized for the period | NFR | TC-LV-222 | CONDITIONAL on payroll-period lock (non-locked editable path verified) |
| NFR-4: Audit trail for all LOP assignments (auto + manual) | NFR | TC-LV-223, TC-LV-215, TC-LV-221 | Direct (notification dispatch DEFERRED on notifications module) |
| BR-1: LOP has no entitlement/balance -- purely a deduction mechanism | BR | TC-LV-210, TC-LV-212, TC-LV-220 | Direct |
| BR-2: LOP deduction formula tenant-configurable (basic/working_days or gross/calendar_days) | BR | TC-LV-217 | CONDITIONAL on Payroll config (default basic-salary formula documented) |
| BR-3: System-generated LOP can be overridden by HR (convert to another type or remove) | BR | TC-LV-221, TC-LV-222 | Direct |
| BR-4: Compulsory leave deducts from balance first; LOP only on shortfall | BR | TC-LV-219 | Direct |
| BR-5: LOP entries for a payroll-locked period cannot be modified | BR | TC-LV-222 | CONDITIONAL on payroll-period lock (non-locked path verified) |
| BR-6: Employees notified whenever LOP is assigned (auto or manual) | BR | TC-LV-223, TC-LV-215, TC-LV-219 | Direct (dispatch DEFERRED on notifications module; queued/log-only seam verified) |

### Coverage Summary (Leave Management -- US-LV-011)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 4/4 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 6/6 (100%) -- FR-2 attendance-driven trigger CONDITIONAL on US-ATTENDANCE-*; FR-5 payroll deduction calc CONDITIONAL on US-PAYROLL-* (lop-summary contract verified) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) -- NFR-1 measured on batched-iteration path; NFR-3 CONDITIONAL on payroll-period lock (non-locked path verified) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) -- BR-2 formula CONDITIONAL on Payroll config; BR-5 CONDITIONAL on payroll lock | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 6 (4 dedicated ISO-041..044 + embedded cross-tenant scope in TC-LV-227, TC-LV-ISO-041) | >= 3 | PASS |
| Security Test Cases | 9/26 (35%) including ISO (TC-LV-223, TC-LV-224, TC-LV-225, TC-LV-226, TC-LV-227, TC-LV-ISO-041..044) | >= 30% | PASS |
| Performance Test Cases | 2/26 (TC-LV-228, TC-LV-229) | >= 1 | PASS |
| Accessibility Test Cases | 1/26 (TC-LV-230) | >= 1 | PASS |
| Cross-Browser Test Cases | 1/26 (TC-LV-231) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred / Conditional Test Cases | TC-LV-213/TC-LV-214 (auto-LOP absenteeism trigger -- CONDITIONAL on Attendance), TC-LV-217 (payroll deduction calc -- CONDITIONAL on Payroll), TC-LV-222 (payroll-finalize immutability -- CONDITIONAL on payroll lock), TC-LV-223 (notification dispatch -- DEFERRED on notifications module), TC-LV-228 (5,000/3-min throughput -- CONDITIONAL on Attendance source), TC-LV-ISO-044 partial (LOP/balance cache keys -- pending Redis) | -- | NOTE |

### US-LV-012 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Balance Summary -- per-employee balance per leave type, filterable by dept/job level/employment type, CSV/Excel exportable; balances match the dashboard | AC | TC-LV-232, TC-LV-233, TC-LV-241, TC-LV-239 | Direct |
| AC-2: Utilization -- total leaves by type, average utilization %, department breakdown with charts | AC | TC-LV-234, TC-LV-235 | Direct |
| AC-3: Absenteeism -- top absentees (unplanned + LOP), trend lines, flagged over tenant threshold | AC | TC-LV-236, TC-LV-237 | Direct |
| AC-4: Trend Analysis -- 12-month monthly trends by type with year-over-year comparison | AC | TC-LV-238 | Direct |
| AC-5: Export CSV/Excel; >5,000 rows -> Hangfire background job + notify | AC | TC-LV-239, TC-LV-240, TC-LV-249 | Direct (sync); >5,000 blob persistence + notification CONDITIONAL/DEFERRED |
| FR-1: Pre-built reports (Balance Summary, Utilization, Absenteeism, Trend, Carry-Forward, LOP, Dept Calendar Coverage) | FR | TC-LV-232, TC-LV-234, TC-LV-236, TC-LV-238, TC-LV-244 | Direct |
| FR-2: Filters -- date range, department, job level, employment type, leave type, employee search | FR | TC-LV-241, TC-LV-242 | Direct (job-level CONDITIONAL on a JobLevel entity) |
| FR-3: Sorting + server-side pagination | FR | TC-LV-243 | Direct |
| FR-4: Export to CSV and Excel (XLSX) via OSS library | FR | TC-LV-239, TC-LV-249 | Direct |
| FR-5: Large exports (>5,000) via Hangfire; tenant-scoped blob storage; notify when ready | FR | TC-LV-240, TC-LV-ISO-048 | Queue/threshold Direct; blob + notification CONDITIONAL/DEFERRED |
| FR-6: API `GET /api/v1/leaves/reports/{reportType}` with filter/pagination params | FR | TC-LV-232, TC-LV-241, TC-LV-243, TC-LV-250 | Direct |
| FR-7: Chart data API `GET /api/v1/leaves/analytics/{chartType}` | FR | TC-LV-234, TC-LV-236, TC-LV-238 | Direct |
| FR-8: Report queries use PostgreSQL read replicas where available | FR | TC-LV-248 | CONDITIONAL/DEFERRED (primary-DB live path measured) |
| NFR-1: Report API ≤2s P95 for ≤1,000 rows | NFR | TC-LV-248 | Direct (read-replica/materialized-view CONDITIONAL) |
| NFR-2: Export ≤5,000 rows ≤10s synchronous; larger deferred | NFR | TC-LV-249, TC-LV-240 | Direct |
| NFR-3: All report data tenant-isolated via EF Core filters (RLS-equivalent per vault) | NFR | TC-LV-251, TC-LV-252, TC-LV-ISO-045, TC-LV-ISO-046, TC-LV-ISO-047, TC-LV-ISO-048 | Direct |
| NFR-4: Charts rendered client-side via OSS library; non-color cues | NFR | TC-LV-234, TC-LV-254 | Direct |
| NFR-5: Reports accessible + print-friendly | NFR | TC-LV-254 | Direct |
| BR-1: Reports only show current-tenant data; no cross-tenant aggregation | BR | TC-LV-ISO-045, TC-LV-ISO-047, TC-LV-252 | Direct |
| BR-2: Role-based access -- HR all / manager team / employee own | BR | TC-LV-245, TC-LV-246, TC-LV-247 | Direct |
| BR-3: Balance reflects real-time computed values (Redis cache or DB) | BR | TC-LV-233, TC-LV-253 | Direct (Redis DEFERRED; DB-fallback verified) |
| BR-4: Absenteeism flagging threshold tenant-configurable (default 3+ unplanned/month) | BR | TC-LV-237 | Direct |
| BR-5: Reports for previous leave years available (7-year retention) | BR | TC-LV-253 | Direct |

### Coverage Summary (Leave Management -- US-LV-012)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 8/8 (100%) -- FR-5 blob/notification CONDITIONAL/DEFERRED (queue/threshold verified); FR-8 read-replica CONDITIONAL/DEFERRED (primary-DB path measured); FR-2 job-level filter CONDITIONAL on a JobLevel entity | >= 85% | PASS |
| Non-Functional Requirements Coverage | 5/5 (100%) -- NFR-1 read-replica/materialized-view CONDITIONAL (live-query path measured) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) -- BR-3 Redis cache DEFERRED (DB-fallback verified) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 6 (4 dedicated ISO-045..048 + embedded cross-tenant scope in TC-LV-252, TC-LV-245) | >= 3 | PASS |
| Security Test Cases | 9/28 (32%) including ISO (TC-LV-246, TC-LV-247, TC-LV-250, TC-LV-251, TC-LV-252, TC-LV-ISO-045..048) | >= 30% | PASS |
| Performance Test Cases | 2/28 (TC-LV-248, TC-LV-249) | >= 1 | PASS |
| Accessibility Test Cases | 1/28 (TC-LV-254) | >= 1 | PASS |
| Cross-Browser Test Cases | 1/28 (TC-LV-255) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred / Conditional Test Cases | TC-LV-240 (large-export blob persistence + ready-notification -- CONDITIONAL/DEFERRED on Blob Storage + Notifications), TC-LV-242 (job-level filter -- CONDITIONAL on a JobLevel entity), TC-LV-248 (read-replica/materialized-view -- CONDITIONAL/DEFERRED on FR-8), TC-LV-253 (Redis-cached real-time balance -- DEFERRED; DB-fallback verified), TC-LV-ISO-047 (materialized-view tenant-filtering -- CONDITIONAL on view existence), TC-LV-ISO-048 partial (export blob path + cache keys -- pending Blob/Redis) | -- | NOTE |

---

## Attendance Module

### Forward Traceability (User Stories --> Test Cases)

| User Story ID | User Story Title | Priority | Test Cases | TC Count | Coverage |
|---------------|-----------------|----------|------------|----------|----------|
| US-ATT-001 | Employee Clock-In from Browser with Optional Geolocation | Must Have | TC-ATT-001, TC-ATT-002, TC-ATT-003, TC-ATT-004, TC-ATT-005, TC-ATT-006, TC-ATT-007, TC-ATT-008, TC-ATT-009, TC-ATT-010, TC-ATT-011, TC-ATT-012 | 12 | 5/5 AC covered |
| Cross-cutting (ATT-001) | Multi-tenant isolation (mandatory) | Critical | TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 | 4 | -- |
| US-ATT-002 | Employee Clock-Out with Work Hours Auto-Calculation | Must Have | TC-ATT-013, TC-ATT-014, TC-ATT-015, TC-ATT-016, TC-ATT-017, TC-ATT-018, TC-ATT-019, TC-ATT-020, TC-ATT-021, TC-ATT-022, TC-ATT-023, TC-ATT-024 | 12 | 5/5 AC covered |
| Cross-cutting (ATT-002) | Multi-tenant isolation (clock-out write path) | Critical | TC-ATT-ISO-005 (+ reuses TC-ATT-ISO-001..004) | 1 | -- |
| US-ATT-003 | Attendance Regularization Request (Forgot Clock-In/Out) | Must Have | TC-ATT-025, TC-ATT-026, TC-ATT-027, TC-ATT-028, TC-ATT-029, TC-ATT-030, TC-ATT-031, TC-ATT-032, TC-ATT-033, TC-ATT-034, TC-ATT-035, TC-ATT-036 | 12 | 5/5 AC covered |
| Cross-cutting (ATT-003) | Multi-tenant isolation (regularization read + submit path) | Critical | TC-ATT-ISO-006 (+ reuses TC-ATT-ISO-001..004) | 1 | -- |
| US-ATT-004 | Manager Approves/Rejects Regularization Requests | Must Have | TC-ATT-037, TC-ATT-038, TC-ATT-039, TC-ATT-040, TC-ATT-041, TC-ATT-042, TC-ATT-043, TC-ATT-044, TC-ATT-045, TC-ATT-046, TC-ATT-047, TC-ATT-048, TC-ATT-049, TC-ATT-050 | 14 | 5/5 AC covered |
| Cross-cutting (ATT-004) | Multi-tenant isolation (approve/reject mutation path) | Critical | TC-ATT-ISO-007 (+ reuses TC-ATT-ISO-001..004, TC-ATT-ISO-006) | 1 | -- |
| **TOTAL** | | | **57 test cases** | **57** | **20/20 AC** |

### Backward Traceability (Test Cases --> User Stories)

| Test Case ID | Test Case Title | Type | Priority | User Story | Requirements Covered |
|-------------|----------------|------|----------|------------|---------------------|
| TC-ATT-001 | Clock-in succeeds and a tenant-scoped attendance_log is created (happy path) | Functional | Critical | US-ATT-001 | AC-1, FR-1, FR-5, FR-6, FR-7, NFR-2, BR-1, BR-5 |
| TC-ATT-002 | Clock-in succeeds without location when geo is optional and denied | Functional | High | US-ATT-001 | AC-4, FR-1, BR-2 |
| TC-ATT-003 | Duplicate clock-in prevented when an open record exists | Functional | Critical | US-ATT-001 | AC-2, FR-2, BR-1 |
| TC-ATT-004 | Clock-in blocked when geo required but permission denied | Functional | Critical | US-ATT-001 | AC-3, FR-3, BR-2, NFR-3 |
| TC-ATT-005 | Clock-in rejected from a non-allowlisted IP | Functional/Security | High | US-ATT-001 | AC-5, FR-4, FR-5, BR-3 |
| TC-ATT-006 | Grace-period boundary -- not late at last grace second, late one second past | Functional (boundary) | High | US-ATT-001 | FR-1, FR-7, BR-4 |
| TC-ATT-007 | Geo-fence radius edge -- on-boundary accepted, just-outside rejected | Functional (boundary) | High | US-ATT-001 | AC-3, FR-1, FR-3, BR-2 |
| TC-ATT-008 | Clock-in forbidden without Attendance.Clock.Self permission | Security | Critical | US-ATT-001 | Authz (Attendance.Clock.Self), FR-1 |
| TC-ATT-009 | Clock-in requires authentication and valid tenant context | Security | Critical | US-ATT-001 | Authn, NFR-3 |
| TC-ATT-010 | Clock-in API P95 <= 500ms under load | Performance | High | US-ATT-001 | NFR-1, FR-1, FR-6 |
| TC-ATT-011 | Clock-in card accessible & responsive -- WCAG 2.1 AA, 360px, 48px, keyboard/SR | Accessibility | High | US-ATT-001 | NFR-5, UI/UX S8 |
| TC-ATT-012 | Two simultaneous clock-ins create only one record (concurrency) | Integration | Critical | US-ATT-001 | AC-2, FR-2, NFR-4, BR-1 |
| TC-ATT-ISO-001 | Tenant A cannot see/retrieve Tenant B attendance records | Security | Critical | US-ATT-001 | NFR-2 |
| TC-ATT-ISO-002 | Clock-in API rejects requests without valid tenant context | Security | Critical | US-ATT-001 | NFR-2, FR-1 |
| TC-ATT-ISO-003 | Tenant A employee cannot create an attendance_log in Tenant B | Security | Critical | US-ATT-001 | NFR-2, FR-1 |
| TC-ATT-ISO-004 | Attendance dashboard cache keys are tenant-scoped | Security | Critical | US-ATT-001 | FR-6, NFR-2 |
| TC-ATT-013 | Clock-out succeeds; total work hours auto-calculated; summary shown (happy path) | Functional | Critical | US-ATT-002 | AC-1, FR-1, FR-2, FR-3, FR-5, NFR-2, NFR-5, BR-1, BR-2 |
| TC-ATT-014 | Clock-out with no open record rejected with clear error | Functional | Critical | US-ATT-002 | AC-2, FR-1, BR-1 |
| TC-ATT-015 | Clock-out on an already-completed record rejected; record untouched | Functional | High | US-ATT-002 | AC-2, FR-1, FR-2, BR-1, NFR-3 |
| TC-ATT-016 | Overtime detection -- 10h on 8h shift -> overtime_minutes=120 stored separately | Functional (boundary) | Critical | US-ATT-002 | AC-3, FR-2, FR-4, BR-2, BR-3, NFR-2 |
| TC-ATT-017 | Short-day detection -- below shift minimum flagged SHORT_DAY for HR | Functional (boundary) | Critical | US-ATT-002 | AC-4, FR-2, FR-4, BR-2, BR-4 |
| TC-ATT-018 | Auto-break deduction boundary -- no deduct at 6h, full 60-min deduct just over 6h | Functional (boundary) | High | US-ATT-002 | FR-2, FR-3, BR-2, NFR-2 |
| TC-ATT-019 | Anomaly detection -- span > 16h flagged ANOMALY for review | Functional (boundary) | High | US-ATT-002 | FR-2, FR-4, FR-7, BR-6 |
| TC-ATT-020 | Geolocation captured on clock-out when tenant policy requires it | Functional | High | US-ATT-002 | AC-5, FR-1, FR-6, NFR-5 |
| TC-ATT-021 | Auto-clock-out Hangfire job closes open records, flags regularization | Integration | High | US-ATT-002 | FR-1, FR-2, FR-7, BR-5 |
| TC-ATT-022 | Clock-out atomicity -- mid-request failure leaves no partial update | Integration | Critical | US-ATT-002 | NFR-3, FR-1, FR-2, FR-4, FR-5 |
| TC-ATT-023 | Clock-out API P95 <= 500ms under load | Performance | High | US-ATT-002 | NFR-1, FR-2, FR-3, FR-4, FR-5 |
| TC-ATT-024 | Clock-out button & summary accessible/responsive -- WCAG 2.1 AA, 360px, status pills | Accessibility | High | US-ATT-002 | NFR-5, UI/UX S8 |
| TC-ATT-ISO-005 | Tenant A employee cannot clock out Tenant B's open record | Security | Critical | US-ATT-002 | NFR-4, FR-1 |
| TC-ATT-025 | Submit regularization for a date with no record (MISSED_BOTH) creates PENDING request (happy path) | Functional | Critical | US-ATT-003 | AC-1, FR-1, FR-2, FR-3, BR-1, BR-5 |
| TC-ATT-026 | Submit regularization with clock-in but no clock-out (MISSED_CLOCK_OUT) links to existing attendance_log | Functional | Critical | US-ATT-003 | AC-2, FR-1, FR-2, FR-3, BR-5 |
| TC-ATT-027 | Date older than lookback rejected with exact lookback message | Functional | Critical | US-ATT-003 | AC-3, FR-6, BR-2 |
| TC-ATT-028 | Duplicate pending regularization for same date rejected with exact message | Functional | Critical | US-ATT-003 | AC-4, BR-3 |
| TC-ATT-029 | Date in a locked payroll period rejected with exact locked-period message | Functional | Critical | US-ATT-003 | AC-5, FR-7, BR-6 |
| TC-ATT-030 | Validation -- reason < 10 chars, future date, clock-in not before clock-out each rejected | Functional | High | US-ATT-003 | FR-5, BR-4, BR-7 |
| TC-ATT-031 | Lookback boundary -- exactly N days accepted, N+1 days rejected | Functional (boundary) | High | US-ATT-003 | AC-3, FR-6, BR-2 |
| TC-ATT-032 | Manager in-app notification on submit (CONDITIONAL/DEFERRED on US-NTF) | Integration | High | US-ATT-003 | FR-4, BR-1 |
| TC-ATT-033 | Regularization submission recorded in audit log | Security | High | US-ATT-003 | NFR-3 |
| TC-ATT-034 | Regularization submit API P95 <= 500ms under load | Performance | High | US-ATT-003 | NFR-1, FR-2, FR-3, FR-6, FR-7 |
| TC-ATT-035 | Regularization drawer/form accessible & responsive -- WCAG 2.1 AA, 360px full-screen, live char-count | Accessibility | High | US-ATT-003 | NFR-4, UI/UX S8 |
| TC-ATT-036 | Regularization submit requires authn + Attendance.Regularize.Self; self-scope enforced | Security | Critical | US-ATT-003 | Authn/Authz (S2), FR-2 |
| TC-ATT-ISO-006 | Tenant A employee cannot see/submit a regularization for Tenant B | Security | Critical | US-ATT-003 | NFR-2, FR-2 |
| TC-ATT-037 | Manager approves -- status APPROVED, attendance_log created/updated with regularized times, total recalculated, employee notified (happy path) | Functional | Critical | US-ATT-004 | AC-1, FR-2, FR-5, FR-6, FR-8, NFR-2, BR-2 |
| TC-ATT-038 | Manager rejects with mandatory reason -- status REJECTED, no attendance_log change, employee notified with reason (happy path) | Functional | Critical | US-ATT-004 | AC-2, FR-3, FR-5, FR-6, BR-1 |
| TC-ATT-039 | Rejection without reason / reason < 10 chars rejected; stays PENDING (negative + boundary) | Functional | High | US-ATT-004 | FR-3, BR-1, BR-2 |
| TC-ATT-040 | Approval queue lists pending requests for direct reports with employee/date/times/reason/submitted-on | Functional | Critical | US-ATT-004 | AC-3, FR-1, FR-7 |
| TC-ATT-041 | Approve for a non-team employee denied with exact authorization message | Security | Critical | US-ATT-004 | AC-5, FR-7 |
| TC-ATT-042 | Manager cannot self-approve -- own request absent from actionable queue; routes to supervisor | Functional/Security | High | US-ATT-004 | BR-6, FR-1, FR-4, FR-7 |
| TC-ATT-043 | Decided (APPROVED/REJECTED) request immutable -- re-acting blocked; audit entries immutable | Functional/Security | High | US-ATT-004 | BR-3, NFR-4 |
| TC-ATT-044 | Multi-level workflow -- level-1 approval keeps PENDING; log written only on final approval (CONDITIONAL/DEFERRED) | Functional | High | US-ATT-004 | AC-4, FR-4, BR-4 |
| TC-ATT-045 | Approval into a now-locked payroll period blocked with contact-HR message (CONDITIONAL on Payroll) | Functional | High | US-ATT-004 | BR-5 |
| TC-ATT-046 | Bulk approval -- select multiple, approve in one action; all eligible processed | Functional | High | US-ATT-004 | BR-7, FR-2, FR-6, FR-7 |
| TC-ATT-047 | Approval atomicity -- mid-approval failure leaves neither regularization nor attendance_log updated | Integration | Critical | US-ATT-004 | NFR-2, FR-2 |
| TC-ATT-048 | Approve/reject recorded in audit log with manager id, timestamp, comment | Security | High | US-ATT-004 | FR-6, NFR-4 |
| TC-ATT-049 | Approval queue loads < 2s P95 for 50 pending requests | Performance | High | US-ATT-004 | NFR-1, FR-1, FR-7 |
| TC-ATT-050 | Approval queue table/cards, inline approve/reject comment area, bulk checkboxes accessible & responsive (WCAG 2.1 AA, 360px) | Accessibility | High | US-ATT-004 | UI/UX S8, WCAG 2.1 AA |
| TC-ATT-ISO-007 | Manager in Tenant A cannot see/approve/reject a regularization in Tenant B | Security | Critical | US-ATT-004 | NFR-3, FR-2, FR-7 |

### US-ATT-001 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: New attendance_log on clock-in; tenant_id from session; UI confirmation in local time | AC | TC-ATT-001 | Direct |
| AC-2: Duplicate clock-in prevented with error message | AC | TC-ATT-003, TC-ATT-012 | Direct |
| AC-3: Geo required -- capture if granted, block if denied | AC | TC-ATT-004, TC-ATT-007 | Direct |
| AC-4: Geo optional -- clock-in proceeds without location | AC | TC-ATT-002 | Direct |
| AC-5: IP allowlist -- reject from non-allowed IP | AC | TC-ATT-005 | Direct |
| FR-1: Create attendance_log with required + nullable geo fields | FR | TC-ATT-001, TC-ATT-002, TC-ATT-004, TC-ATT-007 | Direct |
| FR-2: Prevent multiple active clock-ins per day (tenant tz) | FR | TC-ATT-003, TC-ATT-012 | Direct |
| FR-3: Geo-fence radius validation against allowed locations | FR | TC-ATT-007, TC-ATT-004 | Direct |
| FR-4: IP allowlist validation | FR | TC-ATT-005 | Direct |
| FR-5: Record IP + user agent for audit | FR | TC-ATT-001, TC-ATT-005 | Direct |
| FR-6: Update tenant-scoped Redis cache key | FR | TC-ATT-001, TC-ATT-ISO-004 | Direct (cache CONDITIONAL on Redis; DB-fallback verified) |
| FR-7: UTC storage, local-tz display | FR | TC-ATT-001, TC-ATT-006 | Direct |
| NFR-1: Clock-in P95 <= 500ms | NFR | TC-ATT-010 | Direct |
| NFR-2: Tenant isolation on attendance_log | NFR | TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 | Direct (EF query filters; RLS extension point noted -- see closing note) |
| NFR-3: Geolocation prompt -- HTTPS + consent | NFR | TC-ATT-004, TC-ATT-009 | Direct |
| NFR-4: Idempotent within 5s; no double-submit | NFR | TC-ATT-012 | Direct |
| NFR-5: Responsive, mobile 360px | NFR | TC-ATT-011 | Direct |
| BR-1: At most one open record at a time | BR | TC-ATT-003, TC-ATT-012 | Direct |
| BR-2: Geolocation enforcement is tenant config | BR | TC-ATT-002, TC-ATT-004, TC-ATT-007 | Direct |
| BR-3: IP allowlist is tenant config | BR | TC-ATT-005 | Direct |
| BR-4: Grace period -- not marked late | BR | TC-ATT-006 | Direct |
| BR-5: Clock-in only for active employees | BR | TC-ATT-001 (active precondition) | Indirect |
| BR-6: Selfie photo if required | BR | -- | NOT COVERED (no dedicated AC; reported to caller as follow-up candidate) |

### Coverage Summary (Attendance -- US-ATT-001)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-6 cache CONDITIONAL on Redis (DB-fallback verified) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/6 (83%) -- BR-6 photo has no AC; reported to caller | >= 85% (excl. out-of-AC BR-6) | CONDITIONAL |
| Multi-Tenant Isolation Tests | 4 dedicated (ISO-001..004) + isolation aspects in TC-ATT-001/008/009 | >= 4 | PASS |
| Security Test Cases | 7/16 (44%) (TC-ATT-005, TC-ATT-008, TC-ATT-009, TC-ATT-ISO-001..004) | >= 30% | PASS |
| Performance Test Cases | 1/16 (TC-ATT-010) | >= 1 | PASS |
| Accessibility Test Cases | 1/16 (TC-ATT-011) | >= 1 | PASS |
| API Endpoint Coverage | 1/1 (clock-in) (100%) | >= 90% | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |

### US-ATT-002 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Clock-out sets clock_out (UTC); total work hours calculated and displayed | AC | TC-ATT-013 | Direct |
| AC-2: No open clock-in record -> clear error message | AC | TC-ATT-014, TC-ATT-015 | Direct |
| AC-3: Hours over shift standard flagged overtime, stored separately | AC | TC-ATT-016 | Direct |
| AC-4: Hours below shift minimum flagged "short day" for HR review | AC | TC-ATT-017 | Direct |
| AC-5: Tenant geo policy on clock-out -- capture lat/lon if permitted | AC | TC-ATT-020 | Direct |
| FR-1: Set clock_out to current UTC timestamp | FR | TC-ATT-013, TC-ATT-014, TC-ATT-015, TC-ATT-020, TC-ATT-021, TC-ATT-022, TC-ATT-ISO-005 | Direct |
| FR-2: total_work_minutes = clock_out - clock_in, excl. break | FR | TC-ATT-013, TC-ATT-016, TC-ATT-017, TC-ATT-018, TC-ATT-019, TC-ATT-022, TC-ATT-023 | Direct |
| FR-3: Auto-break deduction per tenant policy | FR | TC-ATT-013, TC-ATT-018, TC-ATT-023 | Direct |
| FR-4: Compare to shift standard; flag overtime/short-day | FR | TC-ATT-016, TC-ATT-017, TC-ATT-019, TC-ATT-022, TC-ATT-023 | Direct |
| FR-5: Update tenant-scoped Redis cache key | FR | TC-ATT-013, TC-ATT-022, TC-ATT-023 | Direct (cache CONDITIONAL on Redis; DB-fallback verified) |
| FR-6: Capture geolocation on clock-out if required | FR | TC-ATT-020 | Direct |
| FR-7: Flag anomaly if span > 16h | FR | TC-ATT-019, TC-ATT-021 | Direct |
| NFR-1: Clock-out P95 <= 500ms | NFR | TC-ATT-023 | Direct |
| NFR-2: Work-hours accuracy to the minute | NFR | TC-ATT-013, TC-ATT-016, TC-ATT-017, TC-ATT-018 | Direct |
| NFR-3: Atomic; no partial updates | NFR | TC-ATT-022 | Direct |
| NFR-4: PostgreSQL RLS / tenant isolation on attendance_log | NFR | TC-ATT-ISO-005 (+ TC-ATT-ISO-001..004) | Direct (EF query filters; RLS extension point noted) |
| NFR-5: Timezone display correctness (local tz) | NFR | TC-ATT-013, TC-ATT-020, TC-ATT-024 | Direct |
| BR-1: Clock-out only with an active open record | BR | TC-ATT-013, TC-ATT-014, TC-ATT-015 | Direct |
| BR-2: Total = span - auto break | BR | TC-ATT-013, TC-ATT-016, TC-ATT-017, TC-ATT-018 | Direct |
| BR-3: Overtime when over standard + threshold, pending approval | BR | TC-ATT-016 | Direct |
| BR-4: Short day when under minimum | BR | TC-ATT-017 | Direct |
| BR-5: End-of-day auto-clock-out job closes open records, flags regularization | BR | TC-ATT-021 | Direct |
| BR-6: Max 16h session; over is anomalous | BR | TC-ATT-019 | Direct |

### Coverage Summary (Attendance -- US-ATT-002)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-5 cache CONDITIONAL on Redis (DB-fallback verified) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 1 dedicated (ISO-005) + reuses ISO-001..004 + isolation aspect in TC-ATT-021 | >= 1 (clock-out write) | PASS |
| Security Test Cases | TC-ATT-ISO-005 dedicated + read/context/cache reuse of ISO-001..004 | >= 1 | PASS |
| Performance Test Cases | 1 (TC-ATT-023) | >= 1 | PASS |
| Accessibility Test Cases | 1 (TC-ATT-024) | >= 1 | PASS |
| API Endpoint Coverage | 1/1 (clock-out) (100%) | >= 90% | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |

### US-ATT-003 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Missed clock-in (no record) -> PENDING regularization + workflow initiated | AC | TC-ATT-025 | Direct |
| AC-2: Clocked in but forgot clock-out -> PENDING regularization linked to existing attendance_log | AC | TC-ATT-026 | Direct |
| AC-3: Date older than lookback -> reject with exact "...the last {N} days." message | AC | TC-ATT-027, TC-ATT-031 | Direct |
| AC-4: Duplicate pending for same date -> reject with exact message | AC | TC-ATT-028 | Direct |
| AC-5: Date in a locked payroll period -> reject with exact message | AC | TC-ATT-029 | Direct |
| FR-1: Regularization form (date, type, corrected time(s), reason) | FR | TC-ATT-025, TC-ATT-026, TC-ATT-030, TC-ATT-035 | Direct |
| FR-2: Create attendance_regularization with required fields; tenant/employee from session | FR | TC-ATT-025, TC-ATT-026, TC-ATT-033, TC-ATT-034, TC-ATT-036, TC-ATT-ISO-006 | Direct |
| FR-3: Initiate tenant's configured approval workflow on submit | FR | TC-ATT-025, TC-ATT-026 | Direct (workflow_instance_id asserted; multi-level/approve-reject -> US-ATT-004) |
| FR-4: In-app notification to approver (line manager) | FR | TC-ATT-032 | CONDITIONAL/DEFERRED on US-NTF (seam verified now) |
| FR-5: Validate times (clock-in before clock-out, single day, not future) | FR | TC-ATT-030 | Direct |
| FR-6: Tenant-configurable lookback period (default 7 days) | FR | TC-ATT-027, TC-ATT-031, TC-ATT-034 | Direct |
| FR-7: Prevent regularization within a locked payroll period | FR | TC-ATT-029 | Direct (locked-period assertion CONDITIONAL on Payroll; unlocked path verified) |
| NFR-1: Submission P95 <= 500ms | NFR | TC-ATT-034 | Direct |
| NFR-2: PostgreSQL RLS / tenant isolation on attendance_regularization | NFR | TC-ATT-ISO-006 (+ TC-ATT-ISO-001..004) | Direct (EF query filters; RLS extension point noted) |
| NFR-3: All regularization actions recorded in audit log | NFR | TC-ATT-033 | Direct (submit; approve/reject -> US-ATT-004) |
| NFR-4: Accessible & responsive, 360px minimum | NFR | TC-ATT-035 | Direct |
| BR-1: Requires >= 1 level of approval (approver = notification/workflow target) | BR | TC-ATT-025, TC-ATT-026, TC-ATT-032 | Direct |
| BR-2: Lookback tenant-configurable, default 7 days | BR | TC-ATT-027, TC-ATT-031 | Direct |
| BR-3: Only one pending regularization per employee per date | BR | TC-ATT-028 | Direct |
| BR-4: No regularization for future dates | BR | TC-ATT-030 | Direct |
| BR-5: Link to existing attendance_log if present; new log on approval | BR | TC-ATT-025 (null link, no log created), TC-ATT-026 (linked to existing log) | Direct (approval-side log create/update -> US-ATT-004) |
| BR-6: No regularization in locked payroll period unless HR unlocks | BR | TC-ATT-029 | Direct (CONDITIONAL on Payroll) |
| BR-7: Reason mandatory, >= 10 characters | BR | TC-ATT-030 | Direct |

### Coverage Summary (Attendance -- US-ATT-003)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-4 notification CONDITIONAL on US-NTF; FR-7 locked-period CONDITIONAL on Payroll | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) | >= 85% | PASS |
| Business Rules Coverage | 7/7 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 1 dedicated (ISO-006) + reuses ISO-001..004 + isolation aspects in TC-ATT-033/036 | >= 1 (regularization submit) | PASS |
| Security Test Cases | TC-ATT-033, TC-ATT-036, TC-ATT-ISO-006 dedicated + read/context/cache reuse of ISO-001..004 | >= 1 | PASS |
| Performance Test Cases | 1 (TC-ATT-034) | >= 1 | PASS |
| Accessibility Test Cases | 1 (TC-ATT-035) | >= 1 | PASS |
| API Endpoint Coverage | 1/1 (regularization submit) (100%) | >= 90% | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |

### US-ATT-004 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Approve -> APPROVED, attendance_log created/updated with regularized times, employee notified | AC | TC-ATT-037 (+ TC-ATT-047, TC-ATT-044) | Direct |
| AC-2: Reject with mandatory reason -> REJECTED, employee notified with reason | AC | TC-ATT-038 | Direct |
| AC-3: Queue lists pending requests for direct reports (employee, date, times, reason, submitted-on) | AC | TC-ATT-040 (+ TC-ATT-049, TC-ATT-050) | Direct |
| AC-4: Multi-level workflow -- level-1 approval keeps status PENDING until final level | AC | TC-ATT-044 | CONDITIONAL/DEFERRED on US-ADM-007 (single-level verified via TC-ATT-037/042) |
| AC-5: Approve for a non-team employee -> exact "You are not authorized to approve requests for this employee." | AC | TC-ATT-041 | Direct |
| FR-1: Filterable list of pending requests for the manager's team | FR | TC-ATT-040, TC-ATT-049 | Direct |
| FR-2: On approval, create/update attendance_log with regularized times, recalc total_work_minutes | FR | TC-ATT-037, TC-ATT-044, TC-ATT-046, TC-ATT-047 | Direct |
| FR-3: On rejection, require reason (min 10 chars), store in workflow history | FR | TC-ATT-038, TC-ATT-039 | Direct |
| FR-4: Advance workflow per the tenant's configured approval chain | FR | TC-ATT-044 | CONDITIONAL/DEFERRED on US-ADM-007 (single-level default verified) |
| FR-5: Notify the employee on approval/rejection | FR | TC-ATT-037, TC-ATT-038 | CONDITIONAL/DEFERRED on US-NTF (dispatch seam incl. rejection reason verified now) |
| FR-6: Log approval/rejection in audit (manager id, timestamp, comment) | FR | TC-ATT-048 (+ TC-ATT-037/038) | Direct |
| FR-7: Manager may only approve requests for direct reports | FR | TC-ATT-041, TC-ATT-046, TC-ATT-ISO-007 | Direct |
| FR-8: Update Redis cache for the employee's daily attendance status on approval | FR | TC-ATT-037 | CONDITIONAL/DEFERRED on Redis (DB-fallback path verified) |
| NFR-1: Approval queue loads < 2s P95 for up to 50 pending requests | NFR | TC-ATT-049 | Direct |
| NFR-2: Approval/rejection atomic -- both update or neither | NFR | TC-ATT-047 (+ TC-ATT-037) | Direct |
| NFR-3: Tenant isolation -- managers only see requests within their tenant | NFR | TC-ATT-ISO-007 (+ TC-ATT-ISO-001..004, TC-ATT-ISO-006) | Direct (EF query filters; RLS extension point noted) |
| NFR-4: Approval actions immutable in the audit log | NFR | TC-ATT-043, TC-ATT-048 | Direct |
| BR-1: Rejection reason mandatory (min 10 chars) | BR | TC-ATT-038, TC-ATT-039 | Direct |
| BR-2: Approval comment optional | BR | TC-ATT-037, TC-ATT-039 | Direct |
| BR-3: Decision immutable once approved/rejected | BR | TC-ATT-043 (+ TC-ATT-046) | Direct |
| BR-4: attendance_log updated only on the final approval | BR | TC-ATT-044 | CONDITIONAL/DEFERRED on US-ADM-007 (single-level final write via TC-ATT-037) |
| BR-5: Approval blocked if date in a locked payroll period -- contact HR | BR | TC-ATT-045 | CONDITIONAL on Payroll (unlocked path verified) |
| BR-6: Managers cannot approve their own requests; route to supervisor/HR | BR | TC-ATT-042 | Direct |
| BR-7: Bulk approval -- select multiple, approve in one action | BR | TC-ATT-046 | Direct |

### Coverage Summary (Attendance -- US-ATT-004)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) -- AC-4 multi-level CONDITIONAL on US-ADM-007 (single-level verified) | >= 100% | PASS |
| Functional Requirements Coverage | 8/8 (100%) -- FR-4 workflow CONDITIONAL on US-ADM-007; FR-5 notification CONDITIONAL on US-NTF; FR-8 cache CONDITIONAL on Redis | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) -- NFR-3 RLS noted as EF-query-filter extension point | >= 85% | PASS |
| Business Rules Coverage | 7/7 (100%) -- BR-4 final-write CONDITIONAL on US-ADM-007; BR-5 locked-period CONDITIONAL on Payroll | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 1 dedicated (ISO-007) + reuses ISO-001..004, ISO-006 + isolation aspects in TC-ATT-041/048 | >= 1 (approve/reject mutation) | PASS |
| Security Test Cases | TC-ATT-041, TC-ATT-042, TC-ATT-043, TC-ATT-048, TC-ATT-ISO-007 dedicated + read/context/cache reuse of ISO-001..004/006 | >= 1 | PASS |
| Performance Test Cases | 1 (TC-ATT-049) | >= 1 | PASS |
| Accessibility Test Cases | 1 (TC-ATT-050) | >= 1 | PASS |
| API Endpoint Coverage | approve + reject + bulk-approve + approval-queue (100%) | >= 90% | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |

---

### Cross-Module Coverage Summary

| Module | User Stories | Test Cases | AC Coverage | Multi-Tenant Tests | Status |
|--------|------------|------------|-------------|-------------------|--------|
| Authentication & Authorization | 10 | 116 | 61/61 (100%) | 23 | PASS |
| Core HR (US-CHR-001 through US-CHR-012) | 12 | 372 | 61/61 (100%) | 67 | PASS |
| Leave Management (US-LV-001 through US-LV-012) | 12 | 303 | 57/57 (100%) | 48 | PASS |
| Attendance (US-ATT-001, US-ATT-002, US-ATT-003, US-ATT-004) | 4 | 57 | 20/20 (100%) | 7 | PASS |
| **TOTAL** | **38** | **848** | **199/199 (100%)** | **155** | |

---

*Note: This traceability matrix covers Authentication & Authorization (10 stories, 116 TCs), Core HR (12 stories, 372 TCs), Leave Management (12 stories, 303 TCs -- module complete), and Attendance (4 stories, 57 TCs -- module in progress). US-ATT-004 (Manager Approves/Rejects Regularization Requests) adds 14 functional/security/integration/performance/accessibility test cases (TC-ATT-037..050) + 1 dedicated multi-tenant isolation test (TC-ATT-ISO-007), reusing TC-ATT-ISO-001..004 (table-level read/missing-context/cache) and TC-ATT-ISO-006 (regularization read/submit) for the cross-cutting isolation mechanism. All 5 acceptance criteria for US-ATT-004 have coverage. US-ATT-004 KEY notes: AC-1 approve (TC-ATT-037) verifies status -> APPROVED, the attendance_log CREATE branch (MISSED_BOTH) with regularized UTC times and recalculated total_work_minutes, the optional approval comment + actor + timestamp in workflow history, and the employee-notification dispatch SEAM; AC-2 reject (TC-ATT-038) verifies status -> REJECTED, the mandatory reason stored in workflow history, NO attendance_log mutation, and a notification payload that includes the rejection reason; TC-ATT-039 enforces the BR-1/FR-3 reason rule (empty/9-char rejected, 10-char accepted) and keeps the request PENDING, with approval-comment-optional (BR-2) as a positive control; AC-3 queue (TC-ATT-040) returns only the manager's direct-report PENDING rows with employee/date/requested-times/reason/submitted-on, excluding decided and out-of-team requests and supporting filters + expandable rows; AC-5 authz (TC-ATT-041) returns the EXACT "You are not authorized to approve requests for this employee." for an out-of-team target on both approve and reject, server-side and auditable; BR-6 self-approval (TC-ATT-042) confirms a manager's own request is absent from their actionable queue and self-approval is blocked, routing instead to their supervisor; BR-3/NFR-4 immutability (TC-ATT-043) blocks re-acting on a decided request with no duplicate side effects and confirms audit entries cannot be modified/deleted; AC-4/FR-4/BR-4 multi-level workflow (TC-ATT-044) is CONDITIONAL/DEFERRED on the Approval Workflow Engine (US-ADM-007) -- a level-1 approval keeps the request PENDING and writes the attendance_log only at the final level, with the single-level final-approval path verified live; BR-5 payroll-lock at approval (TC-ATT-045) is CONDITIONAL on the Payroll module -- approval into a now-locked period is blocked with the contact-HR message (unlocked path verified now), complementing the submit-time lock of US-ATT-003 TC-ATT-029; BR-7 bulk approval (TC-ATT-046) approves a multi-select set in one action with per-item attendance_log writes/audits and per-item relationship/immutability/lock checks (ineligible items reported, not applied); NFR-2 atomicity (TC-ATT-047) confirms a mid-approval failure rolls back the status flip, the attendance_log write, and the workflow advance together (no half-applied state), with a clean retry fully applying; FR-6/NFR-4 audit (TC-ATT-048) records action/actor/timestamp/target/comment per decision, tenant-scoped and immutable; NFR-1 performance (TC-ATT-049) measures the approval-queue load against 2s P95 at 50 pending requests while preserving scope; UI/UX S8 accessibility (TC-ATT-050) verifies the WCAG 2.1 AA queue table/cards, keyboard-operable inline approve/reject with a labeled slide-down comment area announcing the 10-char minimum, bulk-selection checkboxes + Bulk Approve, text-not-color status pills, the pending badge, and full 360px usability; tenant isolation (TC-ATT-ISO-007) confirms a Tenant A manager cannot see (queue), fetch (404 via EF global query filter), approve, reject, or bulk-approve a Tenant B regularization -- by id, body-injected tenant_id/employee_id, or subdomain/JWT switch -- and never writes a Tenant B attendance_log, extending TC-ATT-ISO-001..004/006 to the approve/reject mutations. REPORTED TO CALLER for US-ATT-004: (1) AC-4/FR-4/BR-4 multi-level approval routing + final-only log write -- DEFERRED on the Approval Workflow Engine (US-ADM-007); single-level final-approval (TC-ATT-037) and deny-self-approval (TC-ATT-042) verified live; (2) FR-5 employee notification on approve/reject -- DEFERRED on the Notification System (US-NTF); the dispatch seam (recipient = requesting employee, tenant-scoped, payload references regularization_id + outcome, incl. the rejection reason) verified now; (3) FR-8 Redis daily-status cache update on approval -- CONDITIONAL on the Redis layer; DB-fallback path verified now; (4) BR-5 payroll-period lock at approval -- CONDITIONAL on the Payroll module; unlocked path + contact-HR error-contract verified now; (5) NFR-3/S10 specify PostgreSQL RLS, but the platform enforces isolation via EF Core global query filters + TenantInterceptor -- TC-ATT-ISO-007/006/003/001 describe the EF mechanism and mark the RLS session-level assertion as an extension point. US-ATT-003 (Attendance Regularization Request -- Forgot Clock-In/Out) adds 12 functional/integration/security/performance/accessibility test cases (TC-ATT-025..036) + 1 dedicated multi-tenant isolation test (TC-ATT-ISO-006), reusing TC-ATT-ISO-001..004 for table-level read/missing-context/cache isolation. All 5 acceptance criteria for US-ATT-003 have direct coverage. US-ATT-003 KEY notes: AC-1 happy path (TC-ATT-025) verifies a MISSED_BOTH submission for a date with no record creates a PENDING attendance_regularization with attendance_log_id null, UTC requested times, session-derived tenant_id/employee_id, an initiated workflow_instance_id, and NO attendance_log at submission (the log is created only on approval per S10/BR-5); AC-2 (TC-ATT-026) verifies a MISSED_CLOCK_OUT submission links to the existing open attendance_log via attendance_log_id and leaves that log unchanged until approval; AC-3 lookback rejection (TC-ATT-027) returns the exact "Regularization requests can only be submitted for the last {N} days." with N from tenant config, and the lookback boundary (TC-ATT-031) confirms exactly-N-days accepted vs N+1 rejected, evaluated in tenant-local time and tracking the tenant-configurable N; AC-4 duplicate-pending (TC-ATT-028) returns the exact "A pending regularization request already exists for this date." and confirms a prior REJECTED/CANCELLED request does NOT block a new submission (BR-3 blocks only a concurrent PENDING); AC-5 locked-payroll-period (TC-ATT-029) returns the exact "This date falls within a locked payroll period. Please contact HR." and confirms the same date succeeds once HR unlocks (BR-6); validation (TC-ATT-030) rejects reason < 10 chars and empty reason (BR-7), future dates (BR-4), and time inconsistencies -- clock-in not before clock-out, cross-day, future time (FR-5); audit (TC-ATT-033/NFR-3) records the submit action tenant-scoped with actor/regularization_id; performance (TC-ATT-034/NFR-1) measures the full validation+insert+workflow-init path P95 <= 500ms; accessibility (TC-ATT-035/NFR-4) verifies the right-slide drawer that becomes FULL-SCREEN on mobile, keyboard operation with focus trap/return, a LIVE reason char-count announced to screen readers with below-minimum highlight, labeled date/time inputs and approval-chain preview, a text-not-color "Pending" pill, and full 360px visibility; authn/authz (TC-ATT-036) enforces 401 unauthenticated, 403 without Attendance.Regularize.Self (server-side, not button-hiding), and self-scope (a body-injected employee_id for another employee is ignored); tenant isolation (TC-ATT-ISO-006) confirms a Tenant A employee cannot read (404 via EF global query filter), list, submit (body-injected tenant_id/employee_id ignored/stamped by TenantInterceptor), link a Tenant B attendance_log, target a Tenant B approver, or subdomain-switch into Tenant B, extending TC-ATT-ISO-001..004 to attendance_regularization. REPORTED TO CALLER for US-ATT-003: (1) FR-4 in-app manager notification -- the Notification System (US-NTF) is not built; TC-ATT-032 verifies the submit-time notification SEAM (recipient = line manager, tenant-scoped, payload references regularization_id) now and DEFERS in-app delivery/badge assertions until US-NTF lands (consistent with leave-management notification deferrals); (2) FR-7/BR-6 payroll-period lock depends on the Payroll module -- TC-ATT-029 verifies the unlocked path and the exact error-contract now, with the locked-period assertion CONDITIONAL on Payroll; (3) FR-3/BR-1 approval workflow -- TCs assert a workflow_instance is initiated on submit; multi-level routing and the approve/reject side (US-ATT-004) are out of this story's scope; (4) NFR-2/S10 specify PostgreSQL RLS on attendance_regularization, but the platform currently enforces isolation via EF Core global query filters + TenantInterceptor -- TC-ATT-ISO-006/003/001 describe the EF mechanism and mark the RLS session-level assertion as an extension point if backend adds RLS policies; (5) per S10/BR-5 no attendance_log is created/updated at submission -- the log create-on-approval side is verified under US-ATT-004. US-ATT-002 (Employee Clock-Out with Work Hours Auto-Calculation) adds 12 functional/integration/performance/accessibility test cases (TC-ATT-013..024) + 1 dedicated multi-tenant isolation test (TC-ATT-ISO-005), reusing TC-ATT-ISO-001..004 for table-level read/missing-context/cache isolation rather than duplicating them. All 5 acceptance criteria for US-ATT-002 have direct coverage. US-ATT-002 KEY notes: AC-1 happy path (TC-ATT-013) verifies the UPDATE-in-place of the open record with UTC clock_out, total_work_minutes = (span - auto break) accurate to the minute, COMPLETE status, audit/source-IP stamping, the fade-in summary card in the employee's local timezone, and the FR-5 tenant-scoped status cache (CONDITIONAL on Redis -- DB-fallback verified); AC-2 no-open-record is covered as both the never-clocked-in reject (TC-ATT-014, exact "No active clock-in found..." message) and the already-completed-record reject that leaves the original untouched (TC-ATT-015); AC-3 overtime (TC-ATT-016) verifies the 10h-on-8h-shift -> overtime_minutes=120 worked example stored SEPARATELY and pending approval (feeds US-ATT-006); AC-4 short-day (TC-ATT-017) flags SHORT_DAY for HR review without blocking the clock-out; AC-5 geolocation-on-clock-out (TC-ATT-020) stores clock_out_latitude/longitude at decimal(10,7) when granted over HTTPS and handles permission-denied per tenant policy; boundaries are covered for the auto-break threshold (TC-ATT-018, no deduct at exactly 6h vs full 60-min deduct at 6h 1m) and the 16h anomaly threshold (TC-ATT-019, not anomalous at 16h exactly vs ANOMALY at 16h 1m, FR-7/BR-6); BR-5 auto-clock-out is verified as an end-of-day Hangfire job (TC-ATT-021) that closes only OPEN records with a system actor, flags them for regularization, leaves manually completed records untouched, is idempotent on re-run, and is tenant-scoped; NFR-3 atomicity (TC-ATT-022) confirms a mid-request failure rolls back leaving the record fully OPEN with no partial clock_out/total/status; NFR-1 (TC-ATT-023) measures the close/calculate path P95 <= 500ms under load; NFR-5/UI a11y (TC-ATT-024) verifies the warm-colored Clock Out button, keyboard operation, an ARIA-live summary card, Notion-style status pills that convey state by text not color alone, and full visibility at 360px without scroll; clock-out tenant isolation (TC-ATT-ISO-005) confirms a Tenant A employee cannot close a Tenant B open record by id, body-injected tenant_id/employee_id, or subdomain switch (404 via EF global query filter; globex record untouched), extending the table-level isolation of TC-ATT-ISO-001..004 to the clock-out mutation. REPORTED TO CALLER for US-ATT-002: (1) NFR-4/S10 specify PostgreSQL RLS on attendance_log, but the platform currently enforces isolation via EF Core global query filters + TenantInterceptor -- TC-ATT-ISO-005/003/001 describe the EF mechanism and mark the RLS session-level UPDATE-block assertion as an extension point if backend adds RLS policies; (2) the Redis cache (FR-5) is not assumed wired -- TC-ATT-013/022/023 verify the DB-fallback status path now and activate cache assertions once the layer exists (consistent with US-ATT-001 FR-6); (3) TC-ATT-016/017/018/019 depend on shift standard/minimum hours and break rules from US-ATT-005 and the overtime workflow of US-ATT-006 -- the assumed shift config is documented inline so the TCs run against seeded shift data and integrate when those stories land; (4) S10 fixes Phase 1 to a single clock-in/out session per day, so multi-session totals are out of scope; (5) the exact status enum value used by the auto-clock-out job for the system-closed/regularization flag (TC-ATT-021) should be confirmed against the backend implementation when available. (Earlier-pass note retained below.) This traceability matrix also covers Authentication & Authorization (10 stories, 116 TCs), Core HR (12 stories, 372 TCs), Leave Management (12 stories, 303 TCs -- module complete), and Attendance US-ATT-001 (the module's first story, which created test-cases/attendance/ and its TEST-MATRIX.md). US-ATT-001 (Employee Clock-In from Browser with Optional Geolocation) is the first Attendance story and establishes the module's TEST-MATRIX; it adds 12 functional/security/performance/accessibility/integration test cases (TC-ATT-001..012) + 4 dedicated multi-tenant isolation tests (TC-ATT-ISO-001..004). All 5 acceptance criteria for US-ATT-001 have direct coverage. US-ATT-001 KEY notes: AC-1 happy path (TC-ATT-001) verifies the created attendance_log with UTC clock_in, session-derived tenant_id, IP/user-agent/source audit fields, the FR-6 tenant-scoped cache (CONDITIONAL on the Redis layer -- DB-fallback verified), and the local-timezone success toast; AC-2 duplicate prevention is covered both as a sequential reject (TC-ATT-003) and as the key concurrency/race test where two simultaneous requests yield exactly one record via a DB-level guard satisfying NFR-4's 5-second idempotency (TC-ATT-012); AC-3 mandatory geolocation is split into the permission-denied block (TC-ATT-004) and the geo-fence radius boundary (TC-ATT-007, on-radius accepted / one-meter-past rejected); AC-4 optional-geo success without coordinates (TC-ATT-002); AC-5 IP allowlist rejection with the allowed-IP positive control and CIDR evaluation (TC-ATT-005); BR-4 grace-period boundary (TC-ATT-006) verifies not-late at the last grace second and late one second past, computed in tenant-local time against UTC storage; security covers authz -- the Attendance.Clock.Self permission gate enforced server-side, not just a hidden button (TC-ATT-008) -- and authn/tenant-context rejection (TC-ATT-009); NFR-1 P95 <= 500ms under representative load on valid first clock-ins (TC-ATT-010); NFR-5/UI a11y -- WCAG 2.1 AA, 360px full-width card, >= 48px touch target, keyboard operation, screen-reader live-region toast (TC-ATT-011); tenant isolation (TC-ATT-ISO-001..004) confirms a Tenant A user cannot read (ISO-001), write into (ISO-003, body-injected tenant_id/employee_id ignored, stamped by TenantInterceptor), or act without a resolved tenant context (ISO-002) across tenants, and that the FR-6 attendance status cache key is tenant-scoped (ISO-004, CONDITIONAL on Redis; DB-fallback verified). REPORTED TO CALLER for US-ATT-001: (1) BR-6 selfie-photo-on-clock-in (require_photo) has no acceptance criterion in AC-1..AC-5 and is intentionally left without a dedicated TC -- flag for the BA whether photo capture is in Phase 1 scope or belongs to a separate story; (2) NFR-2/S10 specify PostgreSQL RLS on attendance_log, but this platform currently enforces tenant isolation via EF Core global query filters + TenantInterceptor -- the ISO TCs describe the EF mechanism and mark the RLS session-level assertion as an extension point if backend adds RLS policies; (3) the Redis cache (FR-6) is not assumed wired -- TC-ATT-001/010/ISO-004 verify the DB-fallback path now and activate cache-specific assertions once the layer exists. This is the first story of a brand-new Attendance module; the test-cases/attendance/ directory and its TEST-MATRIX.md were created in this pass. US-LV-012 (Leave Reports and Analytics for HR) -- the final leave-management story -- adds 24 functional/integration/security/performance/accessibility/cross-browser test cases (TC-LV-232..255) + 4 dedicated multi-tenant isolation tests (TC-LV-ISO-045..048). All 5 acceptance criteria for US-LV-012 have coverage. US-LV-012 KEY notes: AC-1 Balance Summary (TC-LV-232) is filterable by dept/job level/employment type and CSV/Excel exportable, with TC-LV-233 reconciling its balances against the US-LV-006 employee dashboard (single source of truth); AC-2 Utilization (TC-LV-234) shows per-type totals + average utilization % + department breakdown charts, and TC-LV-235 asserts the 200-entitlement/80-used -> 40% math (zero-entitlement guarded); AC-3 Absenteeism (TC-LV-236) ranks unplanned + LOP absentees with trend lines, and TC-LV-237 verifies the BR-4 tenant-configurable threshold (4 vs 3 -> flagged, re-evaluated when the threshold changes); AC-4 Trend Analysis (TC-LV-238) is 12-month monthly-by-type with YoY; AC-5 export (TC-LV-239) verifies 100-row CSV/XLSX headers+data honoring filters, while the >5,000-row Hangfire background export (TC-LV-240/FR-5) is queue/threshold-verified live with the blob persistence (`{tenantId}/reports/leave/{reportId}.xlsx`) and ready-notification recorded CONDITIONAL/DEFERRED on Blob Storage + the notifications module; BR-2 role-based access is verified across all three roles (HR all TC-LV-245, manager team-only with tamper-block TC-LV-246, employee own-only no-IDOR TC-LV-247); NFR-1 (TC-LV-248) measures the report API against 2s P95 for ≤1,000 rows with read-replica/materialized-view (FR-8/§7) recorded CONDITIONAL/DEFERRED (primary-DB live path measured) and NFR-2 (TC-LV-249) ≤5,000-row sync export ≤10s; authz (TC-LV-250, 403), auth (TC-LV-251, 401), and injection + cross-tenant/cross-team IDOR (TC-LV-252) cover security; BR-3 real-time balances (TC-LV-253) verify the DB-fallback (Redis DEFERRED module-wide) and BR-5 prior-year reports from retained data; NFR-4/NFR-5 accessibility + print-friendliness + non-color chart cues (TC-LV-254) and cross-browser/responsive 360--1920px (TC-LV-255); tenant isolation (TC-LV-ISO-045..047) confirms a Tenant A HR Officer sees only Tenant A across every report/analytics/export and that EF global query filters block cross-tenant SUM/COUNT/AVG aggregation (RLS-equivalent per docs/vault/modules/leave-management.md; materialized-view tenant-filtering CONDITIONAL on view existence), and TC-LV-ISO-048 verifies the tenant-scoped export blob path + cache-key design by design with DB-fallback isolation verified live (partial pending Blob/Redis). US-LV-011 (Compulsory Leave / Loss of Pay (LOP) Handling) adds 22 functional/integration/security/performance/accessibility/cross-browser test cases (TC-LV-210..231) + 4 dedicated multi-tenant isolation tests (TC-LV-ISO-041..044). All 4 acceptance criteria for US-LV-011 have coverage. US-LV-011 KEY notes: AC-1 zero-balance->LOP-prompt->confirm (TC-LV-210) creates a leave_request with leave_type=LOP/is_lop=true/lop_source=employee_request, suppressed for negative-balance-allowed types (TC-LV-212) and a clean no-op on decline (TC-LV-211); AC-2 auto-LOP via the absenteeism job (TC-LV-213/TC-LV-214) is CONDITIONAL on the Attendance module -- the no-op attendance-provider seam (generates nothing) and the System-Generated LOP-entry shape are verified live and the attendance-driven trigger/idempotency + the 5,000-employee/3-min throughput (NFR-1, TC-LV-228) are recorded CONDITIONAL on US-ATTENDANCE-*; AC-3 manual assign-lop (TC-LV-215/TC-LV-216) creates an HR-Assigned LOP request + ledger + notification with dates[] validated; AC-4 (TC-LV-217) verifies the lop-summary endpoint contract (FR-5) the payroll engine consumes and records the (salary/working_days)*lop_days deduction as CONDITIONAL on US-PAYROLL-*; FR-1 LOP system type (TC-LV-218) is non-deletable/renamable (onboarding-seeding call site DEFERRED per vault); FR-6/BR-4 compulsory shutdown (TC-LV-219) deducts balance first then spills to LOP; BR-1 no-entitlement/balance (TC-LV-220); BR-3 HR override convert/remove (TC-LV-221); NFR-3/BR-5 payroll-finalize immutability (TC-LV-222) is CONDITIONAL on the payroll-period lock (non-locked editable path verified); NFR-4/BR-6 audit + notification for all LOP assignments (TC-LV-223) with dispatch DEFERRED on the notifications module (queued/log-only seam verified); authz (TC-LV-224, non-HR 403), auth (TC-LV-225, 401), input sanitization (TC-LV-226), and cross-tenant IDOR on employeeId (TC-LV-227) cover the security surface; tenant isolation (TC-LV-ISO-041..043) confirms a Tenant A LOP dataset is invisible to and inert for Tenant B at the API, tenant-context, and EF-query-filter layers, and TC-LV-ISO-044 verifies the tenant+employee-scoped LOP/balance cache-key design by design with DB-fallback isolation verified live (partial pending Redis). US-LV-010 (Leave Cancellation by Employee) adds 21 functional/security/performance/accessibility test cases (TC-LV-189..209) + 4 dedicated multi-tenant isolation tests (TC-LV-ISO-037..040). All 4 acceptance criteria for US-LV-010 have direct coverage. US-LV-010 KEY notes: the pending-cancel path (AC-1, TC-LV-189) verifies Cancelled status with NO ledger entry, manager notification, and audit, while the approved-cancel path (AC-2, TC-LV-190) verifies the reversal `adjusted` (positive) ledger entry restoring balance plus audit; the Redis cache invalidation (FR-4) is DEFERRED module-wide per docs/vault/modules/leave-management.md and the LeaveLedger running-total DB-fallback is verified (not a silent gap); the already-started block (AC-3/BR-3, TC-LV-192/TC-LV-193) is enforced at the start-date==today boundary with the contact-HR message; the payroll-locked block (AC-4, TC-LV-194) is CONDITIONAL on the payroll module (non-locked path verified, `payroll_locked` error-contract surfaced); ownership/authz (BR-1) is verified at two layers -- a manager cannot cancel on behalf (TC-LV-199, 403) and an unrelated same-tenant employee cannot cancel via IDOR (TC-LV-200, 403/404); BR-2 refuses re-cancelling rejected (TC-LV-195) or already-cancelled (TC-LV-196, no double reversal/over-restore) requests; BR-5 reason-mandatory-for-approved/optional-for-pending is split across TC-LV-197/TC-LV-198; TC-LV-201 is the key concurrency test (manager approve vs employee cancel -> PostgreSQL xmin 409, only one wins, no mixed side effects); BR-4 carry-forward-pool restoration (TC-LV-202) is CONDITIONAL -- a general `adjusted` reversal is recorded if the carry-forward-vs-current-year split is not separately tracked, flagged for follow-up rather than passed silently; FR-7 N-day cancellation window (TC-LV-203) verifies the default anytime-before-start live and records the N>0 window CONDITIONAL on tenant-settings; NFR-1 (TC-LV-208) measures the DB-backed cancel path against 500ms P95; tenant isolation (TC-LV-ISO-037..039) confirms a Tenant A employee cannot cancel/resolve/restore a Tenant B request at the API, tenant-context, and EF-query-filter layers, and TC-LV-ISO-040 verifies the tenant+employee-scoped balance cache-key design by design with DB-fallback isolation verified live (partial pending Redis). US-LV-009 (Team Leave Calendar View) adds 20 functional/integration/security/performance/accessibility test cases (TC-LV-169..188) + 4 dedicated multi-tenant isolation tests (TC-LV-ISO-033..036). All 4 acceptance criteria for US-LV-009 have direct coverage. US-LV-009 KEY notes: the access-control rule (AC-2/BR-1) is verified at two layers -- TC-LV-171 (employee UI shows department-approved leaves as a neutral "on leave" block, no pending, no leave-type) and TC-LV-172 (the raw API payload to an employee omits pending entries and leaveTypeName/type-color server-side, so sensitive leave reasons cannot be read via the network), with TC-LV-185 confirming parameter-tampering cannot escalate scope; manager scope (BR-2, TC-LV-175) is limited to direct reports (ReportsToEmployeeId) and excludes other managers' teams; HR with Leave.ViewAll (BR-3, TC-LV-176) sees the whole tenant org; cancelled leaves are excluded (BR-4, TC-LV-177); half-day leaves are visually differentiated (BR-5, TC-LV-178); FR-7 public-holiday background highlights (TC-LV-179) integrate the implemented US-LV-007 holiday calendar; NFR-1 (TC-LV-186) measures the DB-backed month-range path against 300ms P95 with the Redis-cached read path DEFERRED module-wide, and TC-LV-ISO-036 verifies the tenant- and scope-scoped calendar cache-key design by design with DB-fallback isolation verified live (partial pending Redis); tenant isolation (TC-LV-ISO-033..035) confirms Tenant A's calendar is invisible to Tenant B at the API, tenant-context, and EF-query-filter layers. Rollup reconciliation: forward/backward rows, detailed-traceability, and coverage summaries for US-LV-007 (TC-LV-129..148, TC-LV-ISO-025..028) and US-LV-008 (TC-LV-149..168, TC-LV-ISO-029..032) were added in this pass to keep the root matrix consistent with the per-TC files and the module TEST-MATRIX (US-LV-008's prior rollup update had been interrupted); each of those TC files already carried its own internal traceability. US-LV-006 adds 20 functional/security/performance/accessibility test cases (TC-LV-109..128) + 4 dedicated multi-tenant isolation tests (TC-LV-ISO-021..024) for the employee leave-balance dashboard. All 5 acceptance criteria for US-LV-006 have direct coverage. US-LV-006 notes: balance correctness (TC-LV-110/TC-LV-112/TC-LV-115) verifies the BR-1 formula entitlement + carry_forward - used - expired + adjustments against both the card and the ledger running total; pending-separation (TC-LV-114) verifies that submitting a request raises "pending" without reducing "balance" until approval; the Redis balance cache (FR-5/NFR-1) is DEFERRED module-wide per docs/vault/modules/leave-management.md, so TC-LV-121 (cache-miss recompute/re-cache) and TC-LV-125 (200ms cached-read latency) verify the DB-computed LeaveLedger-running-total fallback and record the cache-specific steps as CONDITIONAL/DEFERRED (not silent gaps); TC-LV-ISO-024 verifies the tenant+employee-scoped cache-key pattern by design with DB-fallback isolation verified live; tenant isolation (TC-LV-ISO-021..023) and self-scope (TC-LV-122) confirm an employee sees only their own tenant-scoped data; year-selector (TC-LV-117) and leave-year boundary (TC-LV-118) verify read-only prior-year viewing and calendar-vs-fiscal aggregation. US-LV-005 adds 20 functional/security/performance/accessibility test cases (TC-LV-089..108) + 4 dedicated multi-tenant isolation tests (TC-LV-ISO-017..020) for the manager approve/reject flow. All 5 acceptance criteria for US-LV-005 have direct coverage. US-LV-005 notes: TC-LV-089/TC-LV-090 verify the DB/status/ledger/audit effects of approve/reject while the leave-approved/leave-rejected notification dispatch is the log-only seam DEFERRED on the notifications module and the Redis balance-cache invalidation (FR-3) is DEFERRED module-wide (the LeaveLedger running-total DB-fallback is verified); TC-LV-096 is the key concurrency test (PostgreSQL xmin optimistic concurrency -> 409 "already been actioned" on the second decision); TC-LV-097 multi-level approval (AC-4/FR-5) is CONDITIONAL/forward-looking on the approval-workflow configuration story (US-ADM-007), with single-level the verified default now; TC-LV-098 payroll-lock block (BR-4) is CONDITIONAL on the payroll module (non-locked approval verified now); TC-LV-ISO-020 balance-cache-key isolation is partial pending Redis (tenant-scoped key pattern and DB-fallback verified now). US-LV-004 notes: TC-LV-079 verifies the queue includes new requests on API reload while the real-time SignalR push (AC-5/FR-6) is dependent/deferred on the notifications module; TC-LV-077 detail-panel history-summary and team-calendar subsections are deferred on leave-history/US-LV-009 (the FR-5 numeric conflict count in TC-LV-078 still renders); TC-LV-088 multi-level-approval Scenario B is forward-looking on the leave-approval workflow story (direct-reports default verified now); TC-LV-ISO-016 balance-cache-key isolation is partial pending Redis (DB-fallback path and tenant-scoped key pattern verified now). US-LV-003 notes unchanged: TC-LV-056 holiday-exclusion steps depend on the holiday calendar (US-LV-007) and are conditionally blocked on it if that story is not yet implemented (weekend exclusion passes independently); FR-7 (multi-level approval routing) is downstream of submission and belongs to the leave-approval story; TC-LV-ISO-012 balance-cache-key isolation is partial pending Redis. US-LV-001/US-LV-002 deferred items unchanged: TC-LV-031 (FTE proration -- Employee entity lacks FTE field), TC-LV-042 (Redis balance cache), TC-LV-046 (job-level/tenure dimensions), TC-LV-ISO-008 partial (cache key isolation). All existing test cases for US-LV-001, US-LV-002, US-LV-003, US-LV-004, Core HR, and Authentication remain unchanged.*
