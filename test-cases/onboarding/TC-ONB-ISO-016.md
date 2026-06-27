---
id: TC-ONB-ISO-016
user_story: US-ONB-005
module: Onboarding / Offboarding
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-ONB-ISO-016: Tenant A cannot see Tenant B offboarding records (cross-tenant READ block)

## 1. Test Objective
Verify AC-6 and NFR-2: offboarding instances, exit tasks, clearance statuses, and audit entries created in Tenant A are never visible to a user in Tenant B. Reads are filtered by the EF Core global query filter on `TenantId`.

## 2. Related Requirements
- User Story: US-ONB-005
- Acceptance Criteria: AC-6
- Non-Functional Requirements: NFR-2 (tenant isolation via EF query filters; RLS deferred)
- Cross-cutting: mandatory multi-tenant isolation

## 3. Preconditions
- Tenant A (`acme`) has offboarding instance OB-A300 (employee E300) with clearance tasks and audit entries.
- Tenant B (`globex`) has an HR Officer authenticated in its own tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| tenant A instance | OB-A300 | tenant_id = T-acme |
| tenant B caller | globex HR Officer | tenant_id = T-globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the globex HR Officer, list offboarding instances | Only globex instances returned; OB-A300 and all acme offboarding data are absent (AC-6, NFR-2). |
| 2 | As globex, query the clearance dashboard / exit-task list | No acme tasks or clearance statuses appear. |
| 3 | As globex, query the audit log | No acme offboarding audit entries appear. |
| 4 | Switch to the acme HR Officer and list instances | OB-A300 is visible only within the acme tenant context. |

## 6. Postconditions
- Offboarding data is strictly partitioned by tenant on all read paths.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
