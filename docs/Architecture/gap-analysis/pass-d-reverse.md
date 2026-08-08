# Pass D — reverse pass: shipped code with no story or doc

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains` @ `923db177`
> **Question:** what has been built that no user story and no technical document describes?
> **Status:** ✅ VALIDATED — 3 of 3 orchestrator spot-checks confirmed.
> **Headline:** zero orphaned code. But **two `[EPIC STUB]` stories have ~50 production files built against them** — code written against an empty placeholder, which reads as "covered" to every ledger, grep and traceability check in the repo.

## Orchestrator validation

| Claim | Result |
|---|---|
| US-ADM-012 and US-PLT-004 are `[EPIC STUB]`s with zero IEEE-829 TCs | ✅ **Confirmed.** Both carry `[EPIC STUB]` in the H1 and *"**STUB** — goal + AC skeleton + dependencies only; full detail to be authored before build."* `grep -rl` over `docs/QA/ --include='TC-*.md'` returns **0** for each. |
| `NoOpAttendanceProvider` is the only `IAttendanceProvider` registration | ✅ **Confirmed.** `DependencyInjection.cs:347` is the sole registration. Its comment — *"No attendance module yet (US-ATT-*)"* — **is stale**: attendance shipped 11 stories. |
| Zero orphaned code | ✅ Accepted on the auditor's mechanical whole-population scans (359 handlers, 62 jobs, all `features/` components). Not independently re-run. |

**Nuance the orchestrator adds to the NoOp finding:** Pass D rates this a HIGH bug. The pilot independently found the same seam from the other side (US-LV-011 AC-2) and located a *recorded decision* — `ILopService.cs:47` and `LopService.cs:233` both reference **ISSUE-357**, closed as DECIDED-NOT-BUILT (D2-b). So this is **a stale comment over a known deferral, not an unknown defect.** Two passes converging on it independently is still a signal that the deferral deserves re-examination now that attendance exists — but it should not be filed as a new bug.

---

## SCOPE

Surfaces covered **exhaustively** (mechanical, whole-population, not sampled):
- 79 controllers in `HRM.Api/Controllers/` — every file, route template + story citation
- 62 files in `HRM.Api/Jobs/` + `HostedServices/` + 11 `Middleware/`
- All 359 `IRequestHandler` files in `HRM.Application/Features/` (orphan scan)
- All `*.component.ts` under `src/frontend/src/app/features/` (orphan scan)
- All 65 route paths in `app.routes.ts` (740 lines, read in full)
- 125 `US-*.md` titles + ≥3 naming variants per candidate against `docs/BA/` and the tech doc §5.1/§11

**Sampled, not exhaustive — declared plainly:** no action-by-action AC comparison inside the 76 controllers that *do* cite a story. A controller citing `US-PAY-003` can still expose an action no AC describes. The 36 BE feature folders were worked from their controller/job entry points, per the capability-level rule. **That is this pass's residual blind spot.**

---

## VERDICT TABLE

| Capability | Wired at (file:line) | Verdict | Test bound? | Tenant data? |
|---|---|---|---|---|
| Plan/module runtime gating (`ModuleEntitlementMiddleware`) | `Program.cs:619`; `Middleware/ModuleEntitlementMiddleware.cs` | **UNDOCUMENTED-BUT-WIRED** | xUnit yes; **0 IEEE-829 TCs** | Yes |
| Per-tenant API-call metering + flush | `Program.cs:630`, `:379` (`ApiCallCounterFlushService`), `Domain/Entities/TenantApiUsage.cs`, migration `20260731012730_Platform_TenantApiUsage.cs` | **UNDOCUMENTED-BUT-WIRED** | `PlatformMonitoringUsageGaugesPostgresTests.cs`; **0 TCs** | Yes |
| Per-tenant storage + email-send usage counters | `Persistence/TenantStorageUsage.cs`, `TenantEmailSendUsage.cs` | **UNDOCUMENTED-BUT-WIRED** | 0 TCs | Yes |
| FE module gating (`moduleGuard`, nav hiding) | `app.routes.ts:31,45,347,483,517`; `core/tenant/module.guard.ts`; `main-layout.component.ts` | **UNDOCUMENTED-BUT-WIRED** | `module.guard.spec.ts`, `main-layout.nav-module-entitlement.spec.ts`; 0 TCs | Yes |
| OpenTelemetry traces/metrics + domain metrics | `Program.cs:74`; `Api/Observability/ObservabilityExtensions.cs`; `Common/Observability/HrmDomainMetrics.cs` | **UNDOCUMENTED-BUT-WIRED** | `ObservabilityExtensionsTests.cs`, `HrmDomainMetricsTests.cs`; 0 TCs | Partly (tenant dimensions) |
| `/health/live` + `/health/ready` + recorder job | `Program.cs:644,648`; `Jobs/HealthProbeRecorderJob.cs` reg. `:917` | **UNDOCUMENTED-BUT-WIRED** | 0 TCs | No |
| Encryption key-age watchdog + `encryption_key_activation` table | `Jobs/EncryptionKeyAgeWatchdogJob.cs`; reg. `Program.cs:482,904` | **UNDOCUMENTED-BUT-WIRED** | 0 TCs; documented only in `HRM.Infrastructure/Security/README.md` | No (system scope) |
| `/api/v1/system/*` host confinement | `Program.cs:601`; `Middleware/SystemEndpointHostGuardMiddleware.cs` | **UNDOCUMENTED-BUT-WIRED** | 0 TCs | **Yes — an isolation control** |
| `workspace-not-found` / `tenant-suspended` pages | `app.routes.ts:11,18`; `features/workspace/*.component.ts` | **UNDOCUMENTED-BUT-WIRED** (low) | 0 TCs | No |
| Dashboard cold-start warmup | `Program.cs:375`; `HostedServices/DashboardWarmupHostedService.cs` | **INTENTIONAL-INFRA** — perf priming, no user-visible behaviour | n/a | No |
| SCIM feature-flag gate on `/scim/v2` | `Program.cs:625`; `ScimEntitlementMiddleware.cs:12-21` | **INTENTIONAL-INFRA** — gate deliberately landed ahead of the feature, stated in-file | `PlanModulesEntitlementTests.cs` | Yes |
| JWT-tenant vs resolved-tenant guard | `Program.cs:603`; `TenantAccessGuardMiddleware.cs` | **DOCUMENTED** (naming drift only) | — | Yes |
| Salary-grade CRUD API + Angular UI | `SalaryGradesController.cs`; `app.routes.ts:377` | **DOCUMENTED** — `US-PAY-001.md:142` names the route **and** the UI | `TC-CHR-005-48.md` | Yes |
| F&F policy CRUD (effective-dated) | `FnFPolicyController.cs` | **DOCUMENTED** — `US-PAY-013.md:37,46` names the class verbatim | — | Yes |
| Tenant role/permission CRUD + catalog | `RolesController.cs` | **DOCUMENTED** — US-AUTH-006; tech doc §11.14 | — | Yes |
| Internal vacancy apply screen | `app.routes.ts:495` | **DOCUMENTED** — US-REC-002 AC-4 | — | Yes |
| `PooledLeaveLedger` FIFO pool split | `Infrastructure/Services/PooledLeaveLedger.cs` | **Not a capability** — private helper of US-LV-008 | `LeavePoolAwareCarryForwardTests.cs` | Yes |
| **Orphaned code (any layer)** | — | **NONE FOUND** | — | — |

---

## CONTRADICTIONS

**The orchestrator's brief was wrong on two premises.**

1. *Uncited controllers would surface undocumented surface area.* Only 3 of 79 controllers carry no `US-` reference, and **all three are documented** — `US-PAY-013.md:46` names `FnFPolicyController` by class name; `US-PAY-001.md:142` names the `/api/v1/tenant/salary-grades` route *and* its Angular UI. A missing in-file citation is comment hygiene here, not a requirements gap.
2. *`workspace` and `dashboard` are suspicious starting points.* `dashboard` maps cleanly to US-RPT-005. `workspace` is not a module at all — two tenant-lifecycle error pages. Starting there would have been a dead end.

**The real contradiction is elsewhere — and it is the finding of this pass.**

`docs/BA/STATUS.md` and the traceability tooling treat **US-ADM-012** and **US-PLT-004** as covering stories. They are not. Both are self-labelled:

> `[EPIC STUB]` — *"**STUB** — goal + AC skeleton + dependencies only; full detail to be authored before build."* with section *"4–10. Requirements (TO AUTHOR)"*

Yet **~25 production and test files cite each**, including two EF migrations and three middleware. `grep -rl` for TC files returns **zero** for both. (`docs/QA/platform/TC-PLT-004.md` is a naming collision — its frontmatter reads `user_story: US-PLT-005`.) Neither ID appears in `TRACEABILITY-MATRIX.md`.

That is the shape of this pass's finding: **not code without a story ID, but code built against a story ID that is an empty placeholder** — which reads as "covered" to every ledger, grep, and traceability check in the repo.

---

## GAPS RANKED

1. **Plan/module entitlement + usage metering shipped against a stub (US-ADM-012).** Tenant-scoped, billing-critical, two new usage tables, five enforcement points, zero IEEE-829 TCs. Close: author FR/BR/NFR/data sections into US-ADM-012 **from the shipped code**, then commission TCs for AC-1/AC-3/AC-5. **Size: M (doc) + M (TCs)**
2. **Observability substrate shipped against a stub (US-PLT-004).** OTel wiring, health probes, `TenantApiUsage` + migration, domain metrics. Same remedy. **Size: M**
3. **`SystemEndpointHostGuardMiddleware` — a tenant-isolation control with no requirement anywhere.** It confines `/api/v1/system/*` to the system context and its own doc-comment says the behaviour changes materially under RLS. **A security control nobody wrote a requirement for is a security control nobody wrote a test for.** Close: one AC on US-PLT-002 (RLS) + one isolation TC. **Size: S**
4. **Encryption key-age watchdog + `encryption_key_activation` table.** US-PLT-005 never mentions rotation cadence; documented only in a code-adjacent README. Close: an AC on US-PLT-005. **Size: S**
5. **`workspace-not-found` / `tenant-suspended` pages.** Two user-reachable screens on the tenant-lifecycle unhappy path, unspecified. **Size: S**

---

## COVERAGE SUMMARY

Capabilities audited: **19** | DOCUMENTED: 5 | UNDOCUMENTED-BUT-WIRED: 9 | INTENTIONAL-INFRA: 2 | **ORPHANED: 0** | Not-a-capability: 1 | CONTRADICTED (ledger vs reality): 2

**Where it concentrates:** every undocumented capability is **cross-cutting platform plumbing** — entitlement, metering, observability, encryption ops, host guards. **Not one business module** (Payroll, Leave, Recruitment, Performance…) had undocumented surface area. The BA corpus is genuinely thorough on domain features — it names controller classes and route templates *inside* acceptance criteria — and thin-to-absent on the platform layer, which is exactly where the two `[EPIC STUB]` stories sit.

**This independently corroborates Pass B**, which found 8 of its 15 uncovered capabilities in the §11.1 platform/operator surface. Two passes approaching from opposite directions land on the same conclusion: **the documentation pipeline worked for the product and skipped the platform.**

**On orphaned code — a clean zero, meant mechanically:** 0 of 359 MediatR request types undispatched; 0 of 62 Hangfire job classes unreferenced (all 12 `Hangfire*Scheduler` classes have live callers); 0 Angular components under `features/` lacking a route or parent reference. If you expected dead weight to delete, there isn't any at capability level.

---

## CONFIDENCE

- **Orphan-scan results (zero, all three layers): 90%.** Whole-population and mechanical. The 10% is transitive reachability — a job referenced only by a scheduler that is itself DI-registered but never invoked would pass. Schedulers were spot-checked (all ≥1 reference outside their own file) but not every chain traced to a call site.
- **US-ADM-012 / US-PLT-004 stubs with no TCs: 97%** — directly read; TC grep empty. *(Orchestrator re-verified.)*
- **`SystemEndpointHostGuardMiddleware` undocumented: 85%** — may be described in `docs/Architecture/security-reviews/` or an ADR outside the brief's comparison set.
- **Encryption watchdog undocumented: 90%.**
- **What limited this pass:** the sampling gap named in SCOPE — no per-action AC comparison inside the 76 story-citing controllers. Settling it would take ~76 units of work, and **that is where the remaining undocumented surface would hide.**

---

## OUT-OF-LANE

- **type:** bug · **severity:** HIGH *(orchestrator downgrades to MED — see validation note)* · **where:** `DependencyInjection.cs:344-347` · **what:** `IAttendanceProvider` is registered as `NoOpAttendanceProvider` with the comment *"No attendance module yet (US-ATT-*)"* — but attendance **has** shipped (US-ATT-001..011). `LopService` (US-LV-011 FR-2) is the sole consumer, so Loss-of-Pay computes from a provider that always returns zero absences. · **orchestrator note:** `ILopService.cs:47` and `LopService.cs:233` reference **ISSUE-357**, closed DECIDED-NOT-BUILT (D2-b) — this is a **known deferral with a stale comment**, not an unknown bug. Two passes converging on it independently argues for re-examining the deferral now that attendance exists. · **suggested-action:** update the stale DI comment; re-open the D2-b decision for review rather than filing a new bug.
- **type:** bug · **severity:** MED · **where:** `DependencyInjection.cs:690-695` · **what:** `IRecommendationIntegrationService` resolves to `LogOnlyRecommendationIntegrationService` — US-PRF-010's promotion→Core-HR / bonus→Payroll / training→Training integrations log and do nothing. Only registration; no conditional override. · **suggested-action:** verify against US-PRF-010's ACs in the wave-3 performance audit; if the ACs require real integration, that story is CONTRADICTED.
- **type:** risk · **severity:** MED · **where:** `ScimEntitlementMiddleware.cs:12-21`, `docs/BA/admin-console/US-ADM-009.md:27` · **what:** US-ADM-009 AC-2 lets a System Admin sell a `SCIM` feature flag on a plan, but no SCIM controller exists and US-ADM-005:84 defers it to Phase 2. **A plan can be sold with a capability that does not exist.** The gate middleware is a deliberate pre-landing; the *sellability* is the risk. · **suggested-action:** hide/disable the SCIM flag in the plan editor until the feature lands, or mark it "Phase 2" in the UI.
- **type:** doc-drift · **severity:** LOW · **where:** `docs/QA/TRACEABILITY-MATRIX.md` · **what:** neither US-ADM-012 nor US-PLT-004 appears, despite ~50 files citing them across two migrations and three middleware. · **suggested-action:** add both rows once the stub stories are authored.
