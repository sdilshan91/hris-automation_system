---
module: Authentication & Authorization
total_user_stories: 13
total_test_cases: 161
created: 2026-05-11
updated: 2026-07-24
status: draft
---

# Authentication & Authorization -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories | 13 |
| Total Test Cases | 161 |
| Critical Priority | 73 |
| High Priority | 76 |
| Medium Priority | 12 |
| Low Priority | 0 |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-AUTH-001 | Admin login with username and password | TC-AUTH-001, TC-AUTH-002, TC-AUTH-003, TC-AUTH-004 | 4 |
| US-AUTH-002 | JWT token issuance and refresh token flow | TC-AUTH-005, TC-AUTH-006, TC-AUTH-007 | 3 |
| US-AUTH-003 | User logout and token invalidation | TC-AUTH-008, TC-AUTH-009 | 2 |
| US-AUTH-004 | Password reset flow | TC-AUTH-010, TC-AUTH-011, TC-AUTH-012 | 3 |
| US-AUTH-005 | Multi-factor authentication (TOTP) | TC-AUTH-013, TC-AUTH-014, TC-AUTH-015, TC-AUTH-029, TC-AUTH-030, TC-AUTH-031, TC-AUTH-032, TC-AUTH-033, TC-AUTH-034, TC-AUTH-035, TC-AUTH-036, TC-AUTH-037, TC-AUTH-038 | 13 |
| US-AUTH-006 | Role-based access control (RBAC) | TC-AUTH-016, TC-AUTH-017, TC-AUTH-018, TC-AUTH-039, TC-AUTH-040, TC-AUTH-041, TC-AUTH-042, TC-AUTH-043, TC-AUTH-044, TC-AUTH-045, TC-AUTH-046, TC-AUTH-047, TC-AUTH-048, TC-AUTH-049, TC-AUTH-050 | 15 |
| US-AUTH-007 | Tenant resolution from subdomain | TC-AUTH-019, TC-AUTH-020, TC-AUTH-021, TC-AUTH-051, TC-AUTH-052, TC-AUTH-053, TC-AUTH-054, TC-AUTH-055, TC-AUTH-056, TC-AUTH-057, TC-AUTH-058 | 11 |
| US-AUTH-008 | Cross-tenant user switching | TC-AUTH-022, TC-AUTH-023, TC-AUTH-059, TC-AUTH-060, TC-AUTH-061, TC-AUTH-062, TC-AUTH-063, TC-AUTH-064 | 8 |
| US-AUTH-009 | Session management and concurrent limits | TC-AUTH-024, TC-AUTH-025, TC-AUTH-065, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-069, TC-AUTH-070, TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-074, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077, TC-AUTH-078, TC-AUTH-079, TC-AUTH-080, TC-AUTH-081, TC-AUTH-082 | 20 |
| US-AUTH-010 | Account lockout after failed attempts | TC-AUTH-026, TC-AUTH-027, TC-AUTH-028, TC-AUTH-083, TC-AUTH-084, TC-AUTH-085, TC-AUTH-086, TC-AUTH-087, TC-AUTH-088, TC-AUTH-089, TC-AUTH-090, TC-AUTH-091, TC-AUTH-092, TC-AUTH-093, TC-AUTH-094, TC-AUTH-095, TC-AUTH-096, TC-AUTH-097, TC-AUTH-098, TC-AUTH-099, TC-AUTH-100, TC-AUTH-101, TC-AUTH-102, TC-AUTH-103, TC-AUTH-104, TC-AUTH-105, TC-AUTH-106, TC-AUTH-107, TC-AUTH-108, TC-AUTH-109, TC-AUTH-110, TC-AUTH-111, TC-AUTH-112 | 33 |
| US-AUTH-011 | Entra OIDC authentication foundation | TC-AUTH-137, TC-AUTH-138, TC-AUTH-139, TC-AUTH-140, TC-AUTH-141, TC-AUTH-142, TC-AUTH-143, TC-AUTH-144, TC-AUTH-145, TC-AUTH-146, TC-AUTH-147, TC-AUTH-148, TC-AUTH-149, TC-AUTH-150, TC-AUTH-151, TC-AUTH-152, TC-AUTH-153, TC-AUTH-154, TC-AUTH-ISO-007 | 19 |
| US-AUTH-012 | Per-tenant SSO configuration | TC-AUTH-115, TC-AUTH-116, TC-AUTH-117, TC-AUTH-118, TC-AUTH-119, TC-AUTH-120, TC-AUTH-121, TC-AUTH-122, TC-AUTH-123, TC-AUTH-124, TC-AUTH-125, TC-AUTH-ISO-005 | 12 |
| US-AUTH-016 | SSO enforcement, break-glass & admin-consent onboarding | TC-AUTH-126, TC-AUTH-127, TC-AUTH-128, TC-AUTH-129, TC-AUTH-130, TC-AUTH-131, TC-AUTH-132, TC-AUTH-133, TC-AUTH-134, TC-AUTH-135, TC-AUTH-136, TC-AUTH-ISO-006 | 12 |
| Cross-cutting | Multi-tenant isolation | TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004, TC-AUTH-ISO-005, TC-AUTH-ISO-006, TC-AUTH-ISO-007 | 7 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional | TC-AUTH-001, TC-AUTH-004, TC-AUTH-005, TC-AUTH-006, TC-AUTH-007, TC-AUTH-008, TC-AUTH-010, TC-AUTH-011, TC-AUTH-013, TC-AUTH-014, TC-AUTH-016, TC-AUTH-019, TC-AUTH-022, TC-AUTH-024, TC-AUTH-025, TC-AUTH-028, TC-AUTH-029, TC-AUTH-030, TC-AUTH-032, TC-AUTH-033, TC-AUTH-039, TC-AUTH-040, TC-AUTH-042, TC-AUTH-043, TC-AUTH-046, TC-AUTH-047, TC-AUTH-051, TC-AUTH-059, TC-AUTH-063, TC-AUTH-065, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-069, TC-AUTH-070, TC-AUTH-071, TC-AUTH-072, TC-AUTH-074, TC-AUTH-079, TC-AUTH-080, TC-AUTH-081, TC-AUTH-083, TC-AUTH-086, TC-AUTH-087, TC-AUTH-088, TC-AUTH-089, TC-AUTH-091, TC-AUTH-099, TC-AUTH-100, TC-AUTH-103, TC-AUTH-104, TC-AUTH-106, TC-AUTH-108, TC-AUTH-110, TC-AUTH-111, TC-AUTH-115, TC-AUTH-117, TC-AUTH-118, TC-AUTH-126, TC-AUTH-131, TC-AUTH-132, TC-AUTH-133, TC-AUTH-134, TC-AUTH-137, TC-AUTH-138, TC-AUTH-147, TC-AUTH-148, TC-AUTH-152, TC-AUTH-154 | 69 |
| Security | TC-AUTH-002, TC-AUTH-003, TC-AUTH-009, TC-AUTH-012, TC-AUTH-015, TC-AUTH-017, TC-AUTH-018, TC-AUTH-020, TC-AUTH-021, TC-AUTH-023, TC-AUTH-026, TC-AUTH-027, TC-AUTH-031, TC-AUTH-034, TC-AUTH-035, TC-AUTH-037, TC-AUTH-041, TC-AUTH-045, TC-AUTH-050, TC-AUTH-052, TC-AUTH-053, TC-AUTH-054, TC-AUTH-060, TC-AUTH-061, TC-AUTH-062, TC-AUTH-073, TC-AUTH-075, TC-AUTH-076, TC-AUTH-078, TC-AUTH-082, TC-AUTH-084, TC-AUTH-085, TC-AUTH-090, TC-AUTH-092, TC-AUTH-093, TC-AUTH-094, TC-AUTH-095, TC-AUTH-096, TC-AUTH-097, TC-AUTH-098, TC-AUTH-101, TC-AUTH-102, TC-AUTH-105, TC-AUTH-107, TC-AUTH-112, TC-AUTH-116, TC-AUTH-119, TC-AUTH-120, TC-AUTH-122, TC-AUTH-127, TC-AUTH-128, TC-AUTH-129, TC-AUTH-130, TC-AUTH-135, TC-AUTH-139, TC-AUTH-140, TC-AUTH-141, TC-AUTH-142, TC-AUTH-143, TC-AUTH-144, TC-AUTH-145, TC-AUTH-146, TC-AUTH-149, TC-AUTH-153, TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004, TC-AUTH-ISO-005, TC-AUTH-ISO-006, TC-AUTH-ISO-007 | 71 |
| Integration | TC-AUTH-113, TC-AUTH-114, TC-AUTH-123, TC-AUTH-151 | 4 |
| Performance | TC-AUTH-036, TC-AUTH-044, TC-AUTH-049, TC-AUTH-055, TC-AUTH-056, TC-AUTH-057, TC-AUTH-064, TC-AUTH-077, TC-AUTH-121, TC-AUTH-124, TC-AUTH-150 | 11 |
| Accessibility | TC-AUTH-038, TC-AUTH-048, TC-AUTH-058, TC-AUTH-109, TC-AUTH-125, TC-AUTH-136 | 6 |
## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-AUTH-001, TC-AUTH-005, TC-AUTH-006, TC-AUTH-007, TC-AUTH-008, TC-AUTH-010, TC-AUTH-011, TC-AUTH-013, TC-AUTH-014, TC-AUTH-016, TC-AUTH-019, TC-AUTH-022, TC-AUTH-024, TC-AUTH-025, TC-AUTH-028, TC-AUTH-029, TC-AUTH-030, TC-AUTH-032, TC-AUTH-033, TC-AUTH-039, TC-AUTH-046, TC-AUTH-056, TC-AUTH-059, TC-AUTH-063, TC-AUTH-065, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-069, TC-AUTH-070, TC-AUTH-071, TC-AUTH-079, TC-AUTH-080, TC-AUTH-081, TC-AUTH-086, TC-AUTH-087, TC-AUTH-088, TC-AUTH-089, TC-AUTH-091, TC-AUTH-094, TC-AUTH-100, TC-AUTH-103, TC-AUTH-106, TC-AUTH-108, TC-AUTH-115, TC-AUTH-126, TC-AUTH-127, TC-AUTH-131, TC-AUTH-132, TC-AUTH-134, TC-AUTH-137, TC-AUTH-138, TC-AUTH-151, TC-AUTH-154 | 54 |
| Negative Test | TC-AUTH-002, TC-AUTH-003, TC-AUTH-004, TC-AUTH-007, TC-AUTH-009, TC-AUTH-010, TC-AUTH-012, TC-AUTH-015, TC-AUTH-017, TC-AUTH-020, TC-AUTH-021, TC-AUTH-023, TC-AUTH-024, TC-AUTH-026, TC-AUTH-027, TC-AUTH-031, TC-AUTH-032, TC-AUTH-034, TC-AUTH-035, TC-AUTH-037, TC-AUTH-040, TC-AUTH-041, TC-AUTH-042, TC-AUTH-045, TC-AUTH-052, TC-AUTH-053, TC-AUTH-054, TC-AUTH-057, TC-AUTH-058, TC-AUTH-060, TC-AUTH-062, TC-AUTH-065, TC-AUTH-072, TC-AUTH-073, TC-AUTH-075, TC-AUTH-076, TC-AUTH-080, TC-AUTH-083, TC-AUTH-084, TC-AUTH-085, TC-AUTH-090, TC-AUTH-092, TC-AUTH-093, TC-AUTH-095, TC-AUTH-096, TC-AUTH-102, TC-AUTH-105, TC-AUTH-107, TC-AUTH-110, TC-AUTH-112, TC-AUTH-116, TC-AUTH-117, TC-AUTH-118, TC-AUTH-119, TC-AUTH-120, TC-AUTH-122, TC-AUTH-123, TC-AUTH-124, TC-AUTH-ISO-001, TC-AUTH-ISO-003, TC-AUTH-ISO-004, TC-AUTH-ISO-005, TC-AUTH-126, TC-AUTH-128, TC-AUTH-129, TC-AUTH-130, TC-AUTH-133, TC-AUTH-135, TC-AUTH-ISO-006, TC-AUTH-139, TC-AUTH-140, TC-AUTH-141, TC-AUTH-142, TC-AUTH-143, TC-AUTH-144, TC-AUTH-145, TC-AUTH-146, TC-AUTH-147, TC-AUTH-148, TC-AUTH-151, TC-AUTH-152, TC-AUTH-153, TC-AUTH-154, TC-AUTH-ISO-007 | 84 |
| Boundary Test | TC-AUTH-004, TC-AUTH-012, TC-AUTH-020, TC-AUTH-024, TC-AUTH-025, TC-AUTH-026, TC-AUTH-028, TC-AUTH-043, TC-AUTH-044, TC-AUTH-053, TC-AUTH-061, TC-AUTH-074, TC-AUTH-079, TC-AUTH-080, TC-AUTH-084, TC-AUTH-086, TC-AUTH-089, TC-AUTH-102, TC-AUTH-103, TC-AUTH-111, TC-AUTH-112, TC-AUTH-117, TC-AUTH-118, TC-AUTH-121, TC-AUTH-141, TC-AUTH-143, TC-AUTH-145, TC-AUTH-146, TC-AUTH-152, TC-AUTH-153 | 30 |
| Security Test | TC-AUTH-002, TC-AUTH-003, TC-AUTH-005, TC-AUTH-006, TC-AUTH-009, TC-AUTH-010, TC-AUTH-012, TC-AUTH-013, TC-AUTH-014, TC-AUTH-015, TC-AUTH-016, TC-AUTH-017, TC-AUTH-018, TC-AUTH-020, TC-AUTH-021, TC-AUTH-023, TC-AUTH-024, TC-AUTH-026, TC-AUTH-027, TC-AUTH-029, TC-AUTH-030, TC-AUTH-031, TC-AUTH-032, TC-AUTH-034, TC-AUTH-035, TC-AUTH-037, TC-AUTH-040, TC-AUTH-041, TC-AUTH-042, TC-AUTH-045, TC-AUTH-046, TC-AUTH-047, TC-AUTH-050, TC-AUTH-052, TC-AUTH-053, TC-AUTH-054, TC-AUTH-057, TC-AUTH-058, TC-AUTH-059, TC-AUTH-060, TC-AUTH-061, TC-AUTH-062, TC-AUTH-063, TC-AUTH-064, TC-AUTH-065, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-070, TC-AUTH-072, TC-AUTH-073, TC-AUTH-075, TC-AUTH-076, TC-AUTH-078, TC-AUTH-080, TC-AUTH-082, TC-AUTH-083, TC-AUTH-084, TC-AUTH-085, TC-AUTH-090, TC-AUTH-091, TC-AUTH-092, TC-AUTH-093, TC-AUTH-094, TC-AUTH-095, TC-AUTH-096, TC-AUTH-097, TC-AUTH-098, TC-AUTH-099, TC-AUTH-101, TC-AUTH-102, TC-AUTH-105, TC-AUTH-107, TC-AUTH-111, TC-AUTH-116, TC-AUTH-117, TC-AUTH-119, TC-AUTH-120, TC-AUTH-122, TC-AUTH-123, TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004, TC-AUTH-ISO-005, TC-AUTH-126, TC-AUTH-127, TC-AUTH-128, TC-AUTH-129, TC-AUTH-130, TC-AUTH-131, TC-AUTH-132, TC-AUTH-133, TC-AUTH-135, TC-AUTH-ISO-006, TC-AUTH-137, TC-AUTH-138, TC-AUTH-139, TC-AUTH-140, TC-AUTH-141, TC-AUTH-142, TC-AUTH-143, TC-AUTH-144, TC-AUTH-145, TC-AUTH-146, TC-AUTH-147, TC-AUTH-148, TC-AUTH-149, TC-AUTH-151, TC-AUTH-153, TC-AUTH-154, TC-AUTH-ISO-007 | 112 |
| Multi-Tenant Isolation | TC-AUTH-018, TC-AUTH-022, TC-AUTH-023, TC-AUTH-037, TC-AUTH-051, TC-AUTH-052, TC-AUTH-054, TC-AUTH-059, TC-AUTH-060, TC-AUTH-061, TC-AUTH-062, TC-AUTH-063, TC-AUTH-064, TC-AUTH-075, TC-AUTH-082, TC-AUTH-092, TC-AUTH-093, TC-AUTH-094, TC-AUTH-105, TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004, TC-AUTH-ISO-005, TC-AUTH-ISO-006, TC-AUTH-139, TC-AUTH-142, TC-AUTH-143, TC-AUTH-ISO-007 | 29 |
| Performance Test | TC-AUTH-036, TC-AUTH-044, TC-AUTH-047, TC-AUTH-049, TC-AUTH-055, TC-AUTH-056, TC-AUTH-057, TC-AUTH-064, TC-AUTH-077, TC-AUTH-098, TC-AUTH-100, TC-AUTH-104, TC-AUTH-121, TC-AUTH-123, TC-AUTH-124, TC-AUTH-150 | 16 |
| Accessibility Test | TC-AUTH-038, TC-AUTH-048, TC-AUTH-058, TC-AUTH-109, TC-AUTH-125, TC-AUTH-136 | 6 |

