---
name: feedback-environment-gated-guards
description: Environment-gated startup guards must be allow-list gated and read IHostEnvironment, never the raw ASPNETCORE_ENVIRONMENT config string
metadata:
  type: feedback
---

When writing a startup fail-fast that is gated on the environment (blank secret, unsafe dev fallback,
log-only stub), two things are non-negotiable:

1. **Allow-list the safe case, never deny-list the dangerous one.** Write
   `if (env.IsDevelopment()) return; throw ...`, not `if (env == "Production") throw ...`.
2. **Take `IHostEnvironment`, never `configuration["ASPNETCORE_ENVIRONMENT"]`.**

**Why:**
- A deny-list on `== "Production"` permits Staging, QA, a bespoke environment name, a misspelling, and an
  empty value — all places where the hazard is just as real. Worse, its failure mode is *correlated* with
  the fault it catches: whoever forgot to set the secret is exactly the person liable to have left the
  environment name unset too. A guard that switches off under the same omission it guards against is not
  a guard.
- The raw config string is a *different value* from the resolved environment. `IHostEnvironment.EnvironmentName`
  is the single authoritative resolution of three sources: the `ASPNETCORE_ENVIRONMENT` variable, the
  `--environment` switch, and `IWebHostBuilder.UseEnvironment(...)`. Both `WebApplicationFactory` fixtures in
  `HRM.Tests` call `UseEnvironment(Environments.Development)`, which sets the host's `environment` key and
  **never** sets `ASPNETCORE_ENVIRONMENT` — so a raw read sees `null` there. Separately, when the variable is
  genuinely unset, `IHostEnvironment` resolves to `"Production"` while the raw read is `null` (~95% confident).

**How to apply:** any new guard of this class in `HRM.Api`/`HRM.Infrastructure`. The existing `Smtp:Host`
guard (`DependencyInjection.cs:866-877`) is the *counter*-example — it is deny-list gated AND reads the raw
string, so it fails open twice over; queue item `G15` covers its missing test. Do not copy its shape.
`JwtSigningKeyStartupGuard` is the shape to copy. Prove the choice with mutation arms for the *non*-Production
environment names (`""`, `"Staging"`, a misspelling) — those are the only arms that distinguish allow-list
from deny-list, and without them the two gatings are indistinguishable to the suite.

Related: [[reference-jwt-denylist-session-revocation]]
