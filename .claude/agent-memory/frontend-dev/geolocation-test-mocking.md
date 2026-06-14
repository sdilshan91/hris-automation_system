---
name: geolocation-test-mocking
description: Mocking navigator.geolocation in Karma/headless Chrome requires Object.defineProperty (getter-only); and never mix async+fakeAsync — the geolocation Promise resolves on a microtask flushed by tick(), not await
metadata:
  type: feedback
---

When unit-testing a component that reads `navigator.geolocation` (e.g. the
attendance clock-in `ClockInComponent`):

**1. `navigator.geolocation` is a getter-only property** in headless Chrome.
`(navigator as any).geolocation = {...}` throws
`TypeError: Cannot set property geolocation ... which has only a getter`.
Mock it with `Object.defineProperty(navigator, 'geolocation', { value, configurable: true, writable: true })`
and restore the original descriptor in `afterEach` (save it via
`Object.getOwnPropertyDescriptor(navigator, 'geolocation')`).

**2. Do NOT mix `async` and `fakeAsync` in the same spec.** The component wraps
`getCurrentPosition` in a `Promise` and `await`s it inside `onClockIn()`. Under
`fakeAsync`, that promise resolves on a **microtask**, which is flushed by
`tick()` / `flushMicrotasks()` — NOT by a real `await`. So:
  - make the `setup()` helper synchronous (status comes from `of(...)`, resolved
    inline by `fixture.detectChanges()` -> ngOnInit),
  - call `component.onClockIn()` WITHOUT awaiting, then `tick()` to flush both the
    geolocation promise and the `clockIn()` observable.
A geolocation success/error callback that fires synchronously still only settles
the promise on the next microtask, so one bare `tick()` is enough.

**Why:** got both wrong on the first US-ATT-001 pass — 7 failures: getter
assignment crash + "code should be running in the fakeAsync zone" from awaiting
inside fakeAsync.

**How to apply:** reuse this pattern for any clock-out / mobile-web attendance
spec and anything else that touches `navigator.geolocation`. Related:
[[leave-apply-form-spec-gotcha]] (other attendance/leave spec ordering traps).
