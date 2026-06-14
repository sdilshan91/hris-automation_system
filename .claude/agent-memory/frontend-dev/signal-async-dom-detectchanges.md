---
name: signal-async-dom-detectchanges
description: In fakeAsync specs, after tick() flushes an async observable that sets a signal, you MUST call fixture.detectChanges() before asserting on DOM textContent — signal value asserts pass without it, DOM asserts do not
metadata:
  type: feedback
---

When a component method subscribes to an async observable (e.g. `clockOut()`)
and writes the result into a **signal** that the template renders, a `fakeAsync`
spec must call `fixture.detectChanges()` **after** `tick()` before asserting on
rendered DOM (`nativeElement.textContent`).

- Asserting on the **signal getter** (`component.summary()`, `component.totalHoursLabel()`)
  passes immediately after `tick()` — the signal is already updated.
- Asserting on the **DOM** fails without a manual `detectChanges()` — `tick()`
  flushes the microtask/observable but does not run change detection; the template
  still shows the pre-call render.

**Why:** got 3 spec failures on the US-ATT-002 clock-out summary — signal asserts
green, `textContent` asserts red ("expected '...Clock Out' to contain '7h 45m'"),
because the summary card hadn't been re-rendered.

**How to apply:** in fakeAsync, the order is `component.onX(); tick(); fixture.detectChanges();`
then DOM asserts. Pairs with [[geolocation-test-mocking]] (call onX WITHOUT await, tick() to flush).