## Acceptance Criteria Coverage

| User Story | AC | Covered By Test Cases |
|------------|----|-----------------------|
| US-AUTH-001 | AC-1 | TC-AUTH-001 |
| US-AUTH-001 | AC-2 | TC-AUTH-002, TC-AUTH-003 |
| US-AUTH-001 | AC-3 | TC-AUTH-ISO-001 |
| US-AUTH-001 | AC-4 | TC-AUTH-021 |
| US-AUTH-001 | AC-5 | TC-AUTH-014 |
| US-AUTH-001 | AC-6 | TC-AUTH-020 |
| US-AUTH-002 | AC-1 | TC-AUTH-005 |
| US-AUTH-002 | AC-2 | TC-AUTH-006, TC-AUTH-007 |
| US-AUTH-002 | AC-3 | TC-AUTH-009 |
| US-AUTH-002 | AC-4 | TC-AUTH-007 |
| US-AUTH-002 | AC-5 | TC-AUTH-021 |
| US-AUTH-002 | AC-6 | TC-AUTH-009 |
| US-AUTH-002 | AC-7 | TC-AUTH-005 |
| US-AUTH-003 | AC-1 | TC-AUTH-008 |
| US-AUTH-003 | AC-2 | TC-AUTH-008, TC-AUTH-009 |
| US-AUTH-003 | AC-3 | TC-AUTH-009 |
| US-AUTH-003 | AC-4 | TC-AUTH-008 |
| US-AUTH-003 | AC-5 | TC-AUTH-008 |
| US-AUTH-004 | AC-1 | TC-AUTH-010 |
| US-AUTH-004 | AC-2 | TC-AUTH-010 |
| US-AUTH-004 | AC-3 | TC-AUTH-011 |
| US-AUTH-004 | AC-4 | TC-AUTH-012 |
| US-AUTH-004 | AC-5 | TC-AUTH-012 |
| US-AUTH-004 | AC-6 | TC-AUTH-011 |
| US-AUTH-005 | AC-1 | TC-AUTH-029 |
| US-AUTH-005 | AC-2 | TC-AUTH-013 |
| US-AUTH-005 | AC-3 | TC-AUTH-013 |
| US-AUTH-005 | AC-4 | TC-AUTH-014 |
| US-AUTH-005 | AC-5 | TC-AUTH-015 |
| US-AUTH-005 | AC-6 | TC-AUTH-033 |
| US-AUTH-005 | AC-7 | TC-AUTH-030 |
| US-AUTH-006 | AC-1 | TC-AUTH-016, TC-AUTH-039, TC-AUTH-048 |
| US-AUTH-006 | AC-2 | TC-AUTH-016, TC-AUTH-039, TC-AUTH-044, TC-AUTH-046, TC-AUTH-050 |
| US-AUTH-006 | AC-3 | TC-AUTH-016, TC-AUTH-039, TC-AUTH-042, TC-AUTH-043, TC-AUTH-046 |
| US-AUTH-006 | AC-4 | TC-AUTH-017, TC-AUTH-039, TC-AUTH-049, TC-AUTH-050 |
| US-AUTH-006 | AC-5 | TC-AUTH-017, TC-AUTH-045 |
| US-AUTH-006 | AC-6 | TC-AUTH-016, TC-AUTH-040, TC-AUTH-050 |
| US-AUTH-006 | AC-7 | TC-AUTH-018, TC-AUTH-ISO-004 |
| US-AUTH-007 | AC-1 | TC-AUTH-019, TC-AUTH-054, TC-AUTH-055, TC-AUTH-057 |
| US-AUTH-007 | AC-2 | TC-AUTH-020, TC-AUTH-053, TC-AUTH-058 |
| US-AUTH-007 | AC-3 | TC-AUTH-051 |
| US-AUTH-007 | AC-4 | TC-AUTH-052 |
| US-AUTH-007 | AC-5 | TC-AUTH-021, TC-AUTH-058 |
| US-AUTH-007 | AC-6 | TC-AUTH-019, TC-AUTH-056, TC-AUTH-057 |
| US-AUTH-008 | AC-1 | TC-AUTH-022, TC-AUTH-059, TC-AUTH-064 |
| US-AUTH-008 | AC-2 | TC-AUTH-022, TC-AUTH-059, TC-AUTH-062, TC-AUTH-063 |
| US-AUTH-008 | AC-3 | TC-AUTH-023, TC-AUTH-060, TC-AUTH-062 |
| US-AUTH-008 | AC-4 | TC-AUTH-023, TC-AUTH-060 |
| US-AUTH-008 | AC-5 | TC-AUTH-022, TC-AUTH-061, TC-AUTH-062, TC-AUTH-063 |
| US-AUTH-009 | AC-1 | TC-AUTH-024, TC-AUTH-025, TC-AUTH-065, TC-AUTH-066, TC-AUTH-074, TC-AUTH-075, TC-AUTH-078, TC-AUTH-080, TC-AUTH-082 |
| US-AUTH-009 | AC-2 | TC-AUTH-024, TC-AUTH-067, TC-AUTH-074, TC-AUTH-075, TC-AUTH-078, TC-AUTH-081 |
| US-AUTH-009 | AC-3 | TC-AUTH-024, TC-AUTH-068, TC-AUTH-074, TC-AUTH-078 |
| US-AUTH-009 | AC-4 | TC-AUTH-025, TC-AUTH-069, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077 |
| US-AUTH-009 | AC-5 | TC-AUTH-025, TC-AUTH-070, TC-AUTH-073, TC-AUTH-075, TC-AUTH-078 |
| US-AUTH-009 | AC-6 | TC-AUTH-025, TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-076 |
| US-AUTH-010 | AC-1 | TC-AUTH-026, TC-AUTH-083, TC-AUTH-111 |
| US-AUTH-010 | AC-2 | TC-AUTH-026, TC-AUTH-084, TC-AUTH-111 |
| US-AUTH-010 | AC-3 | TC-AUTH-027, TC-AUTH-085 |
| US-AUTH-010 | AC-4 | TC-AUTH-028, TC-AUTH-086 |
| US-AUTH-010 | AC-5 | TC-AUTH-028, TC-AUTH-087, TC-AUTH-112 |
| US-AUTH-010 | AC-6 | TC-AUTH-028, TC-AUTH-088 |
| US-AUTH-011 | AC-1 | TC-AUTH-137, TC-AUTH-152, TC-AUTH-153 |
| US-AUTH-011 | AC-2 | TC-AUTH-138, TC-AUTH-139, TC-AUTH-140, TC-AUTH-141, TC-AUTH-ISO-007 |
| US-AUTH-011 | AC-3 | TC-AUTH-138, TC-AUTH-144, TC-AUTH-145, TC-AUTH-147 |
| US-AUTH-011 | AC-4 | TC-AUTH-138, TC-AUTH-154 |
| US-AUTH-011 | AC-5 | TC-AUTH-140, TC-AUTH-141 |
| US-AUTH-011 | AC-6 | TC-AUTH-142, TC-AUTH-143, TC-AUTH-144, TC-AUTH-145, TC-AUTH-146, TC-AUTH-ISO-007 |
| US-AUTH-011 | AC-7 | TC-AUTH-148 |
| US-AUTH-012 | AC-1 | TC-AUTH-115, TC-AUTH-124, TC-AUTH-125 |
| US-AUTH-012 | AC-2 | TC-AUTH-116, TC-AUTH-125 |
| US-AUTH-012 | AC-3 | TC-AUTH-115, TC-AUTH-118 |
| US-AUTH-012 | AC-4 | TC-AUTH-117 |
| US-AUTH-012 | AC-5 | TC-AUTH-119 |
| US-AUTH-012 | AC-6 | TC-AUTH-ISO-005 |
| US-AUTH-012 | AC-7 | TC-AUTH-120 |
| US-AUTH-016 | AC-1 | TC-AUTH-126, TC-AUTH-134, TC-AUTH-136, TC-AUTH-ISO-006 |
| US-AUTH-016 | AC-2 | TC-AUTH-127, TC-AUTH-128, TC-AUTH-135 |
| US-AUTH-016 | AC-3 | TC-AUTH-129, TC-AUTH-136 |
| US-AUTH-016 | AC-4 | TC-AUTH-131, TC-AUTH-136 |
| US-AUTH-016 | AC-5 | TC-AUTH-132 |
| US-AUTH-016 | AC-6 | TC-AUTH-133 |
| US-AUTH-016 | AC-7 | TC-AUTH-130 |

