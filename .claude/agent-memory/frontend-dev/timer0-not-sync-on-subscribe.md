---
name: timer0-not-sync-on-subscribe
description: timer(0,n) does NOT emit synchronously on subscribe; for an OnPush initial load a plain detectChanges() spec must see, call the fetch directly + use interval(n) for polling
metadata:
  type: feedback
---

`timer(0, intervalMs)` schedules its FIRST emission on a macrotask (setTimeout 0),
so it does NOT fire synchronously when you subscribe. A component that does its
initial data load via `timer(0,n).pipe(switchMap(fetch))` will still be empty after
a plain `fixture.detectChanges()` in a spec that mocks the service with `of(resp)` —
the `of()` payload hasn't been switched-in yet, so signals stay empty and DOM
assertions fail ("Expected 0 to be 6").

**Why:** seen building the US-RPT-005 dashboard. The auto-refresh poll was
`timer(0, 5min)`; every "renders cards / navigates / maps chart" spec saw an empty
`widgets()` because the first tick is async.

**How to apply:** split the two concerns — do the **initial load directly**
(`this.load(false)` in the constructor, a normal `service.getWidgets().subscribe`)
so `of()` resolves synchronously for plain-`detectChanges()` specs, and use
**`interval(n)`** (NOT `timer(0,n)`) for the recurring poll, since `interval` only
fires on each tick anyway. Keep `takeUntilDestroyed(destroyRef)` on the poll.
Contrast [[timer-polling-fakeasync-tick0]] which is the fakeAsync `tick(0)` workaround
when you DO keep `timer(0,n)` and drive an httpMock.
