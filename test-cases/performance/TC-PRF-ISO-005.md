---
id: TC-PRF-ISO-005
user_story: US-PRF-002
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-005: Self-assessments in Tenant A are invisible from Tenant B (cross-tenant read isolation) (NFR-2)

## 1. Test Objective
Verify NFR-2 for the new `self_assessment` table: a manager/HR/employee authenticated in Tenant B cannot list or retrieve any self-assessment belonging to Tenant A, including by direct ID. Exercises the platform tenant-isolation mechanism (EF Core global query filters + TenantInterceptor).

> Note: US-PRF-002 NFR-2 specifies PostgreSQL RLS (`tenant_id = current_setting('app.current_tenant_id')`) on the self-assessment table. This platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If RLS is later added, extend Step 4 to assert isolation at the DB session level as defense-in-depth.

## 2. Related Requirements
- User Story: US-PRF-002
- Non-Functional Requirements: NFR-2
- Data Requirements: S7 (self_assessment table with tenant_id + RLS policy)

## 3. Preconditions
- Tenant "acme" has self-assessments (e.g. Asha's, ids known) for cycle FY26-H1.
- Tenant "globex" has its own users and self-assessments.
- A user with self-assessment access is authenticated in globex (Tenant B).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | has Asha's self-assessment |
| Tenant B | globex | has its own |
| Auth context | globex | authenticated in Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex `tenant_id` | Tenant context resolves to globex. |
| 2 | Call any self-assessment list/read endpoint | Responses contain only globex self-assessments; zero acme records (NFR-2). |
| 3 | `GET .../self-assessments/{acme_assessment_id}` using an acme self-assessment UUID | 404 Not Found -- the global query filter excludes it; never 200 with acme's record. |
| 4 | Verify at the DB level | A session/context set to globex never reads acme self_assessment rows. (If RLS exists, confirm a globex session cannot read acme rows even via direct query.) |
| 5 | Switch to acme and repeat | acme sees only its own self-assessments; zero globex records. |

## 6. Postconditions
- No cross-tenant self-assessment data is exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
