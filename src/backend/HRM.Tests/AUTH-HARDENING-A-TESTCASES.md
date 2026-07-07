# Auth-Hardening-A — IEEE-829 Regression Test Cases

Regression coverage for the 5 auth-hardening items landed on `fix/auth-hardening-a`.
Test type: functional / security unit tests (xUnit + EF InMemory, real service/interceptor/handler seams).
Each xUnit method carries a `@TC-*` binding comment matching the **Test Case ID** below.

| Test Case ID | Item | User Story / Issue | xUnit method(s) | File |
|---|---|---|---|---|
| TC-AUTH-RL-001 | 1. auth-login rate limit | US-AUTH-001 / US-AUTH-005 NFR-4 | `AuthLogin_HasRateLimitAttribute_AUTH001`, `MfaChallenge_HasRateLimitAttribute_AUTH001` | `Unit/AuthLoginRateLimitAttributeTests.cs` |
| TC-ADM-CACHE-001 | 2. subdomain-cache invalidation | US-ADM-004 FR-9 | `TenantSuspend_/TenantTerminate_/TenantReactivate_InvalidatesSubdomainCache_AUTH007` | `Unit/TenantSubdomainCacheInvalidationTests.cs` |
| TC-AUTH-ISO-049 | 3. refresh cross-subdomain reject | ISSUE-049 | `Refresh_ForeignSubdomain_Rejected_ISSUE049`, `Refresh_SameSubdomain_Succeeds_ISSUE049Control` | `Unit/RefreshTokenCrossTenantRejectTests.cs` |
| TC-AUTH-AUDIT-050 | 4. reuse → audit row | ISSUE-050 | `Refresh_ReuseDetected_WritesAudit_ISSUE050` | `Unit/RefreshTokenReuseAuditTests.cs` |
| TC-AUDIT-STAMP-006 | 5a. audit ip/user-agent stamp | ISSUE-006 | `Audit_StampsIpUserAgent_ISSUE006`, `Audit_NoHttpContext_IsNullSafe_ISSUE006` | `Unit/AuditInterceptorRequestContextStampTests.cs` |
| TC-AUTHZ-ACTOR-054 | 5b. authz denied-actor resolution | ISSUE-054 | `AuthzDenied_ResolvesActor_ISSUE054`, `AuthzDenied_Anonymous_WhenUnauthenticated_ISSUE054` | `Unit/PermissionAuthorizationActorResolutionTests.cs` |

---

## TC-AUTH-RL-001 — Login & MFA-challenge carry the auth-login rate-limit policy
- **Objective:** the anti credential-stuffing / MFA-brute-force limiter is wired to the endpoints.
- **Preconditions:** `AuthController` compiled.
- **Steps:** reflect over `AuthController.Login` and `.MfaChallenge`; read their `EnableRateLimitingAttribute`.
- **Expected:** both methods carry `[EnableRateLimiting("auth-login")]` (policy name == `auth-login`).
- **Note:** the limiter's runtime 429 behavior is not unit-testable; the attribute binding is the meaningful
  assertion (live 429 behavior for other policies is covered by `RateLimitClusterApiTests`).
- **Category:** Security.

## TC-ADM-CACHE-001 — Tenant lifecycle evicts the subdomain-resolution cache
- **Objective:** suspend/terminate/reactivate remove the cached subdomain resolution (FR-9), closing the
  stale-Active login-block bypass window.
- **Preconditions:** seeded tenant with mixed-case subdomain `Acme`; spy `IDistributedCache`.
- **Steps:** run each transition through the real `TenantLifecycleService`.
- **Expected:** persisted status changes AND exactly the key `t:subdomain:acme` (lowercased) is removed.
- **Category:** Security, Multi-tenant isolation.

## TC-AUTH-ISO-049 — Refresh token rejected on a foreign subdomain
- **Objective:** a token minted for tenant A cannot be rotated under tenant B.
- **Preconditions:** full happy-path seed for tenant A + one active refresh token.
- **Steps:** present the token with `ITenantContext` resolved to B (reject arm) and to A (control arm).
- **Expected:** B → 401 `Invalid refresh token.` with the token NOT revoked and no replacement issued;
  A → 200 and the token rotates (revoked + chained + replacement persisted).
- **Category:** Security, Multi-tenant isolation, Negative + happy-path control.

## TC-AUTH-AUDIT-050 — Reuse detection writes a queryable audit row
- **Objective:** a replayed revoked/rotated token records `security.refresh_token_reuse_detected`.
- **Preconditions:** seeded already-revoked (rotated) token in a lineage.
- **Steps:** replay the revoked token through the real `AuthService`.
- **Expected:** 401 AND exactly one audit row with that event, actor = token user, tenant = token tenant.
- **Category:** Security.

## TC-AUDIT-STAMP-006 — AuditInterceptor backfills ip/user-agent
- **Objective:** audit rows written without ip/ua get them from the current request; null-safe with no request.
- **Preconditions:** real `AuditInterceptor` wired into `AppDbContext`; fake `IHttpContextAccessor`.
- **Steps:** write an `AuditLog` (no ip/ua) with a request in scope; and again with `HttpContext == null`.
- **Expected:** with request → row carries the request ip + user-agent; no request → no throw, ip/ua null.
- **Category:** Security, Boundary (null request).

## TC-AUTHZ-ACTOR-054 — Authorization-denied actor resolution
- **Objective:** denied-authz log resolves the actor from `sub`; `anonymous` only when unauthenticated.
- **Preconditions:** capturing `ILogger`.
- **Steps:** deny an authenticated principal (with `sub`, lacking the permission), then an unauthenticated one.
- **Expected:** authenticated → log contains `User={sub}` (not `unknown`); unauthenticated → `User=anonymous`.
- **Category:** Security.