## API Endpoint Coverage

| Endpoint | Method | Covered By |
|----------|--------|------------|
| /api/v1/auth/sso/challenge | GET | TC-AUTH-137, TC-AUTH-152, TC-AUTH-153 |
| /api/v1/auth/sso/callback | GET | TC-AUTH-138, TC-AUTH-139, TC-AUTH-140, TC-AUTH-141, TC-AUTH-142, TC-AUTH-143, TC-AUTH-144, TC-AUTH-145, TC-AUTH-146, TC-AUTH-147, TC-AUTH-148, TC-AUTH-153, TC-AUTH-154, TC-AUTH-ISO-007 |
| /api/v1/auth/login | POST | TC-AUTH-001, TC-AUTH-002, TC-AUTH-003, TC-AUTH-004, TC-AUTH-014, TC-AUTH-026, TC-AUTH-027, TC-AUTH-028, TC-AUTH-029, TC-AUTH-030, TC-AUTH-037, TC-AUTH-065, TC-AUTH-066, TC-AUTH-074, TC-AUTH-083, TC-AUTH-084, TC-AUTH-085, TC-AUTH-086, TC-AUTH-088, TC-AUTH-089, TC-AUTH-090, TC-AUTH-091, TC-AUTH-092, TC-AUTH-095, TC-AUTH-097, TC-AUTH-098, TC-AUTH-099, TC-AUTH-103, TC-AUTH-105, TC-AUTH-110, TC-AUTH-111, TC-AUTH-126, TC-AUTH-127, TC-AUTH-128, TC-AUTH-130, TC-AUTH-134, TC-AUTH-ISO-006 |
| /api/v1/auth/refresh | POST | TC-AUTH-006, TC-AUTH-007, TC-AUTH-009, TC-AUTH-046, TC-AUTH-047, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-074, TC-AUTH-081, TC-AUTH-082, TC-AUTH-095 |
| /api/v1/auth/logout | POST | TC-AUTH-008, TC-AUTH-009, TC-AUTH-065, TC-AUTH-072 |
| /api/v1/auth/forgot-password | POST | TC-AUTH-010, TC-AUTH-091 |
| /api/v1/auth/reset-password | POST | TC-AUTH-011, TC-AUTH-012, TC-AUTH-091 |
| /api/v1/auth/mfa/enroll | POST | TC-AUTH-013, TC-AUTH-029, TC-AUTH-033, TC-AUTH-035 |
| /api/v1/auth/mfa/verify | POST | TC-AUTH-013, TC-AUTH-014, TC-AUTH-015, TC-AUTH-029, TC-AUTH-033, TC-AUTH-090, TC-AUTH-110 |
| /api/v1/auth/mfa/challenge | POST | TC-AUTH-030, TC-AUTH-031, TC-AUTH-036, TC-AUTH-037 |
| /api/v1/auth/mfa | DELETE | TC-AUTH-033, TC-AUTH-034, TC-AUTH-037 |
| /api/v1/tenant/auth-settings | GET | TC-AUTH-032, TC-AUTH-080, TC-AUTH-115, TC-AUTH-116, TC-AUTH-117, TC-AUTH-122, TC-AUTH-ISO-005, TC-AUTH-129, TC-AUTH-132, TC-AUTH-133, TC-AUTH-134, TC-AUTH-135, TC-AUTH-ISO-006 |
| /api/v1/tenant/auth-settings | PUT | TC-AUTH-032, TC-AUTH-034, TC-AUTH-037, TC-AUTH-080, TC-AUTH-102, TC-AUTH-115, TC-AUTH-116, TC-AUTH-117, TC-AUTH-118, TC-AUTH-119, TC-AUTH-120, TC-AUTH-121, TC-AUTH-122, TC-AUTH-123, TC-AUTH-ISO-005, TC-AUTH-129, TC-AUTH-134, TC-AUTH-135, TC-AUTH-ISO-006 |
| /api/v1/tenant/sso/admin-consent-url | GET | TC-AUTH-131 |
| /api/v1/tenant/sso/consent-return (fixed redirect) | GET | TC-AUTH-132, TC-AUTH-133 |
| /api/v1/tenant/roles | GET | TC-AUTH-016, TC-AUTH-039, TC-AUTH-040, TC-AUTH-041, TC-AUTH-044, TC-AUTH-ISO-004 |
| /api/v1/tenant/roles | POST | TC-AUTH-016, TC-AUTH-039, TC-AUTH-041, TC-AUTH-044, TC-AUTH-050 |
| /api/v1/tenant/roles/{id} | GET | TC-AUTH-044, TC-AUTH-046, TC-AUTH-ISO-004 |
| /api/v1/tenant/roles/{id} | PUT | TC-AUTH-040, TC-AUTH-047, TC-AUTH-050 |
| /api/v1/tenant/roles/{id} | DELETE | TC-AUTH-040, TC-AUTH-046, TC-AUTH-050 |
| /api/v1/tenant/users | GET | TC-AUTH-016, TC-AUTH-017, TC-AUTH-042 |
| /api/v1/tenant/users/{id} | PATCH | TC-AUTH-039, TC-AUTH-042, TC-AUTH-043, TC-AUTH-046, TC-AUTH-047, TC-AUTH-050, TC-AUTH-ISO-004 |
| /api/v1/tenant/users/by-user/{id}/unlock | POST | TC-AUTH-087, TC-AUTH-093, TC-AUTH-094, TC-AUTH-107, TC-AUTH-112 |
| /api/v1/tenant/leave/requests | GET | TC-AUTH-039, TC-AUTH-049 |
| /api/v1/tenant/leave/requests/{id}/approve | POST | TC-AUTH-045 |
| /api/v1/tenant/payroll/runs | GET | TC-AUTH-017, TC-AUTH-039, TC-AUTH-049 |
| /api/v1/tenant/audit-log | GET | TC-AUTH-050, TC-AUTH-078, TC-AUTH-101 |
| /api/v1/auth/my-tenants | GET | TC-AUTH-022, TC-AUTH-059, TC-AUTH-062, TC-AUTH-064 |
| /api/v1/auth/switch-tenant | POST | TC-AUTH-022, TC-AUTH-023, TC-AUTH-037, TC-AUTH-059, TC-AUTH-060, TC-AUTH-061, TC-AUTH-062, TC-AUTH-063, TC-AUTH-064 |
| /api/v1/auth/me | GET | TC-AUTH-067, TC-AUTH-077, TC-AUTH-081, TC-AUTH-095 |
| /api/v1/auth/me/sessions | GET | TC-AUTH-025, TC-AUTH-069, TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077 |
| /api/v1/auth/me/sessions/{sessionId}/revoke | POST | TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-076, TC-AUTH-078 |
| /api/v1/tenant/users/by-user/{id}/sessions | GET | TC-AUTH-069, TC-AUTH-073, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077 |
| /api/v1/tenant/users/by-user/{id}/sessions/revoke | POST | TC-AUTH-025, TC-AUTH-070, TC-AUTH-073, TC-AUTH-075, TC-AUTH-078 |

