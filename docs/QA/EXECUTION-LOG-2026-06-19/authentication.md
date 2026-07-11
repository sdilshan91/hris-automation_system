# Authentication — execution results (2026-06-19)

Layer key: API = direct HTTP w/ JWT; UI = Playwright-driven app.

| TC | Title | Layer | Verdict | Evidence |
|----|-------|-------|---------|----------|
| TC-AUTH-001 | Successful login, valid creds | API | ✅ PASS | `POST /api/v1/auth/login` (admin@hrm.local, X-Tenant-Subdomain: platform) → 200; body `{success:true,data:{accessToken,...}}` |
| TC-AUTH-001 | Successful login, valid creds | UI | ✅ PASS | Login form → redirect to `/dashboard`, authenticated as "Platform Administrator" (earlier Playwright run) |
| TC-AUTH-002 | Login fails with wrong password | API | ✅ PASS | wrong password → **401** |
| TC-AUTH-003 | Login fails with non-existent user | API | ✅ PASS | nobody@nowhere.com → **401** (no user enumeration; same status as wrong-password) |
| TC-AUTH-004 | Login form validation (empty fields) | API | ✅ PASS | empty email+password → **400** (server validation) |
| TC-AUTH-004 | Login form validation (empty fields) | UI | ⏳ PENDING | client-side form validation not yet driven |
| TC-AUTH-005 | JWT issued on successful login | API | ✅ PASS | JWT decodes: `sub`, `email`, `tenant_id`, `user_tenant_id`, `roles:"SystemAdmin"`, `permissions[]`, `jti`, `exp`; alg RS256, kid `hrm-dev-key-1` |

## Notes / observations
- **`roles` claim is a scalar string** `"SystemAdmin"`, not an array — relevant to the BUG-1 role-string
  matching and to how the FE `hasRole` substring-matches. Worth a dedicated negative TC.
- Wrong-password and unknown-user both return 401 with no distinguishing body → good (anti-enumeration).
- Response envelope is `{success, data}` (ApiResponse<T>) — consistent with the known FE↔BE envelope note.

## Pending in this module (next batches)
- MFA (US-AUTH-005), password reset (US-AUTH-004), RBAC deny matrix (US-AUTH-006), tenant resolution
  (US-AUTH-007), cross-tenant switch (US-AUTH-008), session limits (US-AUTH-009), lockout (US-AUTH-010),
  tenant-isolation (TC-AUTH-ISO-001..004). Mix of API + UI.
