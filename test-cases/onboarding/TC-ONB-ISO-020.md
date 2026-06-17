---
id: TC-ONB-ISO-020
user_story: US-ONB-006
module: Onboarding / Offboarding
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-020: Tenant A cannot see Tenant B exit interviews or analytics (cross-tenant READ block)

## 1. Test Objective
Verify AC-5 and NFR-2: exit interview records, responses, and the aggregated analytics (reason distribution, average ratings, trends) created in Tenant A are never visible to a user in Tenant B. Reads are filtered by the EF Core global query filter on `TenantId`; analytics aggregates include only the caller's tenant's interviews.

## 2. Related Requirements
- User Story: US-ONB-006
- Acceptance Criteria: AC-5
- Non-Functional Requirement: NFR-2 (tenant isolation via EF query filters; RLS deferred)
- Business Rule: BR-4 (analytics show only current-tenant data)
- Cross-cutting: mandatory multi-tenant isolation

## 3. Preconditions
- Tenant A (`acme`) has multiple completed exit interviews with known reasons/ratings.
- Tenant B (`globex`) has its own distinct exit interviews and an authenticated HR Officer.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| tenant A interviews | acme set | tenant_id = T-acme |
| tenant B caller | globex HR Officer | tenant_id = T-globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the globex HR Officer, list exit interviews | Only globex interviews returned; no acme interview/response is visible (AC-5, NFR-2). |
| 2 | As globex, open the Exit Interview Summary analytics | Aggregates reflect only globex's interviews; acme reasons/ratings do not contribute to the pie/bar/trend (BR-4). |
| 3 | As globex, query an individual interview list/detail | No acme interview records appear. |
| 4 | Switch to the acme HR Officer and view analytics | acme aggregates are visible only within the acme tenant context. |

## 6. Postconditions
- Exit interview data and analytics are strictly partitioned by tenant on all read paths.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
