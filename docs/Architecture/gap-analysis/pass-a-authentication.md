# Pass A3 — authentication requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains` @ `923db177`
> **Depth:** 8 Must-Have stories at AC level (51 ACs) + 8 Should-Have at story level = **59 rows**
> **Status:** ✅ VALIDATED — 4 of 4 orchestrator spot-checks confirmed, including the critical C-1 finding.
> **Headline:** 🔴 **the SSO tenant-isolation allow-list is appsettings-backed, not DB-backed. The BR-5 production gate that `STATUS.md:40` declares satisfied is NOT satisfied.**

## Orchestrator validation

| Claim | Result |
|---|---|
| `CheckIsolation` reads appsettings, not the tenant record | ✅ **Confirmed.** `EntraSsoService.cs:356-386` — `_options.TenantAllowList.TryGetValue(subdomain, …)`, then `allow.AllowedTenantIds` / `allow.AllowedDomains` / `allow.JitProvisioning` / `allow.DefaultRole`. All four from config. |
| The DB fields have zero read sites on the login path | ✅ **Confirmed** by exhaustive grep. `AllowedEntraTenantIds`/`AllowedEmailDomains`/`JitEnabled`/`JitDefaultRole` appear only in: DTOs, the validator, the snapshot mapper (`AuthService.cs:1948-1951, 2018-2021`), the settings-write path (`:2189-2306`), the audit snapshot (`:2390-2393`), and admin-consent capture (`:2478-2507`). **Not once in `EntraSsoService` or on any login path.** |
| The source admits it | ✅ **Confirmed.** `EntraSsoOptions.cs:8-12`: *"The per-tenant allow-list is the **dev-POC home** for the security-critical tenant-isolation config (US-AUTH-013). **In the full feature this moves into per-tenant DB config (US-AUTH-012)**."* |
| `sso_isolation_rejected` / `sso_misconfigured` audit events missing | ✅ **Confirmed.** 0 hits each, repo-wide. |
| `RoleDto.Id` vs FE `roleId` | ✅ **Confirmed.** `RoleDto.cs:8` `public Guid Id`; `role.models.ts:3` `roleId: string`. |

**Two corrections the auditor made to the orchestrator's brief:**
1. The brief's "5 SSO TCs blocked on *not implemented*" was itself stale — `TEST-STATUS.md:226` reads `[b] blocked | 0`, and `:247-248` mark the prior reasons STALE after a 2026-07-29 re-run. The only non-green auth row is `US-AUTH-014` at `[ ]` (7 TCs authored, not executed).
2. The brief framed the SSO epic as possibly complete. It is complete at the **protocol** layer and the **enforcement** layer, and hollow at the **authorization** layer in between.

---

## VERDICT TABLE

