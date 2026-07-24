---
id: TC-PLT-011
user_story: US-PLT-006
module: Platform
priority: high
type: integration
status: draft
created: 2026-07-24
---

# TC-PLT-011: Telemetry stays in-boundary and additive — the GlitchTip sink composes alongside the Serilog console+file sinks (file remains the RequestId QA log); no third-party cloud egress

## 1. Test Objective
Verify AC-4 (and NFR-2, BR-4). Exception telemetry stays **within our trust boundary** — the only configured
destination is the self-hosted GlitchTip DSN, with **no third-party cloud egress**. The GlitchTip sink is
**additive**: the existing Serilog **console + rolling file** sinks continue to emit unchanged, so the daily
`Logs/hrm-<date>.log` remains the authoritative QA/`RequestId` root-cause log. Enabling GlitchTip must not
replace or degrade the existing sinks.

## 2. Related Requirements
- User Story: US-PLT-006
- Acceptance Criteria: AC-4
- Functional Requirements: FR-2 (Sentry Serilog sink alongside console+file)
- Non-Functional: NFR-2 (in-boundary; no third-party sub-processor)
- Business Rule: BR-1 (telemetry stays in trust boundary), BR-4 (additive, file sink stays authoritative)

## 3. Preconditions
- API running with a valid self-hosted `GlitchTip:Dsn` and the existing Serilog console+file sinks configured.
- A throwing endpoint behind the `acme` subdomain that produces both a file-log line and a captured event.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| File log path | `src/backend/HRM.Api/Logs/hrm-<date>.log` | daily rolling, 31-file retention |
| Configured sinks | Console + File + Sentry | additive set |
| Allowed egress target | self-hosted GlitchTip DSN host only | no SaaS Sentry / Datadog / cloud |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With GlitchTip enabled, force an exception behind `acme` and note the request's `RequestId`. | Exception handled; a capture event is produced. |
| 2 | Inspect the daily file log `Logs/hrm-<date>.log`. | The exception line is present, enriched with `TenantId`/`TenantSubdomain`/`RequestId` — the file sink still works (BR-4). |
| 3 | Inspect the console sink output. | The exception is also written to console — the console sink still works. |
| 4 | Enumerate the effective Serilog sink set. | Console + File + Sentry all present; GlitchTip was ADDED, not substituted (FR-2). |
| 5 | Inspect the configured event destination(s). | The only network destination is the self-hosted GlitchTip DSN host; no SaaS Sentry, Datadog, or other cloud endpoint is configured (BR-1, NFR-2). |
| 6 | Cross-check the same `RequestId` appears in both the file log AND (as `RequestId`/tag context) the captured event. | The two logs are correlated by `RequestId` — the file log remains the QA root-cause path. |

## 6. Postconditions
- The observability posture is additive and in-boundary: file log unchanged and authoritative for QA; the
  captured event reaches only the self-hosted tracker.

## 7. Test Category Tags
- [x] Happy path (additive sinks, file log preserved)
- [ ] Negative test
- [ ] Boundary test
- [x] Security test (data-residency: no third-party cloud egress — BR-1/NFR-2)
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Intended binding:** an xUnit/integration arm `[Trait("TC", "TC-PLT-011")]` that asserts the effective
  Serilog configuration contains the console + file + Sentry sinks, that a forced exception produces a file-log
  line under `Logs/`, and that the sole configured event destination is the `GlitchTip:Dsn` host.
- **Status:** `draft` — SDK layer unwired; forward-looking. Flips to `automated` when the arm lands.