## FR/NFR/BR Coverage for US-AUTH-011

| Requirement | Covered By Test Cases |
|-------------|-----------------------|
| FR-1 (SSO challenge endpoint redirects to Entra `organizations` via Auth-Code flow) | TC-AUTH-137, TC-AUTH-152, TC-AUTH-153 |
| FR-2 (single fixed callback handles the code response for all tenants) | TC-AUTH-138, TC-AUTH-ISO-007 |
| FR-3 (tenant carried in a tamper-evident, single-use `state`, resolved from it) | TC-AUTH-137, TC-AUTH-139, TC-AUTH-140, TC-AUTH-141, TC-AUTH-152, TC-AUTH-ISO-007 |
| FR-4 (server-side code exchange keeps client secret confidential) | TC-AUTH-138, TC-AUTH-147, TC-AUTH-149 |
| FR-5 (full id_token validation: JWKS signature, aud, iss, exp, nonce) | TC-AUTH-138, TC-AUTH-144, TC-AUTH-145, TC-AUTH-146 |
| FR-6 (on success issue app JWT + refresh via existing JwtService) | TC-AUTH-138, TC-AUTH-154 |
| FR-7 (OIDC scheme registered in addition to AddJwtBearer; bearer default unaffected) | TC-AUTH-151 |
| FR-8 (all SSO outcomes audited with tenant, tid/oid, outcome; non-sensitive) | TC-AUTH-138, TC-AUTH-140, TC-AUTH-144, TC-AUTH-148, TC-AUTH-149 |
| FR-9 (custom tid/issuer isolation seam; hardened fail-closed allow-list) | TC-AUTH-142, TC-AUTH-143, TC-AUTH-144, TC-AUTH-ISO-007 |
| NFR-1 (Microsoft JWKS cached/auto-refreshed, warm lookup <= 5ms) | TC-AUTH-150 |
| NFR-2 (client secret from user-secrets/Key Vault, never committed) | TC-AUTH-149, TC-AUTH-153 |
| NFR-3 (state/nonce cryptographically random >= 128-bit, single-use, replay detectable) | TC-AUTH-137, TC-AUTH-140, TC-AUTH-141 |
| NFR-4 (callback round-trip P95 <= 2s) | TC-AUTH-150 |
| NFR-5 (no id_token/access_token/code/secret in logs) | TC-AUTH-149, TC-AUTH-140, TC-AUTH-142, TC-AUTH-144, TC-AUTH-145, TC-AUTH-147 |
| BR-1 (SSO terminates in the same app JWT; downstream unchanged except auth_method) | TC-AUTH-138, TC-AUTH-151, TC-AUTH-154 |
| BR-2 (authority always `/organizations`, never `/consumers` or `/common`) | TC-AUTH-137 |
| BR-3 (IdP errors/cancellations do not count toward lockout) | TC-AUTH-148 |
| BR-4 (fixed redirect host is the only registered redirect URI) | TC-AUTH-137, TC-AUTH-138 |
| BR-5 (foundation stays disabled/fail-closed until isolation is enforced) | TC-AUTH-142, TC-AUTH-143, TC-AUTH-153 |

