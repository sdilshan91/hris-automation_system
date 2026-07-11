---
id: TC-PRF-ISO-041
user_story: US-PRF-002
module: Performance Management
priority: critical
type: security
status: automated
created: 2026-07-05
automated_by: "HRM.Tests/Integration/SelfAssessmentAttachmentIntegrationTests.cs"
---

# TC-PRF-ISO-041: Self-assessment evidence attachments — cross-tenant + cross-employee isolation (ISSUE-105 / NFR-2)

## 1. Test Objective
Verify US-PRF-002 NFR-2 (ISSUE-105): self-assessment evidence attachments never leak across tenants OR across employees within a tenant. Tenant B cannot list or download Tenant A's attachment; a different employee in the SAME tenant as the owner cannot download it. Non-existence is surfaced as 404/empty (never 403 disclosure).

## 2. Related Requirements
- User Story: US-PRF-002
- Acceptance Criteria: AC-4
- Non-Functional Requirement: NFR-2 (own self-assessment + tenant scope)
- Finding: ISSUE-105

## 3. Preconditions
- Tenant A owner employee has one uploaded attachment on a goal. Tenant B has its own tenant + employee. A second employee exists in Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A owner | employee linked to owner user | uploads the attachment |
| Tenant A other | second employee, same tenant | non-owner |
| Tenant B user | employee in Tenant B | cross-tenant arm |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Tenant B lists Tenant A's goal attachments | Success with an EMPTY list (cycle/goal/item filtered out by the tenant read filter). |
| 2 | Tenant B downloads Tenant A's attachment id | 404 `attachment_not_found`; storage never opened. |
| 3 | A different Tenant-A employee downloads the owner's attachment id | 404 (ownership check on assessment.EmployeeId). |
| 4 | The owner downloads their own attachment id | 200 with the correct bytes. |

## 6. Postconditions
- No cross-tenant or cross-employee evidence disclosure; only the owner reads their own bytes.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