| Req ID | Requirement (short) | MoSCoW | Verdict | Evidence (file:line) | Note |
|---|---|---|---|---|---|
| AUTH-001 AC-1 | JWT w/ tenant_id/roles/permissions + httpOnly refresh cookie | Must | IMPLEMENTED | `JwtService.cs:83-92`; `AuthController.cs:581-593` | SameSite=Strict, Secure, 7d |
| AUTH-001 AC-2 | 401 generic + increment failed count, no enumeration | Must | IMPLEMENTED | `AuthService.cs:153` (dummy BCrypt), `:223`, `:285` | timing-resistant |
| AUTH-001 AC-3 | 403 when no active membership | Must | IMPLEMENTED | `AuthService.cs:321-325` | |
| AUTH-001 AC-4 | 403 on suspended/terminated tenant | Must | IMPLEMENTED | `AuthService.cs:305-309`, `:329-339` | Owner/Admin exception is FR-8-sanctioned |
| AUTH-001 AC-5 | MFA challenge: 200 + payload, no tokens | Must | IMPLEMENTED | `AuthService.cs:368-379`; `AuthController.cs:65-68` | |
| AUTH-001 AC-6 | Unprovisioned subdomain → 404, no SPA shell | Must | PARTIAL | `TenantResolutionMiddleware.cs:121-125,320-325` | API 404 proven; "no SPA shell" served outside .NET — unproven statically |
| AUTH-002 AC-1 | RS256 ~15min access + 7d httpOnly refresh | Must | IMPLEMENTED | `JwtService.cs:94,101`; `AuthController.cs:588` | |
| AUTH-002 AC-2 | Refresh validates hash, rotates, revokes old | Must | IMPLEMENTED | `AuthService.cs:456-462,583-612` | `ReplacedByTokenId` linked `:609` |
| AUTH-002 AC-3 | Reuse → revoke chain + 401 + **user notified** | Must | PARTIAL | `AuthService.cs:480-497` | leg1: revocation+audit exist; **no user notification** |
| AUTH-002 AC-4 | Expired refresh → 401 | Must | IMPLEMENTED | `AuthService.cs:501-504` | |
| AUTH-002 AC-5 | Suspended/terminated tenant on refresh → 403 | Must | IMPLEMENTED | `AuthService.cs:511-515` | |
| AUTH-002 AC-6 | Disabled membership → 403 + revoke remaining | Must | IMPLEMENTED | `AuthService.cs:527-533` | |
| AUTH-002 AC-7 | Signing-key rotation with overlap | Must | IMPLEMENTED | `JwtKeyRingOptions.cs:22-40`; `JwtService.cs:196,221` | documented 3-step rotation |
| AUTH-003 AC-1 | Logout revokes token, clears cookie, 200 | Must | IMPLEMENTED | `AuthController.cs:173-186`; `AuthService.cs:632-638` | |
| AUTH-003 AC-2 | Access token valid till exp; refresh 401 | Must | IMPLEMENTED | `AuthService.cs:480` | by construction |
| AUTH-003 AC-3 | Admin revokes all sessions for a user | Must | IMPLEMENTED | `TenantUsersController.cs:337` | route drift vs doc |
| AUTH-003 AC-4 | Other tenants' sessions unaffected | Must | IMPLEMENTED | `AuthController.cs:404-407` | tenant-scoped revoke |
| AUTH-003 AC-5 | FE clears state even if logout API fails | Must | IMPLEMENTED | `core/auth/auth.service.ts` logout() `catchError → clearSession` | |
| AUTH-004 AC-1 | forgot-password always 200 (no enumeration) | Must | IMPLEMENTED | `AuthController.cs:196-202` | |
| AUTH-004 AC-2 | One-time token stored securely + emailed link | Must | IMPLEMENTED | `AuthService` `PasswordResetTokenHash`/`ExpiresAt` | drift: custom `HashResetToken` not Identity's provider — functionally equivalent |
| AUTH-004 AC-3 | Reset: hash, changed_at, revoke **all tenants**, reset counters | Must | IMPLEMENTED | `AuthService.cs:1110`; `ChangeUserPasswordAsync` + `CrossTenantScope.Enter()` | all 5 effects present |
| AUTH-004 AC-4 | Expired/used token → 400 | Must | IMPLEMENTED | `AuthService.cs:814-819` | single generic message (no oracle) |
| AUTH-004 AC-5 | Tenant password-policy violation → 400 | Must | IMPLEMENTED | `ChangeUserPasswordAsync` policy block | tenant-configurable |
| AUTH-004 AC-6 | Force-reset: revoke + **mark for change + forced redirect** | Must | **PARTIAL** | `TenantUsersController.cs:183`; `UserManagementService.cs:616` | leg1/2: `PasswordChangedAt=null` **written and never read**; no `MustChangePassword` flag exists |
| AUTH-006 AC-1 | Roles list: built-ins + custom, permission & user counts | Must | **PARTIAL** | `RoleDto.cs:8` `Id` vs `role.models.ts:3` `roleId`; `role-list.component.ts:98,166,323,348` | leg2: **FE/BE shape break** — `roleId` always `undefined`; nav/delete hit `/roles/undefined`. Also `Admin.Roles.Manage` absent from `PermissionCatalog.cs:318-320` |
| AUTH-006 AC-2 | POST /tenant/roles creates tenant-scoped role | Must | IMPLEMENTED | `RolesController.cs:17,69`; `RoleService.cs:135-160` | |
| AUTH-006 AC-3 | PATCH /tenant/users/{id} roleIds[] | Must | IMPLEMENTED | `TenantUsersController.cs:23,44-45`; `RoleService.cs:313-324` | |
| AUTH-006 AC-4 | Permission-based 403 | Must | IMPLEMENTED | `PermissionAuthorizationHandler.cs:21-49`; `PermissionPolicyProvider.cs:79-92`; 70 controllers | |
| AUTH-006 AC-5 | Resource-level manager→direct-report authz | Must | **PARTIAL** | `TeamScopeAuthorizationHandler.cs:19-68`; only non-test ref is DI `DependencyInjection.cs:969` | leg2: handler **orphaned**; scope hand-rolled at `LeaveRequestService.cs:1487-1491`, which lacks the `.All` bypass |
| AUTH-006 AC-6 | DELETE built-in role → 400 | Must | IMPLEMENTED | `RoleService.cs:234-236`; `RolesController.cs:121-122` | exact AC string |
| AUTH-006 AC-7 | Roles never leak across tenants | Must | **PARTIAL** | `AppDbContext.cs:272-273` (Role ok); **no filter** for `RolePermission`/`UserTenantRole` at `:268-276` | tenant-isolation risk — join entities unfiltered |
| AUTH-007 AC-1 | Resolve subdomain → cache→DB → populate ITenantContext | Must | IMPLEMENTED | `TenantResolutionMiddleware.cs:73,119,148-159,230-318` | |
| AUTH-007 AC-2 | Unknown subdomain → 404 static page | Must | IMPLEMENTED | `TenantResolutionMiddleware.cs:121-125,320-325` | |
| AUTH-007 AC-3 | Reserved subdomains skip resolution | Must | IMPLEMENTED | `TenantResolutionMiddleware.cs:51-54,106-110` | full FR-3 list |
| AUTH-007 AC-4 | admin.* → IsSystemContext | Must | IMPLEMENTED | `TenantResolutionMiddleware.cs:97-103` | |
| AUTH-007 AC-5 | Suspended → context set, login blocked | Must | IMPLEMENTED | `TenantResolutionMiddleware.cs:149-156`; `AuthService.cs:329-339`; `TenantStatusEnforcementMiddleware.cs` | |
| AUTH-007 AC-6 | Cache miss → DB fallback, repopulate w/ TTL | Must | IMPLEMENTED | `TenantResolutionMiddleware.cs:232-253,295-315` | "≤50 ms" clause not statically verifiable |
| AUTH-010 AC-1 | Below threshold: increment, generic 401 | Must | IMPLEMENTED | `AuthService.cs:223,285` | |
| AUTH-010 AC-2 | At threshold: locked_until + msg + `account_locked` audit | Must | **PARTIAL** | BE `AuthService.cs:233-253,279-283` ✓ / FE `login.component.ts:215` reads `body.code==='account_locked'` | leg2: `AuthController.cs:59` uses `ApiResponse.Fail(error)` (no code); `lockoutMinutesRemaining` has **zero** backend hits — banner unreachable |
| AUTH-010 AC-3 | Locked + correct password still 401 | Must | IMPLEMENTED | `AuthService.cs:159-170` | password not even checked |
| AUTH-010 AC-4 | Lockout expiry clears state | Must | IMPLEMENTED | `AuthService.cs:172-177` | |
| AUTH-010 AC-5 | Admin manual unlock | Must | IMPLEMENTED | `TenantUsersController.cs:282`; `UnlockUserCommandHandler.cs` | route drift |
| AUTH-010 AC-6 | Success resets failed_login_count | Must | IMPLEMENTED | `AuthService.cs:2971-2972` | |
| **AUTH-013 AC-1** | tid ∈ **resolved tenant's** allow-list → proceed | Must | 🔴 **CONTRADICTED** | `EntraSsoService.cs:356-386` reads `_options.TenantAllowList` (appsettings), **not** `Tenant.AllowedEntraTenantIds` | see C-1 |
| **AUTH-013 AC-2** | Verified email domain as alternative | Must | 🔴 **CONTRADICTED** | `EntraSsoService.cs:369-370` (appsettings); `:473-487` `GetEmail` has no verified check | wrong list **and** unverified email accepted |
| AUTH-013 AC-3 | Reject + no JWT/JIT + `sso_isolation_rejected` audit | Must | **PARTIAL** | reject `EntraSsoService.cs:219-222` ✓; `sso_isolation_rejected` → **0 hits** | leg1: audit event never written (Serilog warning only, `:360-363`) |
| AUTH-013 AC-4 | Org B cannot enter tenant A | Must | **PARTIAL** | `EntraSsoService.cs:372-380` | mechanism holds, but evaluated against config, not the tenant record |
| AUTH-013 AC-5 | Empty allow-list fails closed + `sso_misconfigured` audit | Must | **PARTIAL** | fail-closed `:358-365` ✓; `sso_misconfigured` → **0 hits** | leg1: audit event missing |
| AUTH-013 AC-6 | Custom per-tid issuer validator | Must | IMPLEMENTED | `EntraSsoService.cs:437-453` | throws on mismatch |
| **AUTH-013 AC-7** | Unverified email NOT usable for domain matching | Must | 🔴 **MISSING** | `EntraSsoService.cs:473-487` — reads `email`, falls back to `preferred_username`; no `email_verified`/`xms_edov` anywhere | FR-5 unimplemented |
| AUTH-013 AC-8 | Resolved tenant from state, never token `tid` | Must | IMPLEMENTED | `EntraSsoService.cs:218,226`; `AuthService.cs:2784-2786` | correctly done |
| US-AUTH-005 | MFA (TOTP) per-tenant policy, enroll/verify/challenge | Should | IMPLEMENTED | `AuthController.cs:419-515`; `TotpService.cs`; `MfaSecretProtector.cs:35`; `DependencyInjection.cs:184-185` | |
| US-AUTH-008 | Cross-tenant switch without re-auth | Should | IMPLEMENTED | `AuthController.cs:353-392`; `SwitchTenantCommandHandler.cs`; impersonation guard `:361-366` | |
| US-AUTH-009 | Session policy + concurrent limits + list/revoke | Should | IMPLEMENTED | `AuthController.cs:523-575`; idle/absolute `AuthService.cs:541-580`; concurrent `:2991` | |
| US-AUTH-011 | Entra OIDC foundation (challenge→callback→JWT) | Should | IMPLEMENTED | `SsoController.cs:44-92`; `EntraSsoService.cs:59-251` (PKCE `:495`, nonce `:198`, state `:457`) | genuinely complete |
| **US-AUTH-012** | Per-tenant SSO configuration | Should | 🔴 **CONTRADICTED** | CRUD real (`AuthService.cs:2188-2321`, validators, `sso_not_entitled:2206`, FE page); written fields **never read at login** | config is decorative for authorization |
| **US-AUTH-014** | User matching, linking & JIT provisioning | Should | 🔴 **CONTRADICTED** | matching/linking/JIT real `AuthService.cs:2803-2931`; JIT gate from `EntraSsoService.cs:384-385` (appsettings), not `Tenant.JitEnabled` | `STATUS.md:44` claims the opposite |
| US-AUTH-015 | "Sign in with Microsoft" only when SSO enabled | Should | **PARTIAL** | button `login.component.html:237` gated only on `!ssoOnly()`; `tenant.service.ts:23` omits `ssoEnabled` that BE emits at `TenantContextController.cs:69` | leg2: FE model missing the field that drives the AC |
| US-AUTH-016 | SSO enforcement, break-glass, admin-consent | Should | IMPLEMENTED | `AuthService.cs:2036-2060` (**DB-backed** `EnforcementMode`), `:1842-1850`; `AuthController.cs:89`; `EntraSsoService.cs:255-334` | the one SSO story fully wired to the DB |

