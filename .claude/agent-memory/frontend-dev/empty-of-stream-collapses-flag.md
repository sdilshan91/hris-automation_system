---
name: empty-of-stream-collapses-flag
description: a component method that flips a busy flag true then subscribes to a poll stream sees the flag go false instantly if the stream spy returns of()
metadata:
  type: feedback
---

When a component sets a busy flag (`sending`/`generating`) true, then subscribes to a
polling stream whose `complete` handler sets the flag back to false, a test that stubs
the stream with an empty `of()` will see the flag as **false** immediately — `of()`
completes synchronously, firing `complete`.

**Why:** the streamX poll observables (PayslipEmailService.streamDistributionStatus,
PayslipService.streamGenerationStatus, PayrollRunService.streamProgress) all clear the
busy flag in `complete:`. An empty `of()` has no emissions and completes at once.

**How to apply:** in a spec asserting the busy flag stays true after dispatch, stub the
stream with `new Subject<T>().asObservable()` (stays open) instead of `of()`. Use `of()`
only when you WANT the completion path (e.g. asserting the success toast / flag-cleared).
Pairs with [[signal-async-dom-detectchanges]] — also call `fixture.detectChanges()` after
a signal update before asserting dialog/DOM text.
