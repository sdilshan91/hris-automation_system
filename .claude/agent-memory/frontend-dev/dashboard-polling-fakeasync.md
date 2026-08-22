---
name: dashboard-polling-fakeasync
description: Test a 30s interval()-based polling dashboard (SignalR fallback) — assert initial + tick(30000) calls, then destroy()+tick to prove teardown
metadata:
  type: feedback
---

For US-ATT-010 the HR attendance dashboard polls KPIs + live board every 30s via rxjs `interval(30000).pipe(takeUntil(destroy$))` because SignalR is unavailable (story §10 polling fallback). To unit-test the polling cleanly:

- Mock the service to return `of(...)` (synchronous), wrap the test in `fakeAsync`.
- After `fixture.detectChanges()` assert each endpoint was called **once** (the eager initial `refresh(true)` in ngOnInit).
- `tick(30_000)` then assert called **twice**.
- `fixture.destroy()` then `tick(30_000)` again and assert the count **did not increase** — this proves `takeUntil(destroy$)` tears the interval down (no leaked polling).

**Why:** a leaked `interval` keeps firing HTTP after the component is gone; the destroy+tick assertion is the only thing that actually catches a missing `destroy$.next()`/`complete()` in ngOnDestroy.

**How to apply:** mirrors the [[signal-async-dom-detectchanges]] note for DOM, but here the assertions are on spy call counts, not DOM — no extra detectChanges needed since `of()` resolves synchronously inside `refresh()`. Manager scope toggle (scope='team', BR-4) is gated on `AuthService.hasRole('Manager')`, so mock AuthService with a `hasRole` spy.
