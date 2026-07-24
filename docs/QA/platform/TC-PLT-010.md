---
id: TC-PLT-010
user_story: US-PLT-006
module: Platform
priority: high
type: security
status: draft
created: 2026-07-24
---

# TC-PLT-010: Fail-safe — blank GlitchTip:Dsn leaves the SDK inert (no init, no network, app unaffected); DSN is committed blank and only supplied via user-secrets/env

## 1. Test Objective
Verify AC-3 (and NFR-3, BR-3, Critical Rule #6). With the **shipped default** `GlitchTip:Dsn` blank, the SDK
must initialise **inert**: no network calls, no events queued, and request handling / app behaviour is
completely unaffected (safe-by-default — a blank or unreachable DSN must never crash or degrade the app). Also
proves the DSN is a **secret**: the committed `appsettings.json` value is blank and the real value comes only
from user-secrets/env (no secret is ever committed).

## 2. Related Requirements
- User Story: US-PLT-006
- Acceptance Criteria: AC-3
- Functional Requirements: FR-7 (blank placeholder in `appsettings.json`), FR-8 (inert when blank)
- Non-Functional: NFR-3 (fail-safe / inert-by-default)
- Business Rule: BR-3 (DSN is a secret, user-secrets/env only); Critical Rule #6 (secrets in .env only)

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Start the API with `GlitchTip:Dsn` blank (the shipped default). | App starts normally; no Sentry init error; no attempt to reach a GlitchTip host. |
| 2 | Force several exceptions across requests. | Requests are handled exactly as without the SDK; no crash, no added latency spike, no exception queued for transmit. |
| 3 | Observe outbound network (or the transport spy). | Zero network calls toward any Sentry/GlitchTip endpoint — the SDK is inert. |
| 4 | Inspect the committed `src/backend/HRM.Api/appsettings.json`. | Contains `"GlitchTip": { "Dsn": "" }` — a **blank** placeholder; no DSN secret committed (grep `appsettings*.json` for a non-empty Dsn → none). |
| 5 | Confirm the real DSN path. | The only non-blank DSN source is user-secrets (`GlitchTip:Dsn`) or an environment variable — never the committed config (BR-3). |
| 6 | Set an intentionally **unreachable** DSN and force exceptions. | App still handles requests without crashing or blocking on the unreachable host (fail-safe; capture is async/best-effort). |

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Shipped default Dsn | `""` (blank) | in committed appsettings.json |
| Real Dsn source | user-secrets / env `GlitchTip:Dsn` | never committed |
| Unreachable Dsn | `http://127.0.0.1:1/1` | for the fail-safe arm |

## 6. Postconditions
- The app is safe to run with no error-tracking configured (dev/CI default) and safe when GlitchTip is down;
  no DSN secret exists in the repository.

## 7. Test Category Tags
- [x] Happy path (blank DSN → inert, app normal)
- [x] Negative test (unreachable DSN must not crash/block)
- [x] Boundary test (empty-string DSN is the guard boundary)
- [x] Security test (no committed secret; secret sourced from user-secrets/env only — Critical Rule #6)
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Intended binding:** an xUnit arm `[Trait("TC", "TC-PLT-010")]` booting the host with a blank DSN, forcing
  exceptions, and asserting no capture transport activity + normal responses; plus a repo-hygiene assertion
  (or CI grep) that every committed `appsettings*.json` has a blank `GlitchTip:Dsn`.
- **Status:** `draft` — SDK layer unwired; forward-looking. Flips to `automated` when the arm lands.
