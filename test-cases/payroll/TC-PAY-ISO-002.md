---
id: TC-PAY-ISO-002
user_story: US-PAY-001
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-002: Payroll APIs reject requests without a valid tenant context (no/invalid/mismatched tenant)

## 1. Test Objective
Verify AC-6 / FR-8: salary component and structure endpoints require a resolved tenant context. Requests with no tenant context, an unresolvable subdomain, or a token whose `tenant_id` does not match the request tenant are rejected — never silently served against a default or wrong tenant.

## 2. Related Requirements
- User Story: US-PAY-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-8

## 3. Preconditions
- Tenant "acme" exists and has payroll data; tenant "globex" exists.
- A valid acme bearer token is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing tenant | (no subdomain / no header) | unresolved context |
| Unknown subdomain | nosuchtenant.yourhrm.com | unresolvable |
| Mismatch | acme token + globex subdomain | token/tenant mismatch |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /api/v1/payroll/components` with a valid token but no resolvable tenant (no subdomain / no `X-Tenant-Subdomain`) | Rejected (400/401/403 per the platform's tenant-resolution policy); no data returned. Never served against a default tenant. |
| 2 | Call with subdomain `nosuchtenant.yourhrm.com` | Tenant resolution fails; request rejected; no payroll data leaked. |
| 3 | Present an acme token while addressing the globex subdomain (`X-Tenant-Subdomain: globex`) | Rejected due to token/tenant mismatch; never serves globex data to an acme token (or vice versa). |
| 4 | Repeat for a write endpoint (`POST .../components`) with the same three conditions | All rejected before any row is created. |

## 6. Postconditions
- No payroll data is read or written without a valid, matching tenant context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
