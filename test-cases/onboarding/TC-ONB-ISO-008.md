---
id: TC-ONB-ISO-008
user_story: US-ONB-003
module: Onboarding / Offboarding
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-008: Tenant A cannot see Tenant B's onboarding tasks/progress (cross-tenant READ block)

## 1. Test Objective
Verify NFR-2: an employee authenticated in Tenant A cannot read Tenant B's task instances, checklist progress, completion records, or uploaded documents. The EF Core global query filter excludes all of Tenant B's onboarding data from Tenant A's context.

## 2. Related Requirements
- User Story: US-ONB-003
- Acceptance Criteria: AC-1, AC-2
- Non-Functional Requirements: NFR-2
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: Isolation is enforced by EF Core global query filters (read) + `TenantInterceptor` (write stamping). The story's RLS expectation is deferred (see TC-ONB-ISO-010). Tests assert the EF mechanism in force today.

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) each have employees with assigned checklists, completed tasks, and uploaded documents.
- An employee authenticated in tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | active context |
| Tenant B | globex | control data |
| entities | task instances, checklist progress, attachments | tenant-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the acme employee, GET the checklist / dashboard | Only acme task instances and progress returned; no globex rows (NFR-2). |
| 2 | Query task/progress endpoints | Globex completion records and overdue tasks are absent from every acme response. |
| 3 | Attempt to fetch a globex document by its storage path/reference from the acme context | Denied/not found; the `{tenantId}/...` path scoping plus query filter prevents cross-tenant file access. |
| 4 | Cross-check counts | Aggregate progress for acme excludes any globex task entirely. |

## 6. Postconditions
- No Tenant B onboarding data (tasks, progress, documents) is ever visible from Tenant A.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
