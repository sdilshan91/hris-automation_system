---
id: TC-REC-ISO-017
user_story: US-REC-008
module: Recruitment
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-017: Candidate portal is tenant-bound -- a Tenant A magic link is denied on Tenant B's subdomain; the portal only resolves the tenant from the subdomain and never crosses tenants (AC-4, BR-4, NFR-3)

## 1. Test Objective
Verify AC-4 / BR-4 / NFR-3 for REC-008's new public surface: the candidate portal is strictly tenant-scoped. The magic-link token embeds a tenant_id (NFR-4) and the portal also resolves tenant context from the SUBDOMAIN; the two must agree, and ALL data reads/writes are filtered to that tenant via EF Core global query filters + TenantInterceptor. Concretely: (a) a Tenant A (acme) token presented on Tenant B's (globex) subdomain is DENIED -- the subdomain-resolved tenant does not match the token's tenant, so no acme data is served and no globex data leaks; (b) the portal shows only applications/interviews/offers/timeline within the subdomain-resolved tenant; (c) link regeneration on globex's subdomain for an email that only has an acme application returns the neutral response and generates NO globex token; (d) any portal-side write (offer accept/decline) is stamped with the subdomain/token tenant and cannot touch another tenant's offer; (e) `applicant_portal_token` rows are tenant-scoped. Generic no/invalid/mismatched tenant-context rejection and the cross-tenant write-block + body-injected-tenant_id contract are reused from TC-REC-ISO-010/011 on the recruitment surface.

NOTE: AC-4/NFR-3 describe tenant scoping by subdomain; the platform enforces isolation via EF Core global query filters + TenantInterceptor (not Postgres RLS). If RLS is later added on `applicant_portal_token`/`applicant`/`offer`, extend Step 6 to assert it at the DB session level. Token integrity/forgery (including a tampered tenant_id payload) is covered in TC-REC-008-07; this case asserts tenant BINDING + isolation.

## 2. Related Requirements
- User Story: US-REC-008
- Acceptance Criteria: AC-4 (portal only shows applications within the subdomain-resolved tenant; no cross-tenant data)
- Non-Functional Requirements: NFR-3 (data tenant-scoped; tenant from subdomain), NFR-4 (token embeds tenant_id)
- Business Rules: BR-4 (accessible only for applications within the subdomain tenant), BR-5 (regeneration verifies email matches an application in that tenant)
- Reuses: TC-REC-ISO-010 (no/invalid/mismatched tenant context), TC-REC-ISO-011 (cross-tenant write block + body-injected tenant_id) on the recruitment surface.

## 3. Preconditions
- Tenant "acme" (A): applicant {acme_applicantId} (applicant@example.com) with an application, an active `Sent` offer {acme_offerId}, and a valid acme magic-link token {acmeToken}.
- Tenant "globex" (B): subdomain globex.yourhrm.com active; a globex applicant {globex_applicantId} with their own offer {globex_offerId}.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | holds the target applicant/offer/token |
| Tenant B | globex | the subdomain the token is misused on |
| A token | {acmeToken} | embeds tenant_id=acme |
| A offer | {acme_offerId} | Sent (target) |
| Shared email | applicant@example.com | has only an acme application |
| Injected tenant_id | acme's id in an accept-offer body on globex | must be ignored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Present the acme token {acmeToken} on globex's subdomain: `https://globex.yourhrm.com/portal?token={acmeToken}` | DENIED; the subdomain-resolved tenant (globex) does not match the token's embedded tenant (acme); NO acme application/interview/offer/timeline is served and no globex data leaks (AC-4, BR-4). |
| 2 | Present {acmeToken} on acme's own subdomain | Granted (sanity baseline) -- confirms the denial in Step 1 is due to the tenant mismatch, not a broken token. |
| 3 | On globex's subdomain, request a new link for applicant@example.com (who has only an acme application) | Neutral "if an application exists, a link has been sent" response; NO globex `applicant_portal_token` is created and NO link is emailed (no cross-tenant existence leak) (BR-5, AC-4). |
| 4 | As a globex portal session (valid globex token), attempt to accept/decline acme's offer {acme_offerId} (direct id), or any acme record | 404/403; EF global query filter prevents loading acme's offer/applicant; no acme write occurs (reuses TC-REC-ISO-011). |
| 5 | With no/invalid/mismatched tenant context (e.g. reserved/admin subdomain, missing subdomain), call the portal endpoints | Rejected; no tenant resolved -> no cross-tenant read/write (reuses TC-REC-ISO-010). |
| 6 | As a valid globex portal session, accept globex's own offer {globex_offerId} but inject `tenant_id=acme` in the body | The body tenant_id is ignored; the response/write is stamped globex (TenantInterceptor); acme's offer is untouched (reuses TC-REC-ISO-011, NFR-3). |
| 7 | Verify at the DB level | `applicant_portal_token`, `applicant`, and `offer` reads under a globex context return only globex rows; acme's rows are invisible. (If RLS is later added, confirm a globex session cannot read acme rows via direct SQL.) |

## 6. Postconditions
- No cross-tenant portal data was read or written; the acme token is unusable on globex; tokens/applicants/offers stay tenant-scoped.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
