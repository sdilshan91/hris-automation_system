---
id: TC-AUTH-150
user_story: US-AUTH-011
module: Authentication
priority: medium
type: performance
status: draft
created: 2026-07-24
---

# TC-AUTH-150: Microsoft JWKS signing keys are cached and refreshed automatically, not fetched per callback

## 1. Test Objective
Verify NFR-1: the OIDC discovery document and Microsoft JWKS signing keys are cached (via `ConfigurationManager<OpenIdConnectConfiguration>`) and reused across callbacks rather than re-fetched every request, keeping warm-case key lookup fast (<= 5 ms added to the callback). A second callback must not re-fetch metadata.

## 2. Related Requirements
- User Story: US-AUTH-011
- Non-Functional Requirements: NFR-1, NFR-4 (callback round-trip P95 <= 2s)

## 3. Preconditions
- Entra SSO configured. Network capture / a mock metadata+JWKS server is in place to count outbound requests to `.well-known/openid-configuration` and the JWKS URI.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Metadata endpoint | {Authority}/.well-known/openid-configuration | Discovery |
| JWKS endpoint | (jwks_uri from discovery) | Signing keys |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Cold start; perform the first callback (or challenge) that needs the config. | Exactly one fetch of discovery + JWKS occurs to warm the `ConfigurationManager` cache. |
| 2 | Immediately perform a SECOND callback. | NO additional discovery/JWKS fetch — the cached config/signing keys are reused (NFR-1). |
| 3 | Measure the added key-lookup time on the warm callback. | Warm-case key resolution adds <= 5 ms; the full callback round-trip stays within NFR-4 (P95 <= 2s) under normal Microsoft latency. |
| 4 | Confirm automatic refresh behavior. | The cache refreshes per the ConfigurationManager's automatic refresh interval (or on a signature-key-not-found), not on every request. |

## 6. Postconditions
- Signing keys are served from cache across callbacks; refresh is automatic, not per-request.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
