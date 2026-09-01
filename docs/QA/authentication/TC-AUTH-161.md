---
id: TC-AUTH-161
user_story: US-AUTH-013
module: Authentication
priority: critical
type: security
status: automated
created: 2026-09-02
automated: 2026-08-08
defect:
  - BUG-298
  - GAP-017
---

# TC-AUTH-161: SSO isolation is decided from the TENANT's own SSO settings — disabled/unconfigured/foreign-directory refused, and a domain match counts only for a VERIFIED email (BUG-298 / GAP-017)

## 1. Test Objective
Verify the US-AUTH-013 isolation decision as it is now implemented: the pure
`SsoIsolationGuard.Evaluate(settings, tid, email, emailVerified)`
(`HRM.Application/Features/Auth/SsoIsolationGuard.cs:53-96`) reads the resolved tenant's **own**
`SsoSettingsSnapshot` — the DB-backed per-tenant record, never the appsettings `TenantAllowList` — and

1. refuses a tenant that has switched SSO off, **before** any allow-list is consulted (`:58-61`);
2. fails **closed** on an enabled-but-empty allow-list, reported as `sso_misconfigured` rather than as an
   attack (`:63-68`, AC-5);
3. refuses an Entra directory (`tid`) that is not in the tenant's `AllowedEntraTenantIds` and whose email
   domain is not admissible (AC-3 / AC-4);
