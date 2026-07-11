---
id: TC-REC-ISO-008
user_story: US-REC-002
module: Recruitment
priority: high
type: security
status: fail
created: 2026-06-15
---

# TC-REC-ISO-008: Resume blob storage and the duplicate-detection index are tenant-scoped (no cross-tenant collision or leakage)

## 1. Test Objective
Verify AC-5 / NFR-3 / BR-1: the resume blob namespace and the `(tenant_id, email, vacancy_id)` duplicate-detection index are tenant-scoped, so two tenants can hold applicants with the same email applying to like-named vacancies without collision, and one tenant's stored resume is never served under another tenant's context.

## 2. Related Requirements
- User Story: US-REC-002
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-2 (tenant-scoped path)
- Business Rules: BR-1 (per-(tenant,vacancy,email) uniqueness)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" each have an `Open` vacancy.
- The same email `same.person@example.com` will apply in both tenants.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Shared email | same.person@example.com | Applies in both tenants |
| acme vacancy | "Software Engineer" (Open) | Tenant A |
| globex vacancy | "Software Engineer" (Open) | Tenant B (like-named) |
| Resume | resume.pdf (same filename in both) | Must not collide |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit an application as `same.person@example.com` to the acme vacancy (resume.pdf) | Accepted; applicant created in acme with the resume under `{acme_tenantId}/recruitment/...`. |
| 2 | Submit an application as the SAME email to the globex vacancy (resume.pdf) | Accepted -- the duplicate check is tenant-scoped via `(tenant_id, email, vacancy_id)`; the acme application does NOT block the globex one (BR-1 is per-tenant/per-vacancy). |
| 3 | Inspect both resume objects | They live under distinct tenant prefixes (`{acme_tenantId}/...` vs `{globex_tenantId}/...`) with distinct UUID-based keys; identical filenames cause no collision. |
| 4 | Attempt to fetch the acme applicant's resume while resolving as globex (and vice versa) | Denied (404/403) -- a resume from one tenant cannot be retrieved under another tenant's context. |
| 5 | Re-submit the same email to the acme vacancy again | Rejected per BR-1 within acme (already applied) -- confirming per-tenant uniqueness still holds independently of globex. |

## 6. Postconditions
- Resume blobs and duplicate-detection indexes are tenant-scoped; identical emails/filenames across tenants cause no collision or leakage.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
