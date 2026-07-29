---
id: TC-AUTH-149
user_story: US-AUTH-011
module: Authentication
priority: high
type: security
status: pass
created: 2026-07-24
---

# TC-AUTH-149: No `id_token`, `access_token`, authorization code, or client secret is ever written to logs

## 1. Test Objective
Verify NFR-5: across a full SSO flow — success and each failure path — the Serilog file/console sinks contain NO `id_token`, `access_token`, authorization `code`, `client_secret`, or PKCE `code_verifier` material. Only non-sensitive context (tenant, outcome, failure reason, `tid`/`oid` where relevant) may appear.

## 2. Related Requirements
- User Story: US-AUTH-011
- Non-Functional Requirements: NFR-5, NFR-2 (secrets from user-secrets, never committed)
- Functional Requirements: FR-8 (audit carries non-sensitive context)

## 3. Preconditions
- Entra SSO configured with the secret sourced from user-secrets. Serilog file sink at `HRM.Api/Logs/hrm-<date>.log` is writable; Development level (Debug + EF SQL) is active — the strictest capture case.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Log file | src/backend/HRM.Api/Logs/hrm-<YYYYMMDD>.log | Grep target |
| Sensitive markers | the actual code, id_token, access_token, client secret, code_verifier strings used in the run | Search for these verbatim |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run a successful callback (TC-AUTH-138), capturing the exact `code`/`id_token`/`code_verifier`/secret used. | Session created. |
| 2 | Run a code-exchange failure (TC-AUTH-147 case 4a) and a token-validation failure (TC-AUTH-145 case 4a). | Both rejected. |
| 3 | `grep` the log file for the verbatim `code`, `id_token`, `access_token`, `client_secret`, and `code_verifier` values. | ZERO matches for every sensitive value — even on the failure paths where a body is logged, the token/code/secret substrings are absent. |
| 4 | Confirm what IS logged. | Only non-sensitive fields: tenant subdomain, outcome/`sso_error` code, failure reason, `tid`/`oid`/email domain, HTTP status. |
| 5 | Confirm the secret is not in committed config. | `Authentication:Entra:ClientSecret` is blank in `appsettings*.json`; the real value lives only in user-secrets (NFR-2 / Critical Rule #6). |

## 6. Postconditions
- No token/code/secret leaked to logs on any path; config carries no secret.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