## FR/NFR/BR Coverage for US-AUTH-016

| Requirement | Covered By Test Cases |
|-------------|-----------------------|
| FR-1 (`enforcement_mode = sso_only` refuses local logins; permits SSO + break-glass) | TC-AUTH-126, TC-AUTH-130, TC-AUTH-ISO-006 |
| FR-2 (break-glass admin always authenticates locally regardless of `sso_only`) | TC-AUTH-127, TC-AUTH-128, TC-AUTH-130 |
| FR-3 (cannot enable `sso_only` without a designated break-glass admin) | TC-AUTH-129 |
| FR-4 (break-glass logins high-severity audited + admin notification) | TC-AUTH-127, TC-AUTH-128 |
| FR-5 (generate customer admin-consent URL; handle consent return) | TC-AUTH-131 |
| FR-6 (on consent, capture customer `tid` into allow-list; mark SSO ready) | TC-AUTH-132 |
| FR-7 (enforcement changes, consent outcomes, break-glass logins audited) | TC-AUTH-127, TC-AUTH-132, TC-AUTH-133, TC-AUTH-135 |
| FR-8 (revert to `optional` at any time, no data loss, re-enables local login) | TC-AUTH-134 |
| NFR-1 (break-glass works even if Entra/vendor app/allow-list unreachable) | TC-AUTH-128 |
| NFR-2 (break-glass audit + notification within 60s via Hangfire) | TC-AUTH-127 |
| NFR-3 (admin-consent completes in one session, resumable if interrupted) | TC-AUTH-133 |
| NFR-4 (enforcement evaluation adds negligible overhead; cached tenant setting) | TC-AUTH-126, TC-AUTH-134, TC-AUTH-ISO-006 |
| BR-1 (never enforceable into a no-one-can-log-in state; break-glass mandatory) | TC-AUTH-128, TC-AUTH-129 |
| BR-2 (break-glass restricted to designated admins; not for ordinary users) | TC-AUTH-127, TC-AUTH-130 |
| BR-3 (consent records `tid`; consent alone does NOT enable SSO) | TC-AUTH-131, TC-AUTH-132, TC-AUTH-133 |
| BR-4 (every break-glass login is a security-sensitive audited + notified event) | TC-AUTH-127 |
| BR-5 (reverting to `optional` restores local login for everyone immediately) | TC-AUTH-134 |
| BR-6 (enforcement/onboarding tenant-scoped; one tenant never affects another) | TC-AUTH-ISO-006 |

