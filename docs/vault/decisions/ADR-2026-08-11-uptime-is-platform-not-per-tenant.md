---
type: decision
status: accepted
created: 2026-08-11
deciders: sdilshan91 (product owner), Claude (analysis)
---

# The availability target is PLATFORM uptime, not per-tenant uptime

## Context

Two places promise availability **measured per tenant**: §1.4 success criterion #4 (*"≥ 99.5% monthly,
measured per tenant"*) and §6.4 (*"Target ≥ 99.5% monthly uptime per tenant"*).

The system cannot measure that. `HealthProbeRecorderJob` records one platform-wide readiness probe per run
into `health_probe`, a table with **no `tenant_id`**, and `PlatformMonitoringService.cs:511` states plainly
that the resulting figure is a *platform* property returned identically for every tenant. The gap analysis
filed this as **GAP-019c** with the observation that matters most here: *"a requirement that is unmeasurable is
worse than one that is unmet."* An unmet requirement is visible; an unmeasurable one quietly invites a
fabricated number.

Worth recording, because it shows the existing code made the honest choice: before the probe history existed,
the dashboard's uptime field was **deliberately null** rather than computed from nothing —
TC-ADM-002-17 explicitly forbids fabricating the figure. This ADR extends that same honesty to the
requirement text.

## Decision

**The availability target is platform-wide.** §1.4 criterion #4 and §6.4 are amended to read *"≥ 99.5%
monthly platform uptime"*, dropping "per tenant". The probe, the `health_probe` table, and the dashboard
figure are already correct for that reading and need no change.

## Alternatives considered

- **Make per-tenant uptime genuinely measurable.** Scope the probe per tenant (synthetic per-tenant requests,
  a `tenant_id` on `health_probe`, per-tenant rollups). Rejected for now: it is real infrastructure —
  per-tenant synthetic probing, storage growth proportional to tenants × probe frequency, and a decision about
  what "tenant is down" even means when the platform is up and only that tenant's data is affected. No
  customer contract currently requires a per-tenant figure. `ApiCallCounter` gives partial substrate if this
  is revisited.
- **Leave both statements as they are.** Rejected. It is the option that produces a fabricated metric: the
  next person asked for per-tenant uptime finds a platform number already labelled per-tenant and ships it.

## Consequences

- **Easier:** GAP-019c's contradiction closes with a doc edit; the dashboard's existing figure becomes
  correctly labelled rather than quietly wrong.
- **Harder:** if a customer contract later demands per-tenant SLA reporting, this becomes new
  infrastructure — accepted knowingly.
- **Accepted:** the platform can be "up" by this metric while a single tenant is degraded (for example a
  tenant-specific data problem). That is a real limitation of a platform-level metric and is now stated rather
  than hidden behind a per-tenant label.
- **Follow-up, not blocking:** if per-tenant health is wanted as a *signal* rather than an SLA, the cheaper
  path is per-tenant error-rate alerting off the existing `tenant_id` log enrichment (GAP-035), not synthetic
  probes.

## Links
- Related code: `HRM.Api/Jobs/HealthProbeRecorderJob.cs` · `HRM.Infrastructure/Services/PlatformMonitoringService.cs:511`
- Related requirements: §1.4 success criterion #4 · §6.4 Availability
- Related gaps: GAP-019c (§33.3 platform reporting) · GAP-035 (log enrichment, the cheaper signal path)
- Related tests: TC-ADM-002-17 (forbids fabricating the uptime figure)
- See also: [[ADR-2026-08-11-goal-ownership-stays-individual]] — same pass, same principle
