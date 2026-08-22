---
date: 2026-08-21
status: accepted
tags: [security, deployment, tls, multi-tenant]
related: [BUG-308, GAP-033a]
---

# Trust `X-Forwarded-Proto`, but only from known proxies

## Context

TLS terminates at the reverse-proxy nginx (`docker-compose.tls.yml`), which forwards to
`backend:5000` over **plain HTTP** with `X-Forwarded-Proto: https`. `Program.cs` registers no
`ForwardedHeaders` middleware, so `ctx.Request.IsHttps` is `false` inside the API in the only
deployment that actually has TLS.

Found by `@integration-enforcer` while auditing the GAP-033a security-headers change: the
`if (ctx.Request.IsHttps)` HSTS branch is **dead code** behind the proxy. Filed as [[TEST-FINDINGS#BUG-308|BUG-308]].

The blast radius is wider than HSTS — though **not in the way this ADR first claimed.** The original
draft asserted that password-reset links, invite links and OAuth redirect URIs shared the blind spot.
**That was wrong, and verified wrong:** those are built from `Platform:BaseDomain` /
`Platform:FrontendBaseUrl` with a hardcoded `https://`, never from `Request.Scheme`.

The genuinely affected readers, enumerated rather than assumed:

- **Four scheme-derived branding URLs** — `AuthController.ToServableLogos` (:339),
  `TenantContextController` (:41), `TenantSettingsController` (:255, :295). Behind TLS these emitted
  `http://` logo URLs on an `https://` page, which browsers block as mixed content. A real,
  user-visible bug that nobody had connected to this cause.
- **Rate-limit partitioning** (`ResolveClientIp`) — every request through the proxy previously
  presented the same address, so all tenants shared **one** bucket and a single noisy client could
  exhaust the limit for everyone.
- **The whole audit/security IP trail** — `AuditInterceptor`, `AuditCaptureInterceptor`,
  `AuditLogService`, `PayrollAuditLogger`, plus the controllers stamping client IP on login,
  attendance, review sign-off, payroll approval and portal-token issuance. These now store the real
  client instead of the proxy. More correct, but the *meaning* of the stored column changes on the
  day a proxy is configured.

**Lesson worth keeping:** the first draft named plausible-sounding consumers from memory instead of
grepping for `Request.Scheme`. Three of the four named were wrong and the largest affected surface
(the audit trail) was missed entirely. Enumerate, don't recall.

## Decision

Add `UseForwardedHeaders` **scoped to the known proxy network**, with an integration test proving a
spoofed header from an untrusted source is ignored.

## Why not the cheaper options

- **Read `X-Forwarded-Proto` inside the header middleware only.** Lands fastest and fixes HSTS, but
  leaves the reset-link and OAuth-redirect scheme bugs live, and creates a *second, private* notion
  of "is this HTTPS" that will drift from `Request.IsHttps`. Two answers to one question is the same
  systemic failure (S-1) this codebase already pays for elsewhere.
- **Delete the API's HSTS branch and delegate to the edge.** Honest and zero-risk *today*, because
  the SPA nginx emits HSTS for the same origin and HSTS is host-level. But it silently assumes the
  API is forever co-hosted with the SPA — the day the API moves to `api.example.com` it has no HSTS
  and nothing fails loudly.

## The part that must not be skipped

An **unscoped** `ForwardedHeaders` middleware trusts a client-supplied `X-Forwarded-Proto`. That
lets any caller forge `Request.IsHttps == true`, which is a spoofing vector, not a convenience.
`KnownNetworks`/`KnownProxies` must be cleared and then populated from configuration, and the
"spoofed header from a non-proxy IP is ignored" case must be an **asserted test arm**, not a comment.

This is why the fix was NOT folded into the GAP-033a headers PR: it is a pipeline-wide change with
its own test surface, and burying it inside a header PR would have hidden exactly the arm that
matters.

## What implementing it actually taught us (added 2026-08-21, post-implementation)

**The "fail-safe default" I first wrote was fail-OPEN.** The first cut put empty
`KnownNetworks`/`KnownProxies` in `appsettings.json` and documented empty as "trusts nobody, strips
the headers, identical to today". That is the opposite of the truth.

ASP.NET Core decides whether to run the known-proxy check with
`KnownNetworks.Count > 0 || KnownProxies.Count > 0`. With **both lists empty there is no check at
all**, and `X-Forwarded-*` is honoured from every caller. Clearing both lists — the obvious way to
write "trust nobody" — is precisely how you write "trust the entire internet".

Caught by running it, not reading it: with both lists emptied, a forged `X-Forwarded-Proto: https`
from `203.0.113.9` produced a `Strict-Transport-Security` response header. The spoof succeeded.

**Correction:** when nothing is configured the middleware is **not registered at all**
(`ProxyTrustOptions.Build` returns `null`). That is the only construction where "unconfigured"
genuinely equals "unchanged behaviour".

**Second trap, same family.** `IPNetwork.TryParse` **accepts** host bits and silently normalises:
`10.1.2.3/16` parses happily to `10.1.0.0/16`. On a *trust list* that silence is dangerous — someone
meaning a single proxy host who typos the prefix length would widen trust to 65k addresses with no
warning. Now rejected explicitly.

**Third, a harness trap worth remembering.** `TestServer` never populates a socket peer address, and
ForwardedHeadersMiddleware only runs its trust check when it *has* one. A spoof-rejection test
written against the bare harness passes while proving nothing — it is green because the check never
ran. Same family as this repo's InMemory-masks-Postgres class. The test factory now supplies the
peer address explicitly.

**The through-line:** all three are cases where *absence of configuration* or *absence of input*
reads as safe and behaves as permissive. Worth checking for that shape in any other security control
that has an "unconfigured" state.

## Consequences

- `Request.Scheme` / `IsHttps` become correct behind the proxy, fixing HSTS **and** every absolute-URL
  generator at once.
- Proxy networks become **configuration**, so a topology change is a config edit, not a code change.
- New failure mode to watch: if the known-network config is wrong, the app silently reverts to
  believing it is on `http`. The HSTS-over-HTTPS test arm is the canary.

Related: `memory:hrm-fe-be-contract-drift` (same "two descriptions of one truth" class), `memory:read-the-running-log`.