## FR/NFR/BR Coverage for US-AUTH-010

| Requirement | Covered By Test Cases |
|-------------|-----------------------|
| FR-1 (Track consecutive failed attempts in failed_login_count) | TC-AUTH-026, TC-AUTH-083, TC-AUTH-084, TC-AUTH-088, TC-AUTH-098, TC-AUTH-105, TC-AUTH-111 |
| FR-2 (Set locked_until on reaching max failed attempts) | TC-AUTH-026, TC-AUTH-084, TC-AUTH-086, TC-AUTH-089, TC-AUTH-092, TC-AUTH-105, TC-AUTH-111 |
| FR-3 (Lockout policy configurable per tenant) | TC-AUTH-026, TC-AUTH-083, TC-AUTH-084, TC-AUTH-102, TC-AUTH-103 |
| FR-4 (On success reset failed_login_count to 0 and locked_until to null) | TC-AUTH-028, TC-AUTH-086, TC-AUTH-088 |
| FR-5 (Check locked_until before verifying credentials) | TC-AUTH-027, TC-AUTH-085, TC-AUTH-092, TC-AUTH-097 |
| FR-6 (Tenant admins can unlock accounts) | TC-AUTH-028, TC-AUTH-087, TC-AUTH-093, TC-AUTH-094, TC-AUTH-107, TC-AUTH-112 |
| FR-7 (Lockout and unlock events in tenant + system audit log) | TC-AUTH-026, TC-AUTH-084, TC-AUTH-087, TC-AUTH-094, TC-AUTH-101 |
| FR-8 (Notification email on lockout) | TC-AUTH-026, TC-AUTH-100 |
| FR-9 (Progressive lockout -- duration doubles after repeated cycles) | TC-AUTH-089, TC-AUTH-106 |
| FR-10 (MFA failures count toward lockout threshold) | TC-AUTH-090, TC-AUTH-110 |
| NFR-1 (Lockout check adds <= 2 ms overhead) | TC-AUTH-104 |
| NFR-2 (Atomic failed_login_count increment) | TC-AUTH-098 |
| NFR-3 (Lockout notification within 60 seconds via Hangfire) | TC-AUTH-100 |
| NFR-4 (Timing-attack resistance) | TC-AUTH-027, TC-AUTH-083, TC-AUTH-097 |
| NFR-5 (Lockout state in database, persists across restarts) | TC-AUTH-099 |
| BR-1 (Lockout per global user account, blocks all tenants) | TC-AUTH-092, TC-AUTH-105 |
| BR-2 (Password reset clears lockout) | TC-AUTH-091 |
| BR-3 (Tenant admin can only unlock own-tenant users) | TC-AUTH-093 |
| BR-4 (System admin can unlock any user) | TC-AUTH-094 |
| BR-5 (Policy bounds: maxFailedAttempts 3-10, lockoutDurationMinutes 5-60) | TC-AUTH-102 |
| BR-6 (Social login failures do not increment counter) | TC-AUTH-096 |
| BR-7 (Lockout does not revoke active sessions) | TC-AUTH-027, TC-AUTH-095 |

