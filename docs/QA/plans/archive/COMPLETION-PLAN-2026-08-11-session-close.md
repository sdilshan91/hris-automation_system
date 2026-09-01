# ARCHIVED SNAPSHOT — Session-close snapshot, 2026-08-11.

> Split out of [`../COMPLETION-PLAN.md`](../COMPLETION-PLAN.md) on **2026-09-01**, when the plan was
> audited and rebuilt. It carried five overlapping sections that each claimed to be 'the queue';
> this is one of them, preserved verbatim as history.
>
> **Not current. Do not execute from this file.** The live execution lane is
> [`../GAP-CLOSURE-QUEUE.md`](../GAP-CLOSURE-QUEUE.md); the current backlog is
> [`../COMPLETION-PLAN.md`](../COMPLETION-PLAN.md).

---

## 📍 Session close 2026-08-11 — Phases 0-5b done; 5c is the only queue item left
>
> **PRs #481-#501 merged to `test/local-subdomains`. Every P0 in the register is closed.** Landed: G5 ledger ·
> G1 structural (generated TS models + EF-filter coverage guard) · 8 G2 module fixes · GAP-018 rate limiter ·
> GAP-024 job log attribution · GAP-035 `user_id`/`trace_id` · **GAP-001 privileged-default inversion (last P0)** ·
> GAP-007 semgrep now BLOCKING · GAP-034 E2E in CI · ISSUE-363 permissions · GAP-036 dev key out of committed settings.
>
> **★ Read the register as a list of QUESTIONS, not instructions. Five of eight prescriptions executed this
> session were wrong as written, and following them literally would have shipped something broken:**
>
> | item | prescribed | what was true |
> |---|---|---|
> | GAP-024 | an `IServerFilter` that populates `ITenantContext` | **impossible** — 42 of 62 jobs create their own DI scope, so the filter sets an instance the job never reads. Would have looked like a working isolation control and enforced nothing (ISSUE-375). |
> | GAP-035 | `Serilog.Enrichers.Span` | **emits nothing** — with OTel dormant (the default) nothing registers an `ActivityListener`, so `Activity.Current` is null on every request. |
> | GAP-007 | annotate 270 sites, then flip semgrep | **premise expired** — RLS backstops them all; 265 annotations would have been write-only noise (finding S-2's own antipattern). |
> | ISSUE-363 | add all 17 permissions to the catalog | **15 were FE typos for permissions that already existed.** Would have created two overlapping authorization vocabularies — roles granting one spelling, guards checking the other, failing silently. |
> | GAP-034 | shared Playwright `storageState` | **impossible** — refresh tokens are single-use with rotation + reuse-detection (proved: 200 then 401). |
>
> Plus **GAP-036, sized "S"**, was load-bearing in three unlisted places (bare `dotnet run`, design-time
> `dotnet ef`, and the `migrations` CI job, which had been passing only because a live key sat in a committed file).
> **Measure before building. Every time it was measured, the scope changed.**
>
> **★ The most expensive lesson, and it applies directly to 5c:** GAP-001 passed **5385 green tests, mutation
> verification, and a live HTTP probe** — then silently killed tenant usage metering for 886 ticks behind a
> caught exception ([[BUG-302b]]). Found only by reading the running stack's log. **After any change to runtime
> behaviour, read the log as a separate check from running the suite.** A caught-and-logged failure is invisible
> to a suite that only asserts on outcomes it already expects.
>
> **Next: GAP-019a (billing-ops operator surface) is the only remaining queue item, and it is deliberately NOT
> started.** It is the one piece of genuinely new feature work left — seven distinct surfaces (revenue dashboard,
> credits/invoices/refunds, trial extension, platform staff, maintenance mode, broadcasts, GDPR intake) — and the
> least specified thing in the register. **Begin with a scoping pass against `src/`, not with implementation**, for
> exactly the reason the table above gives. The rest of the open rows (GAP-012/019b/019c/020-033/037-040) are
> unchanged and independently sized.
>
> **Newly filed and unfixed:** ISSUE-372, ISSUE-373, ISSUE-374, **ISSUE-375** (§9.4-3 documents an unimplementable
> filter — do not "fix" it by making the filter set tenant context), **ISSUE-376** (client-cancelled request logged
> as an unhandled 500; pollutes GlitchTip). Still open from before: the `lint` script (`ng lint` with no ESLint
> config), `@axe-core/playwright` with zero `AxeBuilder` usages, a real secrets vault, and the US-PLT-002 AC text.
