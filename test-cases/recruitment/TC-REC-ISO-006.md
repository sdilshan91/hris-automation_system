---
id: TC-REC-ISO-006
user_story: US-REC-002
module: Recruitment
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-REC-ISO-006: Applicant API rejects requests without a valid/resolvable tenant context (incl. public submit)

## 1. Test Objective
Verify AC-5 / NFR-3: applicant reads and submissions made without a valid, resolvable tenant context are rejected rather than served against an arbitrary or default tenant. Because the public application form is anonymous, the tenant must be resolved from the subdomain / `X-Tenant-Subdomain` before any applicant write/read proceeds; a missing, unknown, or mismatched tenant context must not create or expose applicants.

## 2. Related Requirements
- User Story: US-REC-002
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-3
- Constraints: tenant resolution precedes data access (TenantResolutionMiddleware); public submit is anonymous (NFR-2)

## 3. Preconditions
- Tenant "acme" exists and is active with an `Open` vacancy.
- A valid acme JWT is available for the authenticated read cases.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing context | no subdomain + no X-Tenant-Subdomain | Should not resolve a tenant |
| Unknown tenant | X-Tenant-Subdomain: doesnotexist | Unresolvable |
| Inactive/reserved | suspended or reserved subdomain | Must not serve/accept data |
| Mismatched (read) | acme JWT + X-Tenant-Subdomain: globex | Token/context mismatch |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Anonymously `POST .../vacancies/{vacancyId}/applications` with no resolvable tenant (no subdomain, no `X-Tenant-Subdomain`) | Rejected (400/404) -- no applicant is created against a default/arbitrary tenant (TenantInterceptor has no tenant to stamp). |
| 2 | Anonymously submit with `X-Tenant-Subdomain: doesnotexist` | Rejected (tenant not resolvable); no applicant created. |
| 3 | Submit against a suspended/reserved subdomain | Rejected; no applicant created for an inactive/reserved tenant. |
| 4 | Authenticated `GET .../applications` with a valid acme JWT but `X-Tenant-Subdomain: globex` (mismatch) | Rejected (403/401) -- the request is not served against globex; no cross-tenant applicant access via header override. |
| 5 | Authenticated applicant read with no resolvable tenant | Rejected; no applicant data returned against a default tenant. |

## 6. Postconditions
- No applicant data is read or written without a valid resolved tenant context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