## FR/NFR/BR Coverage for US-AUTH-009

| Requirement | Covered By Test Cases |
|-------------|-----------------------|
| FR-1 (Session policy configurable per tenant via PUT /api/v1/tenant/auth-settings) | TC-AUTH-024, TC-AUTH-065, TC-AUTH-066, TC-AUTH-074, TC-AUTH-075, TC-AUTH-080, TC-AUTH-082 |
| FR-2 (Refresh checks idle timeout via last_active_at) | TC-AUTH-067, TC-AUTH-074, TC-AUTH-081 |
| FR-3 (Refresh checks absolute timeout via issued_at) | TC-AUTH-068, TC-AUTH-074 |
| FR-4 (last_active_at updated on each authenticated request, debounced) | TC-AUTH-067, TC-AUTH-077, TC-AUTH-081 |
| FR-5 (Concurrent session check at login time, count non-revoked non-expired tokens) | TC-AUTH-024, TC-AUTH-065, TC-AUTH-066, TC-AUTH-074, TC-AUTH-075, TC-AUTH-082 |
| FR-6 (Admin sessions endpoint: GET /api/v1/tenant/users/by-user/{id}/sessions) | TC-AUTH-069, TC-AUTH-073, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077 |
| FR-7 (Self sessions endpoint: GET /api/v1/auth/me/sessions) | TC-AUTH-025, TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077 |
| FR-8 (Session revocation endpoints: admin and self) | TC-AUTH-025, TC-AUTH-070, TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-076 |
| FR-9 (All session management actions recorded in audit log) | TC-AUTH-065, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-070, TC-AUTH-071, TC-AUTH-078 |
| FR-10 (Hangfire background job cleans up expired/revoked tokens) | TC-AUTH-079 |
| NFR-1 (last_active_at tracking adds <= 2 ms overhead) | TC-AUTH-077 |
| NFR-2 (Concurrent session counting performant with index) | TC-AUTH-077 |
| NFR-3 (Session list queries P95 <= 200 ms) | TC-AUTH-077 |
| NFR-4 (Clock drift handled gracefully) | TC-AUTH-074 |
| NFR-5 (Session metadata only visible to owner and admins) | TC-AUTH-069, TC-AUTH-075, TC-AUTH-076 |
| BR-1 (Session policies are per-tenant) | TC-AUTH-065, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-075, TC-AUTH-080 |
| BR-2 (System admin sessions follow system-level policies) | TC-AUTH-082 |
| BR-3 (Impersonation sessions excluded from concurrent count) | TC-AUTH-082 |
| BR-4 (Current session cannot be self-revoked) | TC-AUTH-072 |
| BR-5 (Admin revocation triggers notification to affected user) | TC-AUTH-070 |
| BR-6 (Idle timeout reset by any authenticated API request) | TC-AUTH-067, TC-AUTH-081 |

## FR/NFR/BR Coverage for US-AUTH-008

