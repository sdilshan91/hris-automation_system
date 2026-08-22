---
name: reference-glitchtip-error-tracking
description: US-PLT-006 GlitchTip/Sentry error-tracking wiring — where it lives, the inert guard, PII scrub, tenant-tag seam
metadata:
  type: reference
---

US-PLT-006: error tracking via self-hosted GlitchTip (Sentry-API-compatible). Uses the Sentry .NET SDK
(`Sentry.AspNetCore` + `Sentry.Serilog`, pinned **6.7.0** — the 6.6.0+ line that supports .NET 10).

Where it lives (all in `HRM.Api/Observability/`):
- `GlitchTipErrorTracking.cs` — the wiring. `IsEnabled(config)` = `!string.IsNullOrWhiteSpace(config["GlitchTip:Dsn"])`
  is the **single inert guard** gating BOTH `builder.WebHost.AddGlitchTipErrorTracking(...)` (UseSentry) and the
  Serilog `.WriteToGlitchTip(...)` sink. Blank DSN (shipped default) ⇒ no SDK init, no network (AC-3).
- `SentryPiiScrubber.cs` — pure static `Scrub(SentryEvent)` used in `SetBeforeSend`. Nulls Request.Data/QueryString/
  Cookies + User.Email/IpAddress; redacts sensitive headers (`SensitiveHeaderNames`) and PII-marked keys
  (`PiiKeyMarkers`: email/national/nid/ssn/…) in Extra/Env/Other to `"[Filtered]"`. Preserves tenant tags.
- `TenantTagSentryEventProcessor.cs` — **scoped** `ISentryEventProcessor`; injects `ITenantContext` to stamp
  `tenant_id`/`tenant_subdomain` tags (AC-1). Scoped because Sentry.AspNetCore resolves event processors from the
  request DI scope. Skips system/unresolved context.

Non-obvious facts (verified via reflection against Sentry 6.7.0):
- **Sentry auto-redacts ONLY the `Authorization` header** to `"[Filtered]"` on assignment. `Cookie`, `X-Api-Key`,
  Request.Data/QueryString/Cookies, User.Email/IpAddress are all kept by the SDK — our scrubber removes them. So a
  mutation-meaningful scrub test must assert on Cookie/X-Api-Key/body/email (NOT Authorization, which Sentry scrubs
  regardless). See `GlitchTipErrorTrackingTests` (TC-PLT-006, 10 tests).
- Serilog sink uses `InitializeSdk = false` so it shares the hub `UseSentry` inits (no double init). Both are
  additive to the existing console/file sinks and the OTel wiring (`ObservabilityExtensions`) — replace neither.
- DSN is a secret: blank placeholder `"GlitchTip": { "Dsn": "" }` in appsettings.json; real value via user-secrets/env.
- Config/scope only — AC-6 (FE `@sentry/angular`) and AC-7 (gt-pgdata backup, ops/glitchtip/) are out of src/ lane.