---

## CONTRADICTIONS

### 🔴 C-1 — The SSO tenant-isolation allow-list is appsettings-backed, not DB-backed

**Ledger claims** — `docs/BA/STATUS.md`, verbatim:
- `:43` — *"US-AUTH-013 … **DB-backed form delivered by 012 (#444)** — allow-list now reads `TenantAuthSettings`, not appsettings"*
- `:44` — *"US-AUTH-014 … JIT now gated by the per-tenant `jit_enabled`/`jit_default_role` from 012"*
- `:40` — *"**EPIC COMPLETE (2026-07-28)** … the BR-5 prod gate is satisfied (DB-backed per-tenant isolation shipped)."*

**Code evidence — the opposite is true** (orchestrator-verified):
- `EntraSsoService.cs:358` — `if (!_options.TenantAllowList.TryGetValue(subdomain, out var allow) …)`. `_options` is `EntraSsoOptions`, bound from the `Authentication:Entra` **configuration section**.
- `:368-370` — `tidAllowed`/`domainAllowed` computed from `allow.AllowedTenantIds` / `allow.AllowedDomains` (config lists).
- `:384-385` — `jitAllowed = allow.JitProvisioning && domainAllowed;` and `allow.DefaultRole` — **config**, not `Tenant.JitEnabled`/`JitDefaultRole`.
- `AuthService.SsoSignInAsync` (`:2782-2950`) — the only other gate. Checks tenant existence, tenant status, user active, membership active. **Never reads** `AllowedEntraTenantIds`, `AllowedEmailDomains`, `SsoEnabled`, `JitEnabled`, or `JitDefaultRole`.
- Exhaustive read-site grep: those five DB fields appear **only** in DTOs, the validator, the snapshot mapper, the settings-write path, and admin-consent capture. **Zero read sites on the login path.**
- The source admits it — `EntraSsoOptions.cs:9-13`: *"the **dev-POC home** for the security-critical tenant-isolation config (US-AUTH-013). **In the full feature this moves into per-tenant DB config (US-AUTH-012)**."*

