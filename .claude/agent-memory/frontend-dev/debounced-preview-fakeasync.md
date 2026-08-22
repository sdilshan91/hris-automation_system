---
name: debounced-preview-fakeasync
description: A component method that fires a debounced (setTimeout) HTTP call leaks a pending request through flush() in fakeAsync — tick the debounce + match/flush it before httpMock.verify()
metadata:
  type: feedback
---

When a component action (e.g. US-NTF-002 `insertPlaceholder` → `schedulePreview`)
sets a `setTimeout`-based debounce that ends in an HTTP POST, a fakeAsync test that
ends with `flush()` will advance past the debounce, fire the POST, and then
`afterEach httpMock.verify()` fails with "Expected no open requests, found 1".

**Why:** `flush()` drains ALL pending macrotasks, including the debounce timer, so
the HTTP call you didn't expect actually goes out.

**How to apply:** after triggering such an action, explicitly `tick(<debounceMs>)`
then `httpMock.match(url).forEach(r => r.flush(...))` (a helper) BEFORE the final
`flush()`. Use `match` not `expectOne` since multiple debounced calls can coalesce.
Also: a fakeAsync spec whose only assertion is `httpMock.expectNone(...)` triggers
Jasmine's "no expectations" warning — add a real `expect(...)` (e.g. modal still
open) so the spec has an assertion. See [[rich-text-editor-no-dep]] for the reused
contenteditable RTE the editor wraps.
