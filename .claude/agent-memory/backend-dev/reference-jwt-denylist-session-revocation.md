---
name: reference-jwt-denylist-session-revocation
description: P3-2 real JWT access-token denylist / session revocation — seams, fail-open rule, Redis overload gotcha
metadata:
  type: reference
---

P3-2: real session revocation replacing `NoOpSessionRevoker`. Design = per-(tenant,user) "revoked-before"
CUTOFF timestamp in Redis (NOT per-jti enumeration).

Seams (all `src/backend`):
- `ITokenDenylist` (Application/Common/Interfaces): `RevokeAsync` + `IsRevokedAsync(userId,tenantId,iatUnix)`.
- `RedisTokenDenylist` (Infrastructure/Services): key `hrm:revoked:{tenantId}:{userId}` = nowUnix, **TTL 16 min**
  (access-token life 15 + buffer → self-cleans). Uses shared `IConnectionMultiplexer`.
- `NoOpTokenDenylist` + `NoOpSessionRevoker` = Redis-absent fallback (IsRevoked always false).
- `RedisSessionRevoker : ISessionRevoker` wraps `ITokenDenylist.RevokeAsync`. SignalR disconnect DEFERRED (optional).
- `SessionRevocationCheck.IsRevokedAsync(principal, denylist, ct)` (HRM.Api/Auth) — extracted so the
  `OnTokenValidated` hook logic is unit-testable; Program.cs hook just calls it + `context.Fail`.
- DI gate in `DependencyInjection.cs`: Redis-configured → Redis pair, else No-op pair (same config check as
  `SharedRedisRegistration`). `IConnectionMultiplexer` registered by `Program.cs AddSharedRedisMultiplexer`.
- `JwtService` now emits explicit `JwtRegisteredClaimNames.Iat` on BOTH access + impersonation tokens — the
  `JwtSecurityToken(issuer,audience,claims,notBefore,expires,creds)` ctor sets nbf/exp but NOT iat.

**CRITICAL security rule: FAIL-OPEN everywhere.** Any Redis error / missing key / parse fail / null denylist /
missing claims → treat as NOT revoked. A Redis blip must never mass-lock-out. `SessionRevocationCheck` catches
every exception; `RedisTokenDenylist.IsRevokedAsync` catches internally too.

**Gotcha — StackExchange.Redis 3.0.11 `StringSetAsync` 3-arg call is AMBIGUOUS** (4/5/6-param overloads all
bind). Pin it: `StringSetAsync(key, value, ttl, When.Always, CommandFlags.None)`. Tests mock that exact overload.

Tests: `HRM.Tests/Unit/SessionRevocationTests.cs` — NSubstitute over `IDatabase`/`IConnectionMultiplexer` (no
Testcontainer needed; contract allows faking IDatabase). Reads claims 1:1 because `MapInboundClaims=false`
(sub/tenant_id/iat verbatim). Full suite 3630 green (Docker present that run).