**Consequences:**
1. A tenant admin editing the SSO allow-list in the UI changes **nothing** about who can sign in.
2. US-AUTH-016's admin-consent flow writes the customer's directory id into `Tenant.AllowedEntraTenantIds` (`AuthService.cs:2485-2490`) — a value the isolation guard never reads. **Admin-consent onboarding cannot actually enable anyone.**
3. `Tenant.SsoEnabled = false` does **not** block SSO. Neither `BuildAuthorizeUrlAsync` (`:59-76`, checks only global `IsConfigured`) nor `CompleteSignInAsync` consults it. A tenant present in the appsettings allow-list can complete SSO login with SSO switched **off** in its own settings — **fail-open relative to the tenant's own configuration.**
4. The reverse direction *is* fail-closed (a DB-configured tenant absent from appsettings is rejected), so this is a **correctness and operability failure more than an immediately exploitable hole** — but the platform's per-tenant SSO isolation boundary lives in a single global config file editable only by deploy, and the entire US-AUTH-012 surface is decorative with respect to authorization.

**The BR-5 production gate that `STATUS.md:40` declares satisfied is not satisfied.**

### C-2 — Module-level `[x]` is wrong in both directions
`STATUS.md:30-44` marks **all 16** stories `[x]`. Against code: 10 Must-Have ACs PARTIAL, 1 MISSING, 2 CONTRADICTED; 3 Should-Have stories PARTIAL/CONTRADICTED.

