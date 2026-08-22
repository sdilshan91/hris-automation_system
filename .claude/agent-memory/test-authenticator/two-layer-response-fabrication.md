---
name: two-layer-response-fabrication
description: A service spec that flushes an invented body PLUS a component spec that mocks that service = the endpoint's real response DTO is never exercised anywhere; check the controller's ProducesResponseType before trusting either
metadata:
  type: feedback
---

When a FE service spec uses `HttpTestingController` + `req.flush(fixture)` and the component spec
`jasmine.createSpyObj`s that same service, **neither layer ever sees the endpoint's real response type**.
Coverage looks complete; the wire contract is untested at both layers.

**Why:** Found on `fix/B4-offboarding-completion-gate` (2026-08-23). `OffboardingService.complete()` is typed
`http.post<OffboardingInstanceWire>(...).pipe(map(mapOffboardingInstance))`, but
`POST /api/v1/offboarding/{id}/complete` returns `ApiResponse<CompleteOffboardingResultDto>`
(`{completed, instance, pendingItems, finalSettlementRef}`) — see `OffboardingController.cs` `ProducesResponseType`.
Every mapped field is `undefined` at runtime. The service spec flushed a fabricated *instance* body and the
dashboard spec's spy returned `of({...cleared(), status:'Completed'})` — a value the service can never emit.
The same change had migrated fixtures to `Schema<'…Dto'>` generated types and *still* missed it, because it
pointed the **wrong schema** at that one endpoint; the right schema would have been a compile error.

**How to apply:** For every FE method under audit, open the controller and read `ProducesResponseType` /
the returned `ApiResponse<T>` generic. Then check the fixture is typed `Schema<'<that exact T>'>`. Also check
the **error** shape separately — `apiEnvelopeInterceptor` unwraps only 2xx, so a 409 body stays enveloped
(`err.error.data.pendingItems`), and hand-written `I…Error { … }` interfaces in `*.models.ts` are usually
invented. Related: [[absence-arm-vacuity]], [[be-unit-test-isolation]].
