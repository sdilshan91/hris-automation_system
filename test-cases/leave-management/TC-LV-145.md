---
id: TC-LV-145
user_story: US-LV-007
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-145: Authorization -- only Holiday.* permission holders manage holidays; employees read-only; unauthenticated rejected

## 1. Test Objective
Verify role-based access on the holiday endpoints: create/edit/deactivate/import require the corresponding `Holiday.*` permissions (granted to Tenant Admin / HR roles); plain employees have only `Holiday.View` (read); a user without any holiday permission and an unauthenticated request are rejected (Preconditions §2, NFR-2).

## 2. Related Requirements
- User Story: US-LV-007
- Preconditions (Section 2): Leave.Configure / Tenant.Admin
- Non-Functional Requirements: NFR-2
- Cross-reference: US-AUTH-006 (RBAC), US-AUTH-007 (tenant resolution)

## 3. Preconditions
- Tenant "acme" with: HR Officer "Priya" (full Holiday.*), employee "Sam" (Holiday.View only), and "Guest" (no holiday permission).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Write endpoints | POST/PUT/deactivate/import | require Holiday.Create/Edit/Deactivate/Import |
| Read endpoints | GET list / by id | require Holiday.View |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Priya (HR) creates, edits, deactivates, and imports holidays | All succeed (authorized). |
| 2 | Sam (Holiday.View) GETs the holiday list | Succeeds (read allowed). |
| 3 | Sam attempts POST/PUT/deactivate/import | 403 Forbidden -- lacks the write permissions. |
| 4 | "Guest" (no holiday permission) hits any holiday endpoint; and an unauthenticated request hits the API | 403 for Guest on protected actions; 401 Unauthorized when no/invalid token is presented. |

## 6. Postconditions
- Holiday management is restricted to permission holders; employees are read-only; anonymous access is rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