### C-3 — Reverse drift (ledger pessimistic, code shipped)
- `STATUS.md:34`: *"**Deferred AC:** challenge not rate-limited."* **False.** `AuthController.cs:464` carries `[EnableRateLimiting("auth-login")]` with the comment *"US-AUTH-005 NFR-4: 10/min/IP anti MFA-code brute force."*
- `STATUS.md:34`: *"the 'MFA secret stored plaintext' claim is **FALSE**."* **This correction is accurate — verified.** `MfaSecretProtector.cs:35` wraps via ASP.NET Data Protection; `DependencyInjection.cs:184-185` persists the key ring to Postgres. Caveat, not a defect: `:39` deliberately returns legacy plaintext as-is when decryption fails, so pre-encryption rows function until the US-PLT-005 backfill heals them.
- The brief's "5 blocked SSO TCs" premise is stale — `TEST-STATUS.md:226` records `[b] blocked | 0`.

---

## GAPS RANKED

1. **🔴 SSO isolation allow-list not DB-backed (C-1) — HIGH · blast radius: entire SSO epic + tenant-isolation posture · Size: M.** Smallest close: in `CompleteSignInAsync`, replace `CheckIsolation(...)` (`:218`) with an async guard loading the tenant's `SsoSettingsSnapshot` (the cache already exists — `AuthService.GetSsoSettingsAsync`) and evaluating `SsoEnabled` + `AllowedEntraTenantIds` + `AllowedEmailDomains` + `JitEnabled`/`JitDefaultRole` from it; keep appsettings only as an explicit dev-override behind an env check, or delete it. **Until then, correct `STATUS.md:40,43,44`.**
2. **🔴 AUTH-013 AC-7 — unverified email accepted for domain allow-listing — HIGH · Size: S.** `:473-487` returns `email`, else `preferred_username`, no verification check. Fix: require `xms_edov == true` (or restrict to tid-match when absent) before `EmailDomain(email)` can satisfy `domainAllowed`.
3. **RBAC roles UI broken at the wire contract — HIGH · Size: S.** `RoleDto.Id` serialises as `id`; `IRole` declares `roleId`. Every `track role.roleId` collapses; edit/delete navigate to `/admin/roles/undefined`. Compounded by `Admin.Roles.Manage` not existing in `PermissionCatalog.cs:318-320`, so Create/Delete never render. **Specs stay green because they mock `roleId` and stub the permission as held** (`role-list.component.spec.ts:25,30,35`) — the exact pilot pattern.
4. **Missing SSO isolation audit events — MEDIUM-HIGH · Size: S.** `sso_isolation_rejected` and `sso_misconfigured`: zero occurrences. `CheckIsolation` writes Serilog warnings the in-app audit search cannot read — while every *other* SSO failure path correctly calls `RecordSsoFailureAsync`.
5. **`RolePermission`/`UserTenantRole` have no global query filter — MEDIUM-HIGH · Size: M.** `AppDbContext.cs:268-276` filters `UserTenant`, `Role`, `RefreshToken` but not the two join entities, which carry no `TenantId`. No leak today (both direct queries pre-verify the tenant) but the invariant rests on caller discipline, not the model.
6. **US-AUTH-015 — Microsoft button renders for every tenant — MEDIUM · Size: S.** BE already emits `ssoEnabled` (`TenantContextController.cs:69`); the FE interface declares only `enforcementMode`.
7. **US-AUTH-010 AC-2 — lockout banner unreachable — MEDIUM · Size: S.** `AuthController.cs:59` uses the single-arg `Fail(error)` overload (contrast `:280`). Generic message happens to carry the right sentence, so the user isn't misled, but `isAccountLocked` never flips.
8. **US-AUTH-004 AC-6 — forced password change written but never enforced — MEDIUM · Size: S.** `PasswordChangedAt = null` is the marker; `LoginAsync` never reads it. **The user's old password still works after an admin force-reset.**
9. **US-AUTH-006 AC-5 — `TeamScopeAuthorizationHandler` orphaned — MEDIUM · Size: M.** Hand-rolled at `LeaveRequestService.cs:1487-1491`, which omits the `.All` bypass — an HR user with `Leave.Approve.All` is wrongly refused.
10. **US-AUTH-002 AC-3 — no user notification on refresh-token reuse — LOW-MED · Size: S.**
11. **US-AUTH-001 AC-6 — "no SPA shell" unproven — LOW · Size: S.**

