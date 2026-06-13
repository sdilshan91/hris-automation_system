---
title: Requirements Traceability Matrix
project: HRM SaaS Platform
created: 2026-05-11
status: draft
last_updated: 2026-06-13
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
| **TOTAL** | | | **55 test cases** | **55** | **10/10 AC** |

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

### Cross-Module Coverage Summary

| Module | User Stories | Test Cases | AC Coverage | Multi-Tenant Tests | Status |
|--------|------------|------------|-------------|-------------------|--------|
| Authentication & Authorization | 10 | 116 | 61/61 (100%) | 23 | PASS |
| Core HR (US-CHR-001 through US-CHR-012) | 12 | 372 | 61/61 (100%) | 67 | PASS |
| Leave Management (US-LV-001, US-LV-002) | 2 | 55 | 10/10 (100%) | 10 | PASS |
| **TOTAL** | **24** | **543** | **132/132 (100%)** | **100** | |

---

*Note: This traceability matrix covers Authentication & Authorization (10 stories, 116 TCs), Core HR (12 stories, 372 TCs), and Leave Management (2 stories, 55 TCs). US-LV-002 adds 22 functional test cases + 4 dedicated multi-tenant isolation tests for leave entitlement configuration. Deferred items in US-LV-002: TC-LV-031 (FTE proration -- Employee entity lacks FTE field), TC-LV-042 (Redis balance cache -- caching not yet implemented), TC-LV-046 (job-level/tenure dimensions as standalone rule criteria -- entity dependencies), TC-LV-ISO-008 partial (cache key isolation -- pending Redis). FR-6 (Redis caching) and NFR-3 (cache TTL/invalidation) are infrastructure dependencies. BR-2 (FTE proration) requires FTE field on Employee entity. All 5 acceptance criteria for US-LV-002 have direct test coverage. All existing test cases for US-LV-001, Core HR, and Authentication remain unchanged.*
