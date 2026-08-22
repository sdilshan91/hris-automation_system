---
name: fake-translate-loader-no-interpolation
description: provideTranslateService() fake loader in specs returns the bare key WITHOUT applying {{param}} interpolation — never assert interpolated values in DOM text
metadata:
  type: feedback
---

In Jasmine specs, `provideTranslateService()` (no real http loader) returns the
raw translation KEY for every pipe, and does NOT apply `| translate: { count: n }`
interpolation. So a template like `{{ 'x.subtitle' | translate: { count: total() } }}`
renders the literal string `x.subtitle` in the DOM — the count never appears.

**Why:** the fake loader is presence-only (returns the key). Three audit-log
(US-ADM-008) tests initially failed asserting `textContent` contained `'120'`/`'42'`
because the interpolated count was dropped.

**How to apply:** when a value is shown ONLY via an interpolated translate param,
assert the bound signal/input on the component (`component.total()`,
`component.recordCount()`) or check the child element exists
(`querySelector('app-...')`) — do NOT assert the interpolated value in DOM text.
Plain (non-interpolated) values bound with `{{ signal() }}` outside a translate
pipe DO render and are safe to assert in text. Related: [[signal-async-dom-detectchanges]].