---

## COVERAGE SUMMARY

```
Requirements audited: 59 | IMPLEMENTED: 43 | PARTIAL: 11 | MISSING: 1 | CONTRADICTED: 4
  Must-Have ACs (51):       IMPLEMENTED 38 | PARTIAL 10 | MISSING 1 | CONTRADICTED 2
  Should-Have stories (8):  IMPLEMENTED  5 | PARTIAL  1 | CONTRADICTED 2
```

**Local authentication is genuinely strong** — 38 of 51 Must-Have ACs pass all three legs, with unusual depth: timing-resistant enumeration defence, atomic lockout under row lock, progressive lockout, refresh-token lineage revocation, key-ring rotation, cross-tenant token rejection. Leg 3 is comprehensively satisfied: 60 IEEE-829 TCs plus ~45 relevant xUnit files including Postgres-backed integration tests.

**The failures cluster in exactly two places, neither being backend logic:**
1. **The frontend/wiring seam — 5 of 11 PARTIALs.** Every one is the pilot's dominant defect class: a field the backend emits under a different name or not at all, a TypeScript model declaring a field the API never sends, or a value written to the DB that nothing reads. **In two cases the Karma specs are green *because* they encode the wrong shape.**
2. **The SSO configuration seam — all 4 CONTRADICTEDs.** The protocol layer (011) and the enforcement layer (016) are genuinely complete and DB-backed. The **authorization layer between them (012/013/014) reads a global appsettings dictionary that the DB surface was supposed to replace** — and the ledger records that replacement as shipped.