4. (**AC-7 / FR-5, the finding's bundled sub-item GAP-017**) admits an email-domain match **only** when the
   issuer asserted the address as verified (`:81` — `domainAllowed = domainMatches && emailVerified`), so an
   impostor holding `impostor@customer.com` in a permissive foreign directory cannot cross into the
   customer's tenant on the domain rule alone; and
5. never lets an *unverified* address turn a legitimate `tid` match into a refusal — `tid` is bound to the
   issuing directory and cannot be self-asserted.

This TC exists because **BUG-298** is closed on this evidence: before the fix the decision read an
appsettings dictionary, the five per-tenant DB columns had **zero read sites on any login path**, and
`Tenant.SsoEnabled = false` did not block SSO. It was invisible to the 80 then-passing SSO tests because the
decision was reachable only through a full OIDC callback — extracting it into a pure function is what made
the refusals directly testable.

> **This is a documentation of ALREADY-GREEN automated arms, not a manual execution record.** No manual
> browser/Microsoft-login run is claimed here; the callback-side, real-`id_token` proof remains
> [TC-AUTH-142](TC-AUTH-142.md) / [TC-AUTH-143](TC-AUTH-143.md), which stay `blocked` on an interactive
> Microsoft sign-in.

## 2. Related Requirements
- User Story: US-AUTH-013
- Acceptance Criteria: **AC-7 (primary — unverified email must not satisfy domain allow-listing)**;
  also AC-1 (allow-listed `tid` admitted), AC-2 (verified domain as the accepted alternative),
  AC-3 (neither rule matches ⇒ rejected, no JWT, no JIT), AC-4 (org-B user cannot enter tenant A),
  AC-5 (empty allow-list fails closed as a *misconfiguration* event)
- Functional Requirements: FR-2 (tenant-scoped allow-listing on the **resolved** tenant),
  FR-5 (only the verified email/`upn` may satisfy a domain check)
- Business Rules: BR-1 (an SSO login is valid only for the tenant whose allow-list matches),
  BR-2 (`tid` primary, verified domain a permitted alternative), BR-5 (unverified email claims are never
  trusted for an authorization decision)
- Findings: **BUG-298** (allow-list was appsettings-backed, not DB-backed) · **GAP-017** (AC-7 verified-email
  requirement, bundled into the same fix as advised)
- Related stories: US-AUTH-012 (the per-tenant settings this guard now reads), US-AUTH-016 (admin-consent
  onboarding writes `AllowedEntraTenantIds`, which was previously read by nothing)

## 3. Preconditions
- xUnit unit suite; no database, no container, no network — `SsoIsolationGuard.Evaluate` is a pure function
  over an `SsoSettingsSnapshot`, so every arm runs in the standard `scripts/run-backend-tests.sh` gate.
- The guard is the shipped decision point: `EntraSsoService.CompleteSignInAsync` loads the snapshot via
  `IAuthService.GetSsoSettingsBySubdomainAsync` and calls `CheckIsolation(...)` on it
  (`EntraSsoService.cs:218-235`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Customer directory | `11111111-1111-1111-1111-111111111111` | the tenant's allow-listed `tid` |
| Other directory | `22222222-2222-2222-2222-222222222222` | a foreign Entra directory |
| Allowed domain | `customer.com` | exact match only |
| Impostor address | `impostor@customer.com`, `emailVerified: false` | the AC-7 crux |
| Sub-domain address | `attacker@evil.customer.com` | must NOT match `customer.com` |
| Settings snapshot | `SsoEnabled`, `AllowedEntraTenantIds`, `AllowedEmailDomains`, `JitEnabled`, `JitDefaultRole` | the tenant's OWN record (cache-aside), never `Authentication:Entra:TenantAllowList` |

## 5. Test Steps
| Step | Action | Expected Result | Automated by (`HRM.Tests/Unit/SsoIsolationGuardTests`) |
|------|--------|-----------------|--------------------------------------------------------|
| 1 | Evaluate with `SsoEnabled = false` while the caller's `tid` IS allow-listed. | Refused, `Reason = sso_disabled_for_tenant`. The tenant's own switch is honoured **first** — before the fix no login path read it at all. | `SsoDisabledForTenant_IsRefused_EvenWhenTheDirectoryIsAllowListed` |
| 2 | Evaluate with SSO enabled but **both** allow-lists empty. | Refused (fail-closed), `Reason = sso_misconfigured` — AC-5's distinction between "nobody set this up" and "someone tried to get in". | `EnabledButUnconfigured_FailsClosed_AndIsReportedAsMisconfigurationNotAttack` |
| 3 | Evaluate a foreign directory `tid` with a non-allow-listed domain. | Refused, `Reason = sso_isolation_rejected` (AC-3 / AC-4 — cross-tenant entry blocked). | `ADifferentDirectory_IsRefused` |
| 4 | Evaluate the tenant's allow-listed `tid`. | Allowed (AC-1) — the positive control that keeps steps 1-3 from passing vacuously. | `TheAllowListedDirectory_IsAdmitted` |
| 5 | **AC-7:** domain is the ONLY allow rule; foreign `tid`, address at the allow-listed domain, `emailVerified: false`. | **Refused**, `Reason = sso_isolation_rejected`, and `DomainMatchedButUnverified = true` so the near-miss is surfaced to an operator. | `UnverifiedEmail_CannotSatisfyTheDomainRule_GAP017` |
| 6 | Same, with `emailVerified: true`. | Allowed via the domain rule (AC-2). Proves step 5 refused on *verification*, not on the domain comparison. | `VerifiedEmail_SatisfiesTheDomainRule_GAP017` |
| 7 | Allow-listed `tid` **and** allow-listed domain, `emailVerified: false`. | Allowed — an unverified address must not demote a legitimate directory match; `DomainMatchedButUnverified = true` is still reported for the log. | `UnverifiedEmail_DoesNotBlockAnOtherwiseValidDirectoryMatch` |
| 8 | JIT enabled; `tid` matches but the address is at an un-allow-listed domain. | `Allowed = true`, `JitAllowed = false`, `DefaultRole = null` — a `tid`-only match must never auto-create accounts for arbitrary domains inside that directory. | `Jit_RequiresTheVerifiedDomainRule_NotMerelyADirectoryMatch` |
| 9 | JIT enabled; allow-listed domain but `emailVerified: false`. | `JitAllowed = false` — an unverified address must never be what provisions an account. | `Jit_IsRefusedForAnUnverifiedEmail_EvenOnAnAllowListedDomain` |
| 10 | JIT enabled; verified address at the allow-listed domain; tenant `JitDefaultRole = Employee`. | `JitAllowed = true` and `DefaultRole = "Employee"` — the role comes from the **tenant's** setting, not the appsettings `DefaultRole` that was invisible to the admin who configured it. | `Jit_IsAllowedOnAVerifiedAllowListedDomain_AndCarriesTheTenantsDefaultRole` |
| 11 | Tenant `JitEnabled = false` with an otherwise valid verified-domain match. | `JitAllowed = false` — the per-tenant JIT flag is enforced (a BUG-298 consequence: it was previously ignored). | `JitDisabledOnTheTenant_BlocksProvisioning_ForAnOtherwiseValidDomainMatch` |
| 12 | Domain matching with `USER@CUSTOMER.COM` and `user@Customer.Com`. | Allowed — case-insensitive, so a correct user is not refused on casing. (2 arms.) | `DomainMatchingIsCaseInsensitive` (`[Theory]`, 2 `InlineData`) |
| 13 | `tid` allow-listed in upper case, presented in lower case. | Allowed — case-insensitive directory-id comparison. | `DirectoryIdMatchingIsCaseInsensitive` |
| 14 | **Boundary:** email `""` and `"not-an-email"` against a domain-only allow-list. | Refused — an address with no domain part can never satisfy the domain rule. (2 arms.) | `AnEmailWithNoDomain_CannotMatchTheDomainRule` (`[Theory]`, 2 `InlineData`) |
| 15 | **Boundary/security:** `attacker@evil.customer.com` against allow-listed `customer.com`. | Refused — exact host match only; controlling a sub-domain of a customer's domain must not grant entry. | `ASubdomainOfAnAllowListedDomain_DoesNotMatch` |

## 6. Postconditions
- A Microsoft user from a non-allow-listed directory, a user of a tenant that has SSO switched off, and an
  impostor holding an unverified address at an allow-listed domain are each refused, with a distinguishable
  reason (`sso_disabled_for_tenant` / `sso_misconfigured` / `sso_isolation_rejected`) for the audit event.
- The tenant's own `SsoEnabled`, allow-lists, `JitEnabled` and `JitDefaultRole` are the values that decide,
  so the admin UI and the US-AUTH-016 admin-consent flow now affect who can sign in.

## 7. Test Category Tags
- [x] Happy path (allow-listed `tid`; verified allow-listed domain; JIT with the tenant's default role)
- [x] Negative test (disabled tenant, unconfigured tenant, foreign directory, unverified-domain impostor, JIT refusals)
- [x] Boundary test (empty/malformed email, sub-domain of an allow-listed domain, casing on both rules)
- [x] Security test (fail-closed authorization decision; verified-claim requirement, BR-5)
- [x] Multi-tenant isolation (a directory allow-listed for one tenant cannot enter another — AC-4)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the standard backend suite):**
  `src/backend/HRM.Tests/Unit/SsoIsolationGuardTests.cs` — **17 arms** (13 `[Fact]`/`[Theory]`
  declarations; the two `[Theory]` cases contribute 2 `InlineData` rows each), mapped one-to-one to steps
  1-15 above.
- **Binding is by fully-qualified test name, not by trait:** `SsoIsolationGuardTests` carries **no**
  `[Trait("TC", …)]` today, so a runner cannot select this TC by id. Adding
  `[Trait("TC", "TC-AUTH-161")]` at the class level would restore id-based selection — that is a `src/`
  change and is flagged to the caller rather than made here.
- **Coverage limitation — the guard is proven, the shell around it is not.** These arms cover the pure
  decision function only. Two behaviours attributed to the BUG-298 fix have **no automated arm** of their
  own:
  - the **fail-closed settings load** (`EntraSsoService.cs:222-231` — an unloadable
    `SsoSettingsSnapshot` denies with `sso_isolation_rejected` / `tenant_settings_unavailable`), and
  - the **`xms_edov` / `email_verified` claim extraction** (`EntraSsoService.IsEmailVerified`,
    `:536-548`), including the "claim absent ⇒ unknown ⇒ false" rule that step 5 depends on for its input.

  A repo-wide search finds `GetSsoSettingsBySubdomainAsync`, `xms_edov` and `sso_isolation_rejected`
  referenced in **no test file other than** `SsoIsolationGuardTests.cs`. The callback-level proof therefore
  still rests on TC-AUTH-142/143, which remain `blocked` on an interactive Microsoft login.
- **Not covered here (out of this TC's scope):** the audit *persistence* of `sso_isolation_rejected` /
  `sso_misconfigured` / `sso_disabled_for_tenant` through `RecordSsoFailureAsync` — the guard returns the
  reason string; writing it is the shell's job.
