---
name: sentry-esm-inject-seam
description: "@sentry/angular ESM exports are frozen/non-writable — spyOn(Sentry,'init') throws; expose an injectable SDK seam to unit-test wiring"
metadata:
  type: feedback
---

`@sentry/angular` ships as a frozen ESM namespace: `spyOn(Sentry, 'init')` /
`spyOn(Sentry, 'setTag')` throw `<spyOn> : init is not declared writable or has
no setter` at test run (build/pure-fn tests still pass, so it only surfaces when
you actually run the spec).

**Why:** same class of problem as [[signalr-hub-client-pattern]] — ESM live
bindings aren't reassignable, so Jasmine can't install a spy on the imported name.

**How to apply:** don't try to spy the namespace. Give the module a tiny
injectable seam and default it to the real SDK:
`export interface SentryApi { init; setTag }` +
`initSentry(dsn = env.sentryDsn, sdk: SentryApi = realSentry)`. Production callers
(main.ts / app.config) use the default; the spec passes
`jasmine.createSpyObj<SentryApi>('SentryApi', ['init','setTag'])`. Pure helpers
like the `beforeSend` scrub need no seam — test them directly. Used in
`core/monitoring/sentry.ts` for US-PLT-006 AC-6.

DSN inert-when-blank pattern: `initSentry` returns `false` and never calls
`sdk.init` when the DSN is empty (AC-3 parity); a module-level `sentryEnabled`
flag keeps `setSentryTenant` a no-op too, so blank DSN = zero SDK activity.
