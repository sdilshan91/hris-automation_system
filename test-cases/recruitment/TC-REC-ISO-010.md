---
id: TC-REC-ISO-010
user_story: US-REC-003
module: Recruitment
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-010: Pipeline + stage-move APIs reject requests without a valid tenant context

## 1. Test Objective
Verify AC-5 / NFR-3: pipeline read and stage-move (and bulk) endpoints require a resolvable tenant context. Requests with no tenant context, an unknown/unresolvable subdomain, or a tenant mismatch between the resolved subdomain and the authenticated user's tenant are rejected -- never served against a default or arbitrary tenant.

## 2. Related Requirements
- User Story: US-REC-003
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" is active with a recruiter authenticated.
- The TenantResolutionMiddleware runs before auth and populates the scoped ITenantContext from the subdomain (dev fallback: `X-Tenant-Subdomain`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| No tenant header/subdomain | (omitted) | Must be rejected |
| Unknown subdomain | `doesnotexist` | Unresolvable tenant |
| Mismatch | JWT=acme, subdomain=globex | Tenant mismatch |
| Reserved subdomain | `admin` | Not a tenant pipeline context |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /api/v1/recruitment/vacancies/{vacancyId}/pipeline` with NO tenant subdomain/header | Rejected (400/401); the board is NOT served against a default tenant. |
| 2 | Repeat with an unknown subdomain `doesnotexist` | Tenant resolution fails; request rejected; no data returned. |
| 3 | Send a stage move with JWT for acme but `X-Tenant-Subdomain: globex` (mismatch) | Rejected; the request is not honoured under either tenant; no `applicant` change. |
| 4 | Call the pipeline endpoint with a reserved subdomain (`admin`) | Treated as system/non-tenant context; the tenant pipeline is not served. |
| 5 | As a positive control, call with a valid acme context | 200; confirms the rejections are due to missing/invalid tenant context, not a broken endpoint. |

## 6. Postconditions
- All tenant-context-less / mismatched requests were rejected with no data exposure and no writes.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
