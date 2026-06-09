---
id: TC-AUTH-053
user_story: US-AUTH-007
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-09
---

# TC-AUTH-053: Subdomain validation rejects invalid and boundary values

## 1. Test Objective
Verify that subdomain parsing enforces valid slug boundaries before cache or database lookup, including length, character set, and leading or trailing hyphen rules.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2, FR-7
- Non-Functional Requirements: NFR-4, NFR-5
- Business Rules: BR-1, BR-2

## 3. Preconditions
- Platform base domain is configured as `yourhrm.com`.
- Tenant `abc` exists and is active.
- No tenants exist for invalid test values.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Minimum valid slug | abc | 3 characters |
| Maximum valid slug | aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa | 63 characters |
| Too short slug | ab | 2 characters |
| Too long slug | aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa | 64 characters |
| Invalid character slug | acme_qa | Underscore is not allowed |
| Leading hyphen slug | -acme | Invalid |
| Trailing hyphen slug | acme- | Invalid |
| Uppercase slug | ACME | Invalid by lowercase-only rule |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET https://abc.yourhrm.com/`. | Host passes validation and tenant resolution may proceed. |
| 2 | Send `GET https://{63-character-slug}.yourhrm.com/` for an active tenant fixture. | Host passes validation and tenant resolution may proceed. |
| 3 | Send `GET https://ab.yourhrm.com/`. | HTTP 404 static workspace-not-found page; no tenant lookup is attempted. |
| 4 | Send `GET https://{64-character-slug}.yourhrm.com/`. | HTTP 404 static workspace-not-found page; no tenant lookup is attempted. |
| 5 | Send requests for `acme_qa`, `-acme`, `acme-`, and `ACME`. | Each request is rejected with the same safe 404 behavior and no tenant context. |
| 6 | Inspect logs for invalid subdomain attempts. | Logs record a validation failure without stack traces, SQL fragments, or internal service details in the response. |
| 7 | Verify Redis and PostgreSQL lookup calls. | No cache read, cache write, or database lookup is performed for invalid slugs. |

## 6. Postconditions
- Invalid hostnames do not create tenant context or cache entries.
- Public error response remains indistinguishable from unknown workspace behavior.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
