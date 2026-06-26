---
id: TC-REC-ISO-016
user_story: US-REC-007
module: Recruitment
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-REC-ISO-016: Tenant B cannot read or write Tenant A's offers / offer PDFs; offer rows + PDF paths are session-tenant-stamped (AC-5, NFR-2/NFR-3)

## 1. Test Objective
Verify AC-5 / NFR-2 / NFR-3 for REC-007's new surface: the `offer` table and its blob-stored PDFs are tenant-isolated on BOTH reads and writes. A user in Tenant B cannot read Tenant A's offers (salary, status, response, PDF storage key), cannot retrieve/download Tenant A's offer PDF, and cannot generate/send/respond-to/withdraw an offer against a Tenant A applicant. Any `offer` row written carries the SESSION-derived tenant_id (via TenantInterceptor), never a client value, and the PDF is stored under the SESSION tenant's path `{tenantId}/recruitment/...` -- a body-injected `tenant_id` or a crafted storage path cannot place a PDF in another tenant's namespace. This exercises EF Core global query filters on `offer` for the new mutation/read. Generic no/invalid/mismatched tenant-context rejection and the cross-tenant write-block + body-injected-tenant_id contract are reused from TC-REC-ISO-010/011 on the recruitment surface (per the module's ISO-reuse convention).

NOTE: AC-5/NFR-2 specify PostgreSQL RLS; the platform enforces isolation via EF Core global query filters + TenantInterceptor -- if RLS is later added on `offer`, extend Step 6 to assert it at the DB session level. PDF at-rest encryption + signed access is covered in TC-REC-007-14; this case asserts the tenant SCOPING of those PDFs.

## 2. Related Requirements
- User Story: US-REC-007
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-2 (tenant-scoped + RLS), NFR-3 (PDF storage tenant-scoped)
- Functional Requirements: FR-3 (tenant-scoped PDF path), FR-6 (status), FR-9 (versions)
- Reuses: TC-REC-ISO-010 (no/invalid/mismatched tenant context), TC-REC-ISO-011 (cross-tenant write block + body-injected tenant_id) on the recruitment surface.

## 3. Preconditions
- Tenant "acme" (A): applicant {acme_applicantId} on {acme_vacancyId} with a `Sent` offer {acme_offerId} (salary 120000, PDF at acme's tenant-scoped path).
- Tenant "globex" (B): `recruiter@globex` (Recruitment.Offer.All) + a globex applicant {globex_applicantId} in "Offer" stage.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Holds the target offer + PDF |
| Tenant B | globex | Auth context |
| A offer | {acme_offerId} | salary 120000, Sent, has PDF |
| A PDF path | {acmeTenantId}/recruitment/{acme_vacancyId}/{acme_applicantId}/offers/{file} | target |
| Injected tenant_id | acme's id in request body | Must be ignored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `recruiter@globex`, `GET` the offers list / offers for {acme_applicantId} | Zero of acme's offers returned; no salary/status/response/PDF-key from acme exposed (EF global query filter) (AC-5). |
| 2 | As `recruiter@globex`, `GET /offers/{acme_offerId}` directly | 404; acme's offer is not retrievable cross-tenant. |
| 3 | As `recruiter@globex`, attempt to download acme's offer PDF (via the offer endpoint AND via a guessed/crafted storage path) | Denied (404/403); globex cannot retrieve acme's PDF; the tenant-scoped path + signed/authorized access prevents cross-tenant blob access (NFR-3, FR-3). |
| 4 | As `recruiter@globex`, attempt to generate/send/respond/withdraw an offer against {acme_applicantId}/{acme_offerId} | 404/403; the EF filter prevents loading acme's applicant/offer; no offer row or PDF written in acme (reuses TC-REC-ISO-011). |
| 5 | As `recruiter@globex`, with no/invalid/mismatched tenant context, call the offer endpoints | Rejected (no tenant context resolved); no cross-tenant read/write (reuses TC-REC-ISO-010). |
| 6 | As `recruiter@globex`, generate a VALID offer for {globex_applicantId} but inject `tenant_id=acme` in the body AND a path hint pointing at acme's namespace | The body tenant_id + path hint are ignored; the new `offer` row is stamped globex (TenantInterceptor) and the PDF is stored under globex's path `{globexTenantId}/recruitment/...`, never acme's (reuses TC-REC-ISO-011, FR-3). |
| 7 | Verify at the DB + blob level | `SELECT * FROM offer WHERE tenant_id = globex_id` returns only globex rows; acme offers are invisible under globex; no globex PDF lands under acme's path. (If RLS exists, confirm a globex session cannot read acme `offer` rows via direct SQL.) |

## 6. Postconditions
- No cross-tenant offer or PDF data was read or written; all offer rows + PDFs carry/land under the session tenant.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
