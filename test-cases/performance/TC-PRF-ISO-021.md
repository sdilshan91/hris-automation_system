---
id: TC-PRF-ISO-021
user_story: US-PRF-006
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-021: Meeting notes + sign-offs in Tenant A are invisible from Tenant B (cross-tenant read isolation, incl. by direct id) (NFR-2)

## 1. Test Objective
Verify NFR-2: all sign-off data (review_meeting_notes, review_signoffs, dispute comments, signed review records, read-tracking) is isolated per tenant. A user authenticated in Tenant B cannot list or retrieve any meeting notes, signature, dispute, or signed review record belonging to Tenant A, including by direct id. Exercises the platform tenant-isolation mechanism (EF Core global query filters + TenantInterceptor).

> Note: US-PRF-006 NFR-2 / S7 specify PostgreSQL RLS (`tenant_id = current_setting('app.current_tenant_id')`) on review_meeting_notes / review_signoffs. This platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If RLS is later added on these tables, extend Step 4 to assert isolation at the DB session level as defense-in-depth.

## 2. Related Requirements
- User Story: US-PRF-006
- Non-Functional Requirements: NFR-2
- Data Requirements: S7 (review_meeting_notes, review_signoffs with tenant_id + RLS policy)

## 3. Preconditions
- Tenant "acme" has Liam Carter's review with meeting notes + recorded sign-offs (known reviewId / signoffId / notesId).
- Tenant "globex" has its own HR Officer and its own reviews/sign-offs.
- An HR Officer with `Performance.Review.All` is authenticated in globex (Tenant B).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | has Liam's signed review |
| Tenant B | globex | its own sign-off data |
| Auth context | globex | Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex `tenant_id` | Tenant context resolves to globex. |
| 2 | `GET .../performance/reviews/...` list/meeting-notes/signoff endpoints | Responses contain only globex sign-off data; zero acme records (NFR-2). |
| 3 | `GET .../performance/reviews/{acme_reviewId}/meeting-notes`, `.../signoff/{acme_signoffId}`, `.../reviews/{acme_reviewId}/export` using acme IDs | 404 Not Found -- global query filters exclude them; never 200 with acme data/signatures. |
| 4 | Verify at the DB level | `SELECT * FROM review_signoffs WHERE tenant_id = acme_id` returns only acme rows; a session/context set to globex never reads acme sign-offs or notes. (If RLS exists, confirm a globex-set session cannot read acme rows even via a direct query.) |
| 5 | Switch to acme and repeat | acme sees only its own notes/sign-offs; zero globex records. |

## 6. Postconditions
- No cross-tenant meeting-notes / signature / dispute / signed-review data is exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
