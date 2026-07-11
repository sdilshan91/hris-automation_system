---
id: US-PLT-001
module: Platform
priority: Must Have
persona: Frontend Platform / All Users
status: draft
created: 2026-06-15
sprint: backlog
acceptance_criteria_count: 5
---

# US-PLT-001: Global API Response Envelope Unwrapping (Frontend)

## 1. Description
**As a** frontend developer (and, transitively, every end user),
**I want** the Angular HTTP layer to transparently unwrap the backend's `ApiResponse<T>` envelope so services receive the bare `T` payload,
**So that** data binds correctly against the real running API instead of only against mocked unit tests, and we eliminate a class of latent FE↔BE contract bugs.

## 2. Background / Problem Statement
The ASP.NET Core API returns nearly every successful response as `ApiResponse<T>.Ok(...)` — a JSON envelope `{ "success": true, "data": <T>, "message": ..., "errors": ... }`. However, **every** Angular service currently types and consumes the **bare** payload (e.g. `http.get<ILocation[]>(url)` with no `.data` access), and every spec `flush()`es a bare array/object. There is **no** response-unwrapping HTTP interceptor (`core/interceptors/` contains only `error` and `tenant`).

Because both sides are only verified via mocked unit tests, the mismatch is invisible to the build/test gate but means dropdowns and most data binding would fail against a real backend (they receive `{success, data}` where an array/object is expected). This was discovered while wiring US-REC-001 vacancy-form master-data lookups. It is a pre-existing, cross-cutting defect, not specific to any feature.

A second, related mismatch: paginated endpoints (e.g. `GET /api/v1/tenant/employees`) return an `EmployeeListResult` / paginated envelope, not a bare array — consumers disagree on that shape too.

## 3. Acceptance Criteria (IEEE 830 S3.2)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The API returns `{ success: true, data: <T> }` | Any Angular service issues an HTTP request through `HttpClient` | The subscriber receives the bare `<T>`, with the envelope unwrapped by a single functional HTTP interceptor |
| AC-2 | The API returns `{ success: false, errors: [...] }` or a non-2xx status | A service call fails | The error interceptor surfaces the envelope's `message`/`errors` (no regression to existing error handling) |
| AC-3 | A response body is NOT enveloped (e.g. a third-party URL, a blob/file download, or a `204 No Content`) | A service call is made | The interceptor passes the body through unchanged (no double-unwrap, no crash on null/blob) |
| AC-4 | The new interceptor is registered | The full existing spec suite runs | Specs are updated so mocks `flush()` the enveloped shape `{ success, data }` and assertions still pass; no test is weakened or skipped |
| AC-5 | A paginated endpoint returns a page envelope | A service consumes it | A documented, consistent pagination contract is followed across services (page shape unwrapped consistently) |

## 4. Functional Requirements
- FR-1: Add a functional `HttpInterceptorFn` (e.g. `apiEnvelopeInterceptor`) registered in `core/interceptors/index.ts` that, for JSON responses matching the `ApiResponse<T>` shape, returns `response.clone({ body: body.data })`.
- FR-2: The interceptor SHALL be a no-op for: non-JSON bodies (blob/text/file), `204 No Content`, bodies that do not have the envelope's discriminating keys, and absolute non-API URLs.
- FR-3: Ordering SHALL be correct relative to `tenant` and `error` interceptors (unwrap success bodies; let `error` handle non-2xx + `success:false`).
- FR-4: Provide a single shared `ApiResponse<T>` / `PaginatedResponse<T>` TypeScript type and migrate services to rely on the unwrapped shape; remove now-redundant per-service envelope assumptions.
- FR-5: Update all affected `*.spec.ts` to mock the enveloped wire shape (this aligns mocks with reality — it is NOT weakening tests).

## 5. Non-Functional Requirements
- NFR-1: Zero runtime overhead beyond an O(1) shape check + one `clone` per response.
- NFR-2: No change to the public method signatures of existing services (subscribers still get `T`).
- NFR-3: The change SHALL keep the full `ng test` suite green and `ng build` clean.

## 6. Out of Scope
- Backend changes (the envelope stays; this is purely a frontend reconciliation).
- Introducing a global state/cache layer.

## 7. Test Hints
- Verify a service that previously flushed a bare array now flushes `{ success: true, data: [...] }` and the subscriber still receives the array.
- Verify a `204` and a blob download are passed through untouched.
- Verify a `success: false` body routes to the existing error path with its message intact.
- Grep for services typed as `http.get<X[]>` that actually receive envelopes and confirm each is covered.

## 8. Notes
- See the loop's running note on this defect. Reinforces the contract-first rule: contract-first prompts pinned paths + DTO field names but NOT the envelope, so the envelope mismatch slipped through every module.