| Requirement | Covered By Test Cases |
|-------------|-----------------------|
| FR-1 (GET /api/v1/auth/my-tenants returns all memberships) | TC-AUTH-022, TC-AUTH-059, TC-AUTH-064 |
| FR-2 (POST /api/v1/auth/switch-tenant accepts tenantId UUID) | TC-AUTH-022, TC-AUTH-059, TC-AUTH-060 |
| FR-3 (New JWT and refresh token scoped to target tenant) | TC-AUTH-022, TC-AUTH-059, TC-AUTH-061, TC-AUTH-063 |
| FR-4 (Previous tenant refresh token remains valid) | TC-AUTH-022, TC-AUTH-060, TC-AUTH-063 |
| FR-5 (Active membership verified before issuing tokens) | TC-AUTH-023, TC-AUTH-060, TC-AUTH-062 |
| FR-6 (Target tenant lifecycle allows login) | TC-AUTH-022, TC-AUTH-059, TC-AUTH-060, TC-AUTH-063 |
| FR-7 (Tenant switch events audited in source and target logs) | TC-AUTH-022, TC-AUTH-060, TC-AUTH-062, TC-AUTH-063 |
| FR-8 (Frontend redirects to target subdomain URL) | TC-AUTH-022, TC-AUTH-059, TC-AUTH-063 |
| FR-9 (GET /api/v1/auth/me returns profile, current tenant, memberships) | TC-AUTH-059, TC-AUTH-064 |
| NFR-1 (Tenant switch response <= 400 ms P95) | TC-AUTH-064 |
| NFR-2 (my-tenants Redis cache per user with invalidation) | TC-AUTH-064 |
| NFR-3 (Switching exposes no source tenant data) | TC-AUTH-061, TC-AUTH-062, TC-AUTH-064 |
| NFR-4 (Works behind a load balancer) | TC-AUTH-064 |
| BR-1 (Single login can switch tenants without re-authentication) | TC-AUTH-022, TC-AUTH-059, TC-AUTH-063 |
| BR-2 (Roles are per membership) | TC-AUTH-022, TC-AUTH-061 |
| BR-3 (Target tenant requiring MFA triggers enrollment) | TC-AUTH-063 |
| BR-4 (Impersonation sessions cannot switch tenants) | TC-AUTH-062 |
| BR-5 (my-tenants includes all memberships with inaccessible statuses flagged) | TC-AUTH-059, TC-AUTH-060, TC-AUTH-064 |

## FR/NFR/BR Coverage for US-AUTH-007

| Requirement | Covered By Test Cases |
|-------------|-----------------------|
| FR-1 (Middleware before auth and authorization) | TC-AUTH-019, TC-AUTH-051, TC-AUTH-052 |
| FR-2 (Extract subdomain from Host header) | TC-AUTH-019, TC-AUTH-053 |
| FR-3 (Reserved subdomain routing) | TC-AUTH-051, TC-AUTH-052 |
| FR-4 (Admin subdomain system context) | TC-AUTH-052 |
| FR-5 (Redis first, PostgreSQL fallback) | TC-AUTH-019, TC-AUTH-055, TC-AUTH-056, TC-AUTH-057 |
| FR-6 (Populate ITenantContext) | TC-AUTH-019, TC-AUTH-052, TC-AUTH-054, TC-AUTH-055, TC-AUTH-056, TC-AUTH-057 |
| FR-7 (Unknown tenant static 404) | TC-AUTH-020, TC-AUTH-053, TC-AUTH-058 |
| FR-8 (Non-accessible tenant state handling) | TC-AUTH-021, TC-AUTH-058 |
| FR-9 (Cache TTL and invalidation readiness) | TC-AUTH-019, TC-AUTH-055, TC-AUTH-056 |
| FR-10 (Tenant ID in logs after resolution) | TC-AUTH-019, TC-AUTH-054 |
| NFR-1 (Cache hit <= 5 ms) | TC-AUTH-055 |
| NFR-2 (Cache miss <= 50 ms P95) | TC-AUTH-056 |
| NFR-3 (Redis fallback to DB) | TC-AUTH-057 |
| NFR-4 (Subdomain validation) | TC-AUTH-053 |
| NFR-5 (Static 404 no information leakage) | TC-AUTH-020, TC-AUTH-053, TC-AUTH-058 |
| BR-1 (Unique immutable subdomain slug) | TC-AUTH-053 |
| BR-2 (Reserved subdomains cannot be claimed) | TC-AUTH-051, TC-AUTH-053 |
| BR-3 (System tenant special case) | TC-AUTH-052 |
| BR-4 (Resolution required except allowed routes) | TC-AUTH-051, TC-AUTH-052, TC-AUTH-054 |
| BR-5 (Custom domains deferred) | TC-AUTH-053 |

## FR/NFR/BR Coverage for US-AUTH-006

| Requirement | Covered By Test Cases |
|-------------|-----------------------|
| FR-1 (Module.Action.Scope pattern) | TC-AUTH-039, TC-AUTH-043, TC-AUTH-045 |
| FR-2 (Tenant-scoped roles, built-in protection) | TC-AUTH-040, TC-AUTH-041, TC-AUTH-ISO-004 |
| FR-3 (role_permission table) | TC-AUTH-039, TC-AUTH-043, TC-AUTH-047 |
| FR-4 (user_tenant_role many-to-many) | TC-AUTH-039, TC-AUTH-043, TC-AUTH-046, TC-AUTH-047 |
| FR-5 (Three-layer authorization) | TC-AUTH-039, TC-AUTH-045, TC-AUTH-049 |
| FR-6 (CRUD endpoints) | TC-AUTH-039, TC-AUTH-040, TC-AUTH-044, TC-AUTH-046, TC-AUTH-050 |
| FR-7 (Audit logging) | TC-AUTH-050 |
| FR-8 (Tenant Owner protection) | TC-AUTH-042 |
| FR-9 (System roles isolation) | TC-AUTH-041 |
| FR-10 (EF Core filters + RLS) | TC-AUTH-ISO-004 |
| NFR-1 (Permission eval <= 5ms) | TC-AUTH-049 |
| NFR-2 (Redis cache + invalidation) | TC-AUTH-047, TC-AUTH-ISO-004 |
| NFR-3 (50 roles / 200+ permissions) | TC-AUTH-044 |
| NFR-4 (Auth failure logging) | TC-AUTH-050 |
| BR-1 (Per-tenant-membership roles) | TC-AUTH-018, TC-AUTH-ISO-004 |
| BR-2 (Built-in immutable) | TC-AUTH-040 |
| BR-3 (Custom role permission subsets) | TC-AUTH-039 |
| BR-4 (Permission union) | TC-AUTH-043 |
| BR-5 (Effect on next token refresh) | TC-AUTH-046 |
| BR-6 (Tenant Owner minimum one) | TC-AUTH-042 |
| BR-7 (Delete role with users) | TC-AUTH-046 |
