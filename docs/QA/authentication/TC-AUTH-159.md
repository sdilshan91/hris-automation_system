---
id: TC-AUTH-159
user_story: US-AUTH-014
module: Authentication
priority: critical
type: security
status: draft
created: 2026-07-29
---

# TC-AUTH-159: JIT privilege ceiling — `jit_default_role` cannot be set to a privileged admin/owner role

## 1. Test Objective
Verify BR-3 (US-AUTH-012 BR-5 privilege-escalation guard): the SSO just-in-time default role can never be a privileged admin/owner role, so JIT provisioning can never mint an admin. **Where the ceiling actually lives (implementation reality):** it is enforced at **configuration time**, NOT inside `SsoSignInAsync`. Two coordinated guards reject a privileged `jit_default_role` before it can ever reach the sign-in path:
1. `TenantAuthSettingsValidator` — a FluentValidation `Must(role => !PermissionCatalog.BuiltInRoles.PrivilegedForJit.Contains(role))` rule (rejects the same-request case).
2. `AuthService.UpdateSsoSettings*` — an authoritative merged-state guard: `if (PermissionCatalog.BuiltInRoles.PrivilegedForJit.Contains(effJitRole)) return Failure("The default SSO role cannot be a privileged admin or owner role.", 400)`.

`PrivilegedForJit = { TenantOwner, TenantAdmin, "System Admin" }` (case-insensitive). Because a privileged role can never be *persisted* as `jit_default_role`, `SsoSignInAsync` never receives one — the ceiling holds by construction. This TC therefore drives the **settings-update** surface, not the sign-in surface.

## 2. Related Requirements
- User Story: US-AUTH-014
- Acceptance Criteria: AC-4 (the JIT role that AC-4 assigns)
- Business Rules: BR-3 (US-AUTH-012 BR-5)
- Functional Requirements: FR-3, FR-6
- Related config coverage: US-AUTH-012 (per-tenant SSO settings)

## 3. Preconditions
- **Executable via (no live IdP):** two xUnit arms — (a) a `TenantAuthSettingsValidator` unit test asserting each privileged role name fails validation; (b) an `AuthService.UpdateSsoSettings*` integration arm (EF InMemory/Testcontainers) asserting the merged-state guard returns a 400 and does NOT persist the privileged role. Neither needs a Microsoft round-trip. No step here needs a live IdP.
- Tenant `acme` Active with the built-in roles seeded (`TenantOwner`, `TenantAdmin`, `Employee`, …).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Privileged roles (must be rejected) | TenantOwner, TenantAdmin, System Admin | Case-insensitive; `PrivilegedForJit` set |
| Case variant | `tenantadmin` | Proves case-insensitivity |
| Non-existent role | GhostRole | Rejected by the role-exists check (separate guard) |
| Allowed role (control) | Employee | Non-privileged, seeded → accepted |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **Validator arm:** validate a settings request with `jit_enabled=true`, `jit_default_role="TenantAdmin"`. | Validation FAILS with the privileged-role rule violation. |
| 2 | Repeat for `jit_default_role="TenantOwner"` and `"System Admin"`. | Each FAILS. |
| 3 | Repeat with the case variant `"tenantadmin"`. | FAILS — the guard is case-insensitive (`StringComparer.OrdinalIgnoreCase`). |
| 4 | **Merged-state guard arm:** call `UpdateSsoSettings*` on `acme` with `jit_default_role="TenantAdmin"`. | Result `IsFailure`, HTTP 400, message "The default SSO role cannot be a privileged admin or owner role." |
| 5 | Re-read `acme`'s persisted `JitDefaultRole`. | **Unchanged** — the privileged role was never persisted. |
| 6 | **Role-exists guard:** call `UpdateSsoSettings*` with `jit_default_role="GhostRole"` (non-privileged but absent). | Rejected 400 "Role 'GhostRole' does not exist in this tenant." (defense-in-depth on the same field). |
| 7 | **Control:** call `UpdateSsoSettings*` with `jit_default_role="Employee"`. | Succeeds and persists `Employee` — proving the guard blocks only privileged/invalid roles, not all roles. |

## 6. Postconditions
- `acme`'s `jit_default_role` can only ever be a seeded, non-privileged role; a privileged role is impossible to persist, so JIT can never provision an admin/owner.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
