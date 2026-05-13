---
id: TC-AUTH-021
user_story: US-AUTH-007
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-021: Suspended tenant returns 403

## 1. Test Objective
Verify that when a user navigates to a subdomain belonging to a suspended tenant, the system returns a 403 Forbidden response with a suspension notice page, and login is blocked.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-5
- User Story: US-AUTH-001
- Acceptance Criteria: AC-4
- Functional Requirements: FR-8

## 3. Preconditions
- Tenant "suspcorp" exists with status `suspended` and subdomain `suspcorp.yourhrm.com`.
- A user `john@suspcorp.com` has valid credentials and an active membership in this tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | suspcorp.yourhrm.com | Suspended tenant |
| Tenant Status | suspended | Non-accessible state |
| User | john@suspcorp.com | Valid credentials |
| Password | S3cure!Pass2026 | Correct password |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `https://suspcorp.yourhrm.com/` | Tenant Resolution Middleware resolves the tenant with status `suspended`. |
| 2 | Verify the ITenantContext is populated with `Status = Suspended` | Tenant context reflects suspended state. |
| 3 | Verify a suspension notice page is rendered | Page displays the tenant name, suspension reason (if available), and a "Contact support" link. |
| 4 | Verify no login form is displayed | The standard login page is replaced by the suspension notice. |
| 5 | Send `POST /api/v1/auth/login` with valid credentials to `suspcorp.yourhrm.com` | HTTP 403 Forbidden with "This workspace is currently unavailable." |
| 6 | Verify no JWT access token or refresh token is issued | No tokens in the response. |
| 7 | Verify an existing refresh token for this tenant cannot be used | `POST /api/v1/auth/refresh` returns HTTP 403 Forbidden (per US-AUTH-002 AC-5). |
| 8 | Test that a tenant admin may have limited read-only access for `terminating` state tenants | For `terminating` status, tenant admin can view suspension details but not perform operations. |

## 6. Postconditions
- No authentication tokens are issued for the suspended tenant.
- The user sees a suspension notice page.
- Existing sessions cannot be refreshed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
