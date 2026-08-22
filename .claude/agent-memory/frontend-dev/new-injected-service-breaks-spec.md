---
name: new-injected-service-breaks-spec
description: adding a new inject()ed service to an existing component breaks EVERY TestBed setup in its spec; update all of them (incl. private setupX helpers)
metadata:
  type: feedback
---

When you extend an existing component by `inject()`ing new services (and calling them
in `ngOnInit`/`load`), EVERY `TestBed.configureTestingModule` block in that component's
spec must add a provider/spy for the new service — not just the top-level `setup()`.
Specs often have MULTIPLE private setup helpers (e.g. `setupProcessing()` for a
different status path); miss one and that whole describe block throws NullInjectorError.

**Why:** payroll run-detail (US-PAY-008) added `PayrollApprovalService` + `AuthService`
+ `ToastrService`; `load()` calls `getApprovalHistory`/`getApprovalSummary`, so the
pre-existing `setupProcessing` helper (US-PAY-003) also needed the new spies or its
3 tests would fail even though they predate the change. Pattern: factor the spy
creation into small `makeApproval()`/`makeAuth()` factories and call them from BOTH
setup helpers so they can't drift.

**How to apply:** after adding a constructor/inject dependency to a component that has
an existing spec, grep the spec for every `configureTestingModule` and add the provider
to each; then run the FULL `ng test` (not just `--include` the one file) to catch
sibling regressions. Spy on methods the component calls on init (history/summary here)
with default `of(...)` returns so unrelated tests don't error. See
[[routerlink-breaks-sibling-spec]] for the same "edit forces a spec-wide fix" shape.
