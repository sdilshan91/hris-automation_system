---
type: decision
date: 2026-07-08
status: accepted
tags: [governance, security, multi-tenancy, observability, saas]
---

# ADR — Data-governance & security posture for production multi-tenant SaaS

## Context

HRM is going to production **as a hosted SaaS**: we operate the platform and are therefore a
**data controller / processor for customer HR data** (names, salaries, national IDs, contact info —
sensitive PII by definition, often regulated). This changes the risk calculus for several tooling and
architecture decisions that were previously "wait / skip / decision-gate": the compliance driver they
were waiting for now exists. This ADR records the decisions triggered by that frame. It refines
[[TOOLING-ADOPTION-PLAN]] (Wave 4) and connects to the BUG-003 tenant-isolation theme.

## Decision 1 — Error tracking: self-hosted, not third-party SaaS

Adopt **self-hosted GlitchTip** (Sentry-API-compatible, open source) — **not** SaaS Sentry — with
**SDK-level PII scrubbing** (`beforeSend` strips request bodies / PII fields; `sendDefaultPii=false`).

**Why:** exception payloads routinely carry PII (request bodies, query params, stack frames). Sending
them to a third-party sub-processor would obligate a DPA, sub-processor disclosure to every tenant, and
a per-customer data-residency answer. Self-hosting keeps exception data inside our trust boundary; the
Sentry MCP then targets our own instance, giving `@browser-debugger` / `/fault-diagnosis` the exact
exception behind a failing TC without leaking data or grepping Serilog files.

**Consequence:** stand up GlitchTip (Docker) in our infra; wire the .NET + Angular SDKs with scrubbing;
point the Sentry MCP at it. Deferred until the current backend WIP settles.

## Decision 2 — Tenant isolation gets a second (database) enforcement layer

**Elevate Postgres Row-Level Security (RLS) from decision-gate → planned.** Today isolation is
**app-layer only** (EF global query filters + `TenantAccessGuardMiddleware`, post-BUG-003). For a
data-controller HR SaaS that is insufficient defense-in-depth: a single missed filter or a raw-SQL
path becomes a cross-tenant breach. RLS in the database catches what the app layer misses.

**Approach (when implemented):** `SET LOCAL app.current_tenant = :tid` inside the request transaction
(never `SET` — it leaks across pooled Npgsql connections); a dedicated **`BYPASSRLS` migrator role**
for EF migrations/seeding; `tenant_id` as the **leading** column of tenant-scoped indexes (else RLS
predicates are 1-2 orders of magnitude slower — verify with Dexter/HypoPG). This is weeks of work and
interacts with EF migrations + connection pooling — sequence it deliberately, not as a quick win.

**This is the single most important consequence of the SaaS decision.**

## Decision 3 — Reprioritized to compliance-required (were backlog/optional)

- **Custom Semgrep tenant-isolation rules** → **priority** (mechanize the BUG-003 class: flag
  `IgnoreQueryFilters()` outside resolution middleware, `BaseEntity` queries with no tenant predicate,
  raw SQL without `tenant_id`). Cheaper than RLS and a fast partial win.
- **Encryption at rest** (US-PLT-005, currently a stub) → **required** for PII at rest.
- **Complete audit logging** (BUG-082: interceptor audits only 3 entity types) → **required** — a
  regulated HR platform needs a queryable trail of *all* data changes.
- **Gitleaks → hard gate** once the historically-committed Postgres password is rotated + purged from
  history (currently advisory in [[TOOLING-ADOPTION-PLAN]]).
- **Trivy (SCA + image scanning) → adopt** — as the hosting provider we own the supply-chain risk.

## Decision 4 — Governance artifacts to track (flagged, out of scope for this ADR)

These are program-level, not code — recorded here so they don't fall through the cracks:
data-processing agreement (DPA) + sub-processor list; data residency; **retention & right-to-erasure**
(we already have `TenantDataDeletionService` — verify it fully purges + is exercised); tenant data
**export** (GDPR portability — a data-export surface exists); encrypted **backups + DR**; a documented
**incident-response / breach-notification** runbook; least-privilege DB roles.

## Status of what's decided vs deferred

- **Decided now:** self-hosted error tracking (GlitchTip); RLS is *planned* (not deferred-indefinitely);
  Trivy adopted; Gitleaks path to hard-gate defined; Semgrep tenant rules prioritized.
- **Deferred (execution) until backend WIP settles:** GlitchTip stand-up, RLS implementation, the
  build-touching analyzer/Semgrep work.
- **Not changed:** app-layer tenant isolation stays as-is (RLS is *additive*, not a replacement).
