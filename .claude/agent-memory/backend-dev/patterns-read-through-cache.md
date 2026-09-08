---
name: patterns-read-through-cache
description: Read-through caching over tenant aggregates — the established pattern, and the two non-obvious key-design traps (scope segment, and tenant segment being unprovable behaviourally).
metadata:
  type: project
---

The repo has a settled read-through pattern over `IDistributedCache?` (optional last ctor param, so DI
injects Redis-or-memory-fallback while a hand-`new`ed test instance simply doesn't cache): private
`TryGetCachedAsync`/`SetCachedAsync` with try/catch **fail-open** (BUG-115), TTL via
`AbsoluteExpirationRelativeToNow`, key prefixed by `CacheTenantPrefix.For(tenantContext)`.
Live examples: `HrReportService`, `DashboardService`, `PerformanceDashboardService` (ISSUE-129).

**Why the traps matter:** a cache key that under-specifies is a data leak, not a perf bug.

**How to apply:**

1. **Any role-scoped read must fold the caller's SCOPE into the key** — and key on the *resolved
   restriction set*, not the scope KIND. Two managers both hold `Team` scope but see different
   populations; kind-only keying lets one read the other's aggregate. Hashing the restrict-set also
   self-invalidates when someone's team changes, which TTL-only keying does not.
   `HrReportService.IsScopedReport`/`ScopeKeyOf` exists because ISSUE-195 hit exactly this.
2. **The tenant segment usually cannot be proven by a behavioural test.** Tenant-unique GUIDs
   (cycle ids, employee ids) already feed the filter hash, so deleting the tenant prefix still yields
   distinct keys and every behavioural assertion stays green. Prove it **structurally** instead: wrap
   the cache in a recorder and assert each entry was written under `t:{tenantId}:`. Without that, a
   "tenant isolation" test is theater.
3. **Explicit eviction is not implementable over `IDistributedCache`** when the key hashes the reader's
   scope: the writer doesn't know the reader's scope set, and there is no prefix-scan/tag support.
   Reaching past the abstraction to `IConnectionMultiplexer` breaks the no-Redis fallback. TTL-only is
   the honest choice — state the observable staleness rather than implying freshness.

Related: [[workflow-mutation-proof-in-worktree]]