Leg-2 (reachability) accounts for 7 of 12 non-pass verdicts; leg-1 for 5; leg-3 for none.

---

## CONFIDENCE

- **C-1 (allow-list appsettings-backed): 97%** — exhaustive read-site enumeration of all five DB fields plus full reads of `CompleteSignInAsync` and `SsoSignInAsync`. Residual 3%: a reflection/middleware binding not searched for. *Settled by:* boot the stack, set `Tenant.AllowedEntraTenantIds` via the API with no matching appsettings entry, attempt SSO login — it should be rejected. **(Orchestrator independently re-verified this at 4 points.)**
- **AUTH-013 AC-7 MISSING: 90%** — `xms_edov`/`email_verified` zero occurrences. Residual doubt: whether Entra's `email` claim is verified-by-construction for the configured scopes — defensible, but not what FR-5 specifies nor documented in code.
- **AUTH-006 AC-1 `roleId` break: 92%** (orchestrator confirmed the two field declarations).
- **AUTH-010 AC-2 FE lockout: 95%** · **US-AUTH-015 gating: 93%** · **MFA-plaintext correction is right: 96%**
- **Should-Have story-level verdicts: ~80%** by construction — story-level depth means spot-checked ACs, not traced.
- **Overall: 90%.**

**What limited this pass:** static reading only, no running stack; `AuthService.cs` is 3,402 lines and ~700 targeted lines were read rather than all of it; two of three planned sub-explorers were refused on a concurrency limit, so US-AUTH-003/004 and the test-binding survey were done by targeted grep rather than exhaustive sweep — a bound test could exist that was not surfaced, which would only strengthen leg 3, never weaken a verdict.

---

## OUT-OF-LANE

- **type:** risk · **severity:** HIGH · **where:** `AppDbContext.cs:268-276` · **what:** `RolePermission` and `UserTenantRole` have no global query filter and carry no `TenantId`; EF does not propagate a principal's filter to a dependent. · **suggested-action:** add `TenantId` + filter, or an architecture test asserting no direct `DbSet` access outside `RoleService`.
- **type:** bug · **severity:** MED · **where:** `features/admin/roles/roles.routes.ts:18,26,41` · **what:** guards gate on `Admin.Roles.Manage`, which does not exist in `PermissionCatalog.cs:318-320` (real names: `Roles.View`/`Roles.Manage`/`Roles.AssignUsers`). · **suggested-action:** correct the strings **and add a build-time test asserting every FE permission literal exists in the catalog — this class of drift will recur.**
- **type:** bug · **severity:** MED · **where:** `LeaveRequestService.cs:1487-1491` · **what:** hand-rolled manager-scope check has no `.All` bypass, so a user holding `Leave.Approve.All` is refused approval outside their direct reports. · **suggested-action:** route through `TeamScopeAuthorizationHandler` (which implements the bypass at `:161-167`).
- **type:** doc-drift · **severity:** MED · **where:** `docs/BA/STATUS.md:40,43,44` · **what:** three claims that the SSO isolation allow-list and JIT gate are DB-backed and the BR-5 gate satisfied. Code contradicts all three. · **suggested-action:** file to `TEST-FINDINGS.md`, re-open US-AUTH-012/013/014; **do not ship SSO to production against the BR-5 claim until the guard reads `TenantAuthSettings`.**
- **type:** doc-drift · **severity:** LOW · **where:** `docs/BA/STATUS.md:34` · **what:** reverse drift — the "challenge not rate-limited" deferral is false. · **suggested-action:** drop the note.
- **type:** test-integrity · **severity:** HIGH · **where:** `role-list.component.spec.ts:25,30,35` · **what:** specs hand-build role mocks with `roleId` and stub `Admin.Roles.Manage` as held — encoding both defects into the fixtures, so the suite stays green over a UI that cannot navigate or delete. · **suggested-action:** route to `@test-authenticator`; generate FE fixtures from backend DTOs or add contract tests so shape drift fails a build.
