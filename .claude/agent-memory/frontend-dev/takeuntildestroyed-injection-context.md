---
name: takeuntildestroyed-injection-context
description: bare takeUntilDestroyed() throws NG0203 inside ngOnInit; pass an injected DestroyRef when wiring a debounced Subject subscription outside the constructor
metadata:
  type: feedback
---

`takeUntilDestroyed()` with NO argument only works in an injection context
(constructor, field initializer, factory). Calling it inside `ngOnInit` (or any
method) throws `NG0203: takeUntilDestroyed() can only be used within an injection
context`.

**Why:** the zero-arg overload reads the ambient `DestroyRef` via `inject()`,
which is unavailable once construction finishes.

**How to apply:** when you set up a debounced `Subject` pipeline in `ngOnInit`
(common pattern: per-toggle auto-save buffer), inject `DestroyRef` as a field and
pass it explicitly — `takeUntilDestroyed(this.destroyRef)`. Caught in US-NTF-003
notification-preferences; the spec's first run surfaced NG0203 on every test that
triggered ngOnInit. Build doesn't catch it — only the Karma run does.
