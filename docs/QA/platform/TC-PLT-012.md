---
id: TC-PLT-012
user_story: US-PLT-006
module: Platform
priority: high
type: integration
status: draft
created: 2026-07-24
---

# TC-PLT-012: Serilog Sentry sink at Error level + ASP.NET Core UseSentry integration wired; sub-Error events do NOT reach GlitchTip; existing OpenTelemetry wiring left intact

## 1. Test Objective
Verify AC-5 (and FR-2, FR-3, NFR-4, BR-4). Prove the pipeline is wired via a **Serilog `WriteTo.Sentry` sink
at `Error` minimum level** PLUS the **ASP.NET Core integration** (`UseSentry`), and — crucially — that the
`Error` threshold holds: an `Information`/`Warning` log does NOT produce a GlitchTip event, keeping ingestion
volume low (NFR-4). Also confirm the existing **OpenTelemetry** wiring (`ObservabilityExtensions`/
`AddObservability`) is left in place and complementary — GlitchTip does not replace or disturb OTel.

## 2. Related Requirements
- User Story: US-PLT-006
- Acceptance Criteria: AC-5
- Functional Requirements: FR-2 (Serilog sink `MinimumEventLevel = Error`), FR-3 (`UseSentry`)
- Non-Functional: NFR-4 (Error-only capture keeps overhead low)
- Business Rule: BR-4 (OTel complementary, not replaced)

## 3. Preconditions
- API running with a valid `GlitchTip:Dsn` and a capture transport/spy.
- OTel is present but endpoint-gated (default blank OTLP endpoint) as shipped.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Sink min level | `Error` | `WriteTo.Sentry` MinimumEventLevel |
| Info log | `Log.Information("routine")` | must NOT reach GlitchTip |
| Warning log | `Log.Warning("soft")` | must NOT reach GlitchTip |
| Error log / exception | `Log.Error(ex, ...)` | MUST reach GlitchTip |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Inspect the effective Serilog config. | A `Sentry` sink is present with `MinimumEventLevel == Error` and `SendDefaultPii == false` (FR-2). |
| 2 | Confirm the ASP.NET Core integration is wired. | `builder.WebHost.UseSentry(...)` is active with a `SetBeforeSend` hook and `SendDefaultPii == false` (FR-3). |
| 3 | Emit an `Information` and a `Warning` log. | Neither produces a captured GlitchTip event (below the `Error` threshold — NFR-4). |
| 4 | Emit an `Error` log / throw an unhandled exception. | Exactly this reaches the capture transport as an event (Error-and-above only). |
| 5 | Inspect the OTel wiring. | `AddObservability`/`ObservabilityExtensions` remains registered and unchanged; OTel and GlitchTip coexist (BR-4). |

## 6. Postconditions
- Only Error-and-above events reach GlitchTip via two complementary paths (Serilog sink + ASP.NET Core
  integration); OTel traces/metrics wiring is undisturbed.

## 7. Test Category Tags
- [x] Happy path (Error event captured via wired sink + integration)
- [x] Negative test (Info/Warning must NOT be captured)
- [x] Boundary test (the Error minimum-level threshold)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Intended binding:** an xUnit/integration arm `[Trait("TC", "TC-PLT-012")]` asserting the Sentry sink's
  minimum level == Error, that Info/Warning logs yield no captured event while an Error/exception does, and
  that `AddObservability` is still registered.
- **Status:** `draft` — SDK layer unwired; forward-looking. Flips to `automated` when the arm lands.
