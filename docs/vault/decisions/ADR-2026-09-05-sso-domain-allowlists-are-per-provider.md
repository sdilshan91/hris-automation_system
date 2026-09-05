---
type: decision
status: accepted
created: 2026-09-05
deciders: product owner + Claude (F1 premise verification)
tags: [auth, sso, tenant-isolation, security, adr-lite]
---

# SSO domain allow-lists are PER-PROVIDER

## Context

`GAP-019b` (Google sign-in) was scoped as reuse of the existing OIDC pipeline. Verifying that premise
surfaced a security-relevant design question that no document answered.

`Tenant.AllowedEmailDomains` has **no provider dimension**, and
`SsoIsolationGuard.Evaluate(settings, tid, email, emailVerified)` admits on `tidAllowed || domainAllowed`.
Today that means "trust these domains, **via Entra**". Route a second provider through the same guard
and **every tenant that allow-listed `acme.com` for Entra would silently also admit any Google account
at `acme.com`** — with no admin action and no notice.

It is sharper for Google specifically: `tidAllowed` can never be true, because Google issues no `tid`.
Admission would rest **entirely** on the domain rule, i.e. entirely on `email_verified`. The guard's own
comment relies on a backstop — *"the tid rule is unaffected — it is bound to the issuing directory and
cannot be self-asserted"* — that **has no Google equivalent**. A secondary rule becomes a single point
of failure.

## Decision

**Domain trust is declared per provider.** Enabling Google grants nothing until an administrator adds
domains for Google specifically.

## Alternatives considered

- **Shared domain list + a per-provider master switch** — cheaper (no migration), and the admin does
  consent by flipping the switch. Rejected: they consent to *"Google on"*, not to *"every domain I
  listed for Entra now also admits Google"*. The widening is retroactive and invisible, and §3.4
  designates cross-tenant leakage zero-tolerance.
- **Defer `F1` entirely** — defensible on cost; the item is M–L, not M, and needs a BA story that does
  not exist. Rejected as the *default*: the decision is cheap to record now and expensive to rediscover
  later, and recording it does not commit to building.

## Consequences

- Costs a schema change + migration, validator work (`TenantAuthSettingsValidator`) and a Tenant Admin
  UI change (`sso-settings.component.ts`). Accepted, because no existing tenant's trust may widen
  without an explicit act.
- `F1` remains blocked on **authoring a BA story**, not on this decision. Both `US-AUTH-001:81` and
  `CR-AUTH-001:164-165` explicitly defer non-Microsoft OIDC.
- ~90% confidence the widening is real as described; it is contingent on Google routing through this
  guard, which is the stated reuse plan.

## Links
- Related code: `SsoIsolationGuard.cs`, `Tenant.cs` (`AllowedEmailDomains`), `TenantAuthSettingsValidator.cs`
- Related findings: `DECISION-480`, `ISSUE-479`
