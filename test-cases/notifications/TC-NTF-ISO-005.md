---
id: TC-NTF-ISO-005
user_story: US-NTF-002
module: Notifications & Audit
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-NTF-ISO-005: Tenant A's custom template is invisible and unusable to Tenant B

## 1. Test Objective
Verify that a custom template created by Tenant A is never visible in Tenant B's template list and is
never used when rendering Tenant B's emails. Tenant B continues to see/use the system default.

## 2. Related Requirements
- User Story: US-NTF-002
- Acceptance Criteria: AC-5 (Tenant A customization invisible to Tenant B; sees system default)
- Non-Functional: NFR-2 (tenant isolation via EF Core global query filters; Postgres RLS deferred)
- Business Rules: BR-1 (overrides take precedence within the owning tenant only)

## 3. Preconditions
- Tenant A has a CUSTOM "Leave Approved" override; Tenant B has NO override for `leave_approved`.
- Tenant Admin A (`adminA`) and Tenant Admin B (`adminB`) are each authenticated in their own tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A override subject | "Acme Corp: leave approved" | custom, tenant_id=Tenant A |
| Tenant B override | none | uses default |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `adminB`, open the template list and the "Leave Approved" template | Status shows "Default"; the content is the system default, NOT Tenant A's custom content |
| 2 | As `adminB`, list templates via the API | Response contains only Tenant B's templates/defaults; no Tenant A override row |
| 3 | Trigger a leave approval for a Tenant B employee | The email is rendered from the system default, NOT Tenant A's custom subject ("Acme Corp: leave approved") |
| 4 | As `adminB`, attempt to fetch Tenant A's override by its template_id (IDOR) | 404 Not Found in Tenant B scope (existence not disclosed) — NOT 403 |
| 5 | Inspect persisted rows | Tenant A's override row has tenant_id = Tenant A only; no copy exists under Tenant B |

## 6. Postconditions
- No cross-tenant template visibility or usage; Tenant B isolation intact.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
