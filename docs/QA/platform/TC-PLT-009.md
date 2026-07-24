---
id: TC-PLT-009
user_story: US-PLT-006
module: Platform
priority: critical
type: security
status: draft
created: 2026-07-24
---

# TC-PLT-009: BeforeSend scrubs ALL PII before an event leaves the process — request body, Authorization, cookies/session, email, national ID removed; SendDefaultPii=false (with a negative national-ID arm)

## 1. Test Objective
Verify AC-2 — **the crux security control** and the accepted condition under which self-hosting was approved
(BR-2, NFR-1). Prove that the `BeforeSend` hook strips the **request body**, the **`Authorization` header**,
**cookies/session** data, and **known PII fields** (email, national ID) from every outgoing event, and that
`SendDefaultPii = false` is enforced so no default PII (IP, headers, body) is auto-attached. Includes an
explicit **negative arm**: a national-ID value present on the inbound request MUST NOT appear anywhere in the
captured event payload. No PII may leave the process — this is a hard fail-closed condition.

## 2. Related Requirements
- User Story: US-PLT-006
- Acceptance Criteria: AC-2
- Functional Requirements: FR-3 (`SendDefaultPii=false`), FR-4 (`BeforeSend` strips body/headers/PII)
- Non-Functional: NFR-1 (no PII leaves the process)
- Business Rule: BR-2 (scrubbing non-negotiable)

## 3. Preconditions
- API running with a valid `GlitchTip:Dsn` and an in-process capture transport/spy that records the exact
  serialized event that WOULD be transmitted (i.e. captured AFTER `BeforeSend`).
- A throwing endpoint reachable behind the `acme` subdomain, accepting a JSON body and headers.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request body | `{"email":"alice@acme.test","nationalId":"199012345678","note":"secret"}` | PII payload |
| Authorization header | `Bearer eyJhbGciOi...SECRET` | must be scrubbed |
| Cookie header | `session=abc123; .AspNetCore.Session=xyz` | must be scrubbed |
| Query string | `?national_id=199012345678&token=leak` | must be scrubbed |
| PII sentinel (negative arm) | `199012345678` (national ID) | MUST NOT appear anywhere in the event |
| PII sentinel #2 | `alice@acme.test` (email) | MUST NOT appear anywhere in the event |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | POST the PII body + Authorization + Cookie headers + PII query string to the throwing endpoint. | Exception is captured; the capture transport records the post-`BeforeSend` event. |
| 2 | Assert `options.SendDefaultPii == false` on the effective Sentry configuration. | `false` — default PII attachment is disabled (FR-3, NFR-1). |
| 3 | Serialize the entire captured event (tags, extra, request context, breadcrumbs, message, exception) to a string and search for the request body. | Request body contents (`"note":"secret"`) are ABSENT (FR-4). |
| 4 | Search the serialized event for the `Authorization` header value and the cookie/session values. | The `Bearer ...` token and `session=abc123`/`.AspNetCore.Session` values are ABSENT (FR-4). |
| 5 | **Negative arm:** search the serialized event for the national-ID sentinel `199012345678` (present in body AND query string). | The string does NOT appear anywhere in the event — not in request context, extra, tags, or breadcrumbs. |
| 6 | Search the serialized event for the email sentinel `alice@acme.test`. | ABSENT everywhere in the event. |
| 7 | Confirm the event still carries the exception + stack trace + `tenant_id`/`tenant_subdomain` (scrub removes PII, not attribution). | Stack trace and tenant tags remain present (scrub is surgical, not a drop). |

## 6. Postconditions
- The captured event is safe to store in GlitchTip: it identifies the error and the tenant but contains no
  request body, credentials, cookies, or PII sentinels. Nothing regulated left the process boundary.

## 7. Test Category Tags
- [x] Happy path (scrub succeeds on a normal captured event)
- [x] Negative test (national-ID / email sentinels MUST NOT appear — asserted absent)
- [ ] Boundary test
- [x] Security test (PII exfiltration prevention — the ADR's hard condition; input carries token/cookie/PII)
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Intended binding:** an xUnit arm `[Trait("TC", "TC-PLT-009")]` that fires the throwing endpoint with a
  PII-laden request through the real `BeforeSend`/`SendDefaultPii=false` pipeline against a capture transport,
  then asserts BOTH the presence of the scrub targets on the inbound request AND their absence in the outgoing
  event (string-search the full serialized event, so a future scrub regression that leaks a new field goes red).
- **Status:** `draft` — SDK layer unwired; forward-looking. Flips to `automated` when the arm lands. This is the
  highest-priority TC of the story: a `pass` here must be a real run, never asserted from the spec.
