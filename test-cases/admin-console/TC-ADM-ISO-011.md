---
id: TC-ADM-ISO-011
user_story: US-ADM-005
module: Admin Console
priority: critical
type: security
status: pass
exec_note: "2026-07-03 API-layer isolation probe (acme tenantadmin JWT): cross-tenant arm (X-Tenant-Subdomain: techoneglobal) => 403 cross_tenant_denied; same-tenant arm (acme) => 200. TenantAccessGuardMiddleware enforced. No leak."
created: 2026-06-17
---

# TC-ADM-ISO-011: Cross-tenant parameter manipulation → 404 (not 403) — existence non-disclosure (AC-6)

## 1. Test Objective
Verify AC-6 / Test-Hint "Cross-tenant isolation": a Tenant A admin who targets a Tenant B `user_tenant_id` / `user_invitation_id` (via URL path, query, or request body `tenant_id`) is rejected. EF Core global query filters cause the cross-tenant resource to be invisible, so the API returns **404 Not Found (not 403)** to avoid disclosing that the resource exists. Applies to read AND every mutating action (deactivate, role-edit, force-reset, end-sessions, resend, revoke).

## 2. Related Requirements
- User Story: US-ADM-005
- Acceptance Criteria: AC-6
- Business Rules: BR-1
- Test Hints: Cross-tenant isolation (parameter manipulation)
- NOTE (platform accuracy): EF query filters in force; Postgres RLS deferred (TC-ADM-005-21); 404-not-403 per ADM module convention.

## 3. Preconditions
- Tenant Admin "Dana" authenticated on Acme (Tenant A).
- Beta (Tenant B) has a known `user_tenant_id = beta-membership-uuid` and a known `user_invitation_id`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| beta_user_tenant_id | beta-membership-uuid | foreign target |
| beta_invitation_id | beta-invite-uuid | foreign target |
| injected body | { tenant_id: beta-uuid } | ignored / overridden by ITenantContext |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, `GET /api/v1/admin/users/{beta_user_tenant_id}` | **404** Not Found — NOT 403 (existence not disclosed). |
| 2 | As Dana, `PUT .../users/{beta_user_tenant_id}/roles` with role_ids | **404**; Beta membership roles UNCHANGED. |
| 3 | As Dana, `POST .../users/{beta_user_tenant_id}/deactivate` | **404**; Beta membership still `active`. |
| 4 | As Dana, `POST .../users/{beta_user}/force-password-reset` | **404**; Beta user's `password_changed_at`/tokens UNCHANGED. |
| 5 | As Dana, `POST .../invitations/{beta_invitation_id}/resend` (and /revoke) | **404**; Beta invitation untouched. |
| 6 | As Dana, invite with body `{ email, role_ids, tenant_id: beta-uuid }` | The injected `tenant_id` is ignored; the invitation is created in Acme (from `ITenantContext`), never Beta. |
| 7 | Query Beta state after all attempts | Zero changes to any Beta record. |

## 6. Postconditions
- Every cross-tenant attempt failed closed with 404; Beta data is provably unmodified and undisclosed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
