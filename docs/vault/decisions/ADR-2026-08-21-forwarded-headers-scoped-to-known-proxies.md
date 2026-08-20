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
`if (ctx.Request.IsHttps)` HSTS branch is **dead code** behind the proxy. Filed as [[BUG-308]].

The blast radius is wider than HSTS. Every `Request.Scheme` / `IsHttps` read has the same blind
spot — password-reset links, invite links, OAuth redirect URIs, `RequireHttpsMetadata`. They all
believe they are on `http` behind the proxy.

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

## Consequences

- `Request.Scheme` / `IsHttps` become correct behind the proxy, fixing HSTS **and** every absolute-URL
  generator at once.
- Proxy networks become **configuration**, so a topology change is a config edit, not a code change.
- New failure mode to watch: if the known-network config is wrong, the app silently reverts to
  believing it is on `http`. The HSTS-over-HTTPS test arm is the canary.

Related: [[hrm-fe-be-contract-drift]] (same "two descriptions of one truth" class), [[read-the-running-log]].
