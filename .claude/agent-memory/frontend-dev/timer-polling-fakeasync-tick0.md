---
name: timer-polling-fakeasync-tick0
description: testing a timer(0, interval)-based polling stream in fakeAsync — the first emission needs tick(0), it does NOT fire on subscribe
metadata:
  type: feedback
---

A `timer(0, intervalMs)`-based polling Observable (e.g. `PayrollRunService.streamProgress`:
`timer(0, 2000).pipe(switchMap(getProgress), takeWhile(active, true))`) does NOT fire its
first HTTP request synchronously on `subscribe()` inside fakeAsync.

**Why:** the `0` delay still schedules a macrotask; nothing flushes it until the virtual
clock advances. So `httpMock.expectOne(...)` immediately after subscribe finds "no matching
request" and fails. (Differs from a bare `interval(n)` test which has no 0-tick emission, and
from a subject-based stream which emits synchronously.)

**How to apply:** in the fakeAsync test, call `tick(0)` after subscribe and BEFORE the first
`httpMock.expectOne(...)`. Then `tick(intervalMs)` per subsequent poll. With `takeWhile(pred,
true)` (inclusive), the terminal snapshot IS emitted and the stream completes — assert
`completed === true` after flushing a terminal status, and `httpMock.expectNone(...)` after a
further `tick(intervalMs)` to prove no more polls fire. Related: [[dashboard-polling-fakeasync]].
