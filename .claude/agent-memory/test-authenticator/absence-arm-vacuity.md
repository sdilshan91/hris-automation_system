---
name: absence-arm-vacuity
description: A negative/rejection arm that asserts an ABSENCE goes vacuously green when the whole feature is unregistered/short-circuited — the positive arm is the real guardian
metadata:
  type: feedback
---

A negative arm that asserts the **absence** of an observable (e.g. `HSTS header absent`, `403`, `no row`) passes in **two** distinct worlds: (a) the control correctly rejected the input, and (b) the feature/middleware never ran at all. It cannot distinguish them by itself.

**Why:** Seen concretely in BUG-308 (`ForwardedHeadersTrustTests`). When `Proxy:KnownNetworks`/`KnownProxies` are both emptied, `ProxyTrustOptions.Build` returns null → `UseForwardedHeaders` is skipped → no scheme promotion → no HSTS. So `SpoofedForwardedProto_FromUntrustedPeer_IsIgnored` (asserts HSTS absent) stays GREEN **for the wrong reason** — while the positive arm `ForwardedProto_FromKnownProxy_IsHonoured` (asserts HSTS present) is what actually goes RED. The arm the author labelled "THE ARM THAT MATTERS" is the one that silently becomes vacuous.

**How to apply:** When auditing a rejection/negative test, always ask "does this also pass if the feature is deleted/unregistered?" If yes, it is not self-sufficient — confirm a **positive** arm exists that would go red on unregistration, and identify which arm is the true guardian. Prefer a direct observable (echoed `Request.Scheme`/`IsHttps`, RemoteIpAddress) over an indirect one (HSTS presence) so the negative arm can distinguish "scoped correctly" from "never ran." Related: [[be-unit-test-isolation]].
