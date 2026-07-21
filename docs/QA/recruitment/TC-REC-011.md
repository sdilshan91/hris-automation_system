---
id: TC-REC-011
user_story: US-REC-008
module: Recruitment
priority: medium
type: functional
status: automated
created: 2026-07-21
automated: 2026-07-21
defect:
  - DF-7
---

# TC-REC-011: Candidate-portal magic-link per-IP throttle is a distributed Redis counter (multi-instance) with a DB sliding-window fallback (DF-7 — US-REC-008 NFR-6)

## 1. Test Objective
Verify the DF-7 refactor of the per-IP applicant-portal magic-link throttle
(`ApplicantPortalTokenService.IssueAsync`) from an inline DB `CountAsync` into the
`IPortalLinkIpRateLimiter` seam, so the per-IP cap holds across **all** API instances at multi-instance
scale. When Redis is configured, `RedisPortalLinkIpRateLimiter` enforces the cap with a **fixed-window**
`INCR`+`EXPIRE` counter keyed per-(tenant, IP); when Redis is not configured, the original
**sliding-window** `DbCountPortalLinkIpRateLimiter` (the correct, shared-across-instances DB count) is
used. Both share the same limits (`Recruitment:PortalLink:MaxIssuesPerIp`=10, `IpWindowSeconds`=3600) and
both return the byte-identical 429 `rate_limited` response. The Redis path is **fail-open** (a Redis error
allows, never locks out legitimate applicants). The per-email guard (`RecentTokenWindowSeconds`=60) is
unchanged.

## 2. Related Requirements
- User Story: US-REC-008 (candidate portal magic-link)
- Non-Functional Requirement: NFR-6 (per-IP anti-abuse throttle on link issuance)
- Business Rule: BR-5 (anti-enumeration: only issue for a real applicant); per-(tenant, IP) cap
- Finding: DF-7 (distributed Redis limiter, previously a documented deferral)

## 3. Preconditions
- The Redis limiter tested via an NSubstitute stateful `IDatabase`/`IConnectionMultiplexer` fake (dict-backed INCR) — no Docker, mirrors `RedisPermissionCacheTests`.
- The DB fallback tested InMemory-through-real-EF (seeds real `ApplicantPortalToken` rows).
- The DI branch resolved from the real `AddInfrastructure` registration both ways.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| MaxIssuesPerIp | 10 | default; shared by both impls |
| IpWindowSeconds | 3600 | fixed (Redis) / sliding (DB) window |
| Redis key | `hrm:ratelimit:portal-link:{tenantId}:{ip}` | per-(tenant, IP) |
| 429 body | "Too many portal link requests. Please try again later.", `rate_limited` | byte-identical to pre-DF-7 |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Redis: acquire 11 times for one (tenant, IP). | First 10 → allow (`true`), 11th → block (`false`). | `RedisPortalLinkIpRateLimiterTests.FirstTenAcquire_EleventhIsBlocked` |
| 2 | Redis: a different (tenant, IP) key. | Independent count — starts fresh. | `RedisPortalLinkIpRateLimiterTests.DifferentTenantIpKey_IsIndependent` |
| 3 | Redis: inspect the window TTL. | `EXPIRE` set exactly once, on the first `INCR`, with the configured TTL. | `RedisPortalLinkIpRateLimiterTests.Expire_IsSetOnlyOnTheFirstHit` |
| 4 | Redis backing store throws. | Fail-open: `TryAcquireAsync` returns `true` (allow), never denies. | `RedisPortalLinkIpRateLimiterTests.RedisThrows_FailsOpen_Allows` |
| 5 | DB fallback: 10 tokens for a (tenant, IP), then the 11th request. | Under cap allows; at cap blocks; per-(tenant, IP). | `DbCountPortalLinkIpRateLimiterTests.UnderCap_Allows_AtCap_Blocks_PerTenantIp` |
| 6 | DB fallback: a different IP. | Independent count. | `DbCountPortalLinkIpRateLimiterTests.DifferentIp_IsIndependent` |
| 7 | DB fallback: a token older than the window. | Not counted (sliding window). | `DbCountPortalLinkIpRateLimiterTests.TokensOlderThanWindow_AreNotCounted` |
| 8 | Service path: 10 issuances then the 11th from one IP. | 11th → 429 `rate_limited` through the extracted limiter; different IP unaffected; no-HttpContext skips the throttle. | `ApplicantPortalTokenServiceTests` (existing per-IP arms, re-wired to the DbCount limiter) |
| 9 | DI: Redis unset vs set. | Unset → `DbCountPortalLinkIpRateLimiter` registered; set → `RedisPortalLinkIpRateLimiter`. | `RedisWiringDiRegistrationTests.WhenRedisNotConfigured_PortalLinkLimiterIsDbCount` / `WhenRedisConfigured_PortalLinkLimiterIsRedisBacked` |

## 6. Postconditions
- The per-IP portal-link cap is enforced consistently at multi-instance scale (Redis) or single-instance
  (DB fallback); a Redis outage degrades to allow; the 429 contract and the per-email guard are unchanged.

## 7. Test Category Tags
- [x] Happy path (under-cap allow)
- [x] Negative test (11th blocked; fail-open on Redis error)
- [x] Boundary test (fixed-window first-hit EXPIRE; sliding-window edge)
- [x] Security test (anti-abuse throttle)
- [x] Multi-tenant isolation (per-(tenant, IP) keying)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite), carrying `[Trait("TC", "TC-REC-011")]`:** the 4 `RedisPortalLinkIpRateLimiterTests` + 3 `DbCountPortalLinkIpRateLimiterTests` arms above; the DI-branch arms in `RedisWiringDiRegistrationTests`; and the existing `ApplicantPortalTokenServiceTests` per-IP arms (re-wired, unchanged assertions).
- Fallback-table index gap filed [[DF-59]]. Redis path avoids the COUNT entirely.
