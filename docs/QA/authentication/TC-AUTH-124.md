---
id: TC-AUTH-124
user_story: US-AUTH-012
module: Authentication
priority: medium
type: performance
status: draft
created: 2026-07-24
---

# TC-AUTH-124: SSO settings UI loads within 1 second and validates inline before submit

## 1. Test Objective
Verify NFR-3: the SSO configuration card loads within 1 second and validates inputs inline (client-side) before submit, so `tid`/domain/role errors surface without a server round-trip and the admin gets immediate feedback. Confirms the perf budget and that inline validation gates the submit button.

## 2. Related Requirements
- User Story: US-AUTH-012
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" plan has `Sso = true`; `admin-a@acme.com` is a tenant admin.
- Measured on a mid-tier device over a normal broadband profile; front end built in production mode.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Load budget | <= 1000ms | Card fully interactive |
| invalid tid | not-a-guid | Should error inline, no network call |
| invalid domain | acme..com | Should error inline, no network call |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Security > Single Sign-On; measure time to the card being fully rendered and interactive. | Card is interactive within 1 second (NFR-3). |
| 2 | Type `not-a-guid` into the `tid` input and blur. | An inline GUID validation error appears immediately with NO network request to the settings API (client-side validation). |
| 3 | Type `acme..com` into the domain input and blur. | An inline domain validation error appears immediately, no network call. |
| 4 | Observe the Save button while any field is invalid. | Save is disabled/blocked until all inline validations pass. |
| 5 | Correct the inputs to valid values. | Inline errors clear; Save becomes enabled. |

## 6. Postconditions
- The SSO card meets the load budget and blocks submission on invalid input before contacting the server.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
