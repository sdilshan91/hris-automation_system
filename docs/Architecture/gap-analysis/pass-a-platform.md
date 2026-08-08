# Pass A10 — platform requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains`
> **Depth:** 3 Must-Have stories at AC level (13 ACs) + 3 Should-Have at story level = **16 rows**
> **Status:** ✅ VALIDATED — spot-checks confirmed, **including a correction to my own figure.**
> **Headline:** 🔴 **No US-PLT acceptance criterion covers the fail-open unresolved-tenant case.** The nearest one exists, is unmet, and is not test-bound. **An uncovered critical.**

## Orchestrator validation

| Claim | Result |
|---|---|
| **The EF filter layer has 6 holes, not 7** | ✅ **Confirmed — my figure was wrong.** I re-scanned: `AuditLog`, `PayrollReportExport`, `PlanLimitOverride`, `TenantLatencyBucket`, `TenantLifecycleEvent`, `TenantScheduledJob` have **zero** `Entity<T>()` configuration in `AppDbContext` and therefore no filter. **`TenantApiUsage` — which I had counted as the 7th — has both an `Entity<>()` call and a `HasQueryFilter`.** 132 filter calls in `AppDbContext.cs`, **0 in `Configurations/`.** |
| No US-PLT AC covers the fail-open case | ✅ Accepted — the auditor read all 26 ACs across six stories and quotes the nearest verbatim |

**The auditor also corrected a depth input:** `docs/BA/INDEX.md:221-224` swaps the MoSCoW of US-PLT-002 and US-PLT-003 relative to the story frontmatter. It used the frontmatter (the spec of record). **Any tool bucketing by INDEX audits the wrong tier at the wrong depth** — including this one, had it not checked.

---

## 🔴 C-1 — THE HEADLINE: no AC covers the fail-open case, and the nearest is unmet

All 26 ACs read. **None states what must happen when tenant resolution is *skipped* rather than *failed*.** The nearest is **US-PLT-002 AC-4**, verbatim:

> *"A system/admin context operation runs (migrations, `DbInitializer` seeding, tenant resolution lookup, system-level Hangfire jobs) | The operation executes | It uses a role/path that can legitimately bypass RLS (`BYPASSRLS` role or explicit system context), **without leaking tenant data to normal requests**"*

FR-4 reinforces it: *"Keep this bypass surface narrow and auditable."* **The code does not meet that clause.** All four layers disengage together on an *unresolved* (not system) context:

| Layer | Code | Behaviour when unresolved |
|---|---|---|
| Resolution | `TenantResolutionMiddleware.cs:90-93` (no subdomain), `:105-110` (**reserved** subdomain) | `await _next(context); return;` |
| EF read filter | `AppDbContext.cs:269-270` `!IsResolved \|\| …` | Tautology → returns every tenant's rows |
| DB role | `ConnectionRoutingInterceptor.cs:92-93` | Routes to **privileged `hrm_owner` (BYPASSRLS)** |
| Cross-tenant guard | `TenantAccessGuardMiddleware.cs:38-42` requires `IsResolved` | Skipped |

The reserved list (`:54`) is `["www", "api", "admin", "app", "mail", …]` — **`api` and `app` are both plausible production API hosts.**

**A mitigation I did not know about:** `SystemEndpointHostGuardMiddleware.cs:62-90` 403s `/api/v1/system/*` off the admin host **and fails closed on an unresolved context**. So **platform-admin endpoints are protected; `/api/v1/tenant/*` on a reserved host is not.** Auth still applies — this is **not anonymous access**. It is an authenticated tenant-A user reading tenant-B rows.

Not present in `TEST-FINDINGS.md` (zero hits for "reserved subdomain" / "unresolved tenant"). **`RlsIsolationPostgresTests` proves the *privileged* path works — never that the *unresolved* path is denied it.**

**Confidence:** 90% on the code paths (all four lines read directly); **60% on real-world exploitability**, which turns on whether `api.<basedomain>` or `app.<basedomain>` actually fronts the API in production. *Settled by an ops answer plus one probe: authenticate as tenant A, `GET /api/v1/tenant/employees` with `Host: api.<basedomain>`, count rows.*

---

## VERDICT TABLE

| Req ID | Requirement | MoSCoW | Verdict | Evidence · note |
|---|---|---|---|---|
| PLT-001 AC-1 | Interceptor unwraps `{success,data}` → bare `T` | Must | IMPLEMENTED | `api-envelope.interceptor.ts:37-52`; registered in `app.config.ts`; spec names US-PLT-001 |
| PLT-001 AC-2 | Error path surfaces envelope `message`/`errors` | Must | IMPLEMENTED | `error.interceptor.ts:81-83,131-141`. **Verified the backend never emits `success:false` on a 2xx** (`grep "Ok(ApiResponse.Fail"` → 0), so that hazard does not exist |
| PLT-001 AC-3 | Blob / 204 / bare array pass through | Must | IMPLEMENTED | `api-envelope.interceptor.ts:59-77` |
| **PLT-001 AC-4** | Specs updated to flush the **enveloped** shape | Must | **PARTIAL (leg1+3)** | **172 specs call `provideHttpClient`; only 2 register `apiEnvelopeInterceptor`.** **170 specs would stay green if the interceptor were deleted** |
| **PLT-001 AC-5** | Documented, consistent pagination contract | Must | **PARTIAL (leg1)** | `core/models/api-response.model.ts:30-40` declares `{data,total,page,pageSize}` and asserts it is "NOT additionally wrapped in `ApiResponse`". **Both claims false** — `PagedResult.cs:8-15` is `{Items,Page,PageSize,TotalCount,TotalPages}` inside `ApiResponse<>`. **The shared type has zero consumers.** Runtime *is* consistent (≥11 per-feature interfaces all use `items`/`totalCount`) — **the single documented contract is wrong and dead**, the exact trap that produced BUG-099 |
| PLT-003 AC-1 | Enums serialize as string names | Must | IMPLEMENTED | `Program.cs:197-203` + SignalR parity `:224-226`; binding test reads enums via `GetString()` |
| PLT-003 AC-2 | FE enum strings model-bind case-insensitively | Must | IMPLEMENTED | `CoreHrApiTests.cs:114-124` POSTs through the real pipeline → 201 |
| PLT-003 AC-3 | Every FE enum value matches the canonical wire value | Must | IMPLEMENTED | 20 C# enums cross-checked. **Two apparent mismatches investigated and cleared:** attendance `'WEB'`/`'MISSED_CLOCK_IN'` are **raw strings, not enums** (FE matches BE exactly); FE `NotificationChannel` is a **UI property-key union**, not the wire enum |
| PLT-003 AC-4 | Full suites green, no test weakened | Must | UNVERIFIABLE | Requires executing the gate — outside the read-only mandate |
| PLT-005 AC-1 | MFA secret encrypted **+ legacy rows migrated** | Must | IMPLEMENTED | `AuthService.cs:1538` protect, `:383,1609,1684` unprotect; DI `:184-187`; back-fill `DbInitializer.cs:52,346-390`; migration + 2 test suites. **Checked the NoOp trap: `PlaintextFieldProtector` is NOT registered** |
| PLT-005 AC-2 | Tenant SMTP/IdP secret envelope-encrypted | Must | **N/A by design** | `Tenant.cs:153` — *"Client secrets/certs are PLATFORM-level, NOT stored here."* **A recorded decision (ADR-2026-07-29). Nothing to encrypt because the feature does not exist** |
| PLT-005 AC-3 | Designated PII columns encrypted | Must | IMPLEMENTED | 9-column registry `EncryptedFieldRegistry.cs:49-68`; converters `AppDbContext.cs:262-266`; 2 migrations; **a bidirectional model↔registry drift guard** (`EncryptedFieldRegistryTests.cs:32`) |
| PLT-005 AC-4 | Key usage stays tenant-safe | Must | IMPLEMENTED | Per-tenant sweep `FieldEncryptionMaintenanceService.cs:168`; authz + sweep tests |
| **US-PLT-002** | RLS as defence-in-depth | Should | **PARTIAL (AC-4)** | AC-1/2/3/5/6 met and strong: `Rls.Enabled=true` default, half-flip fail-fast `DependencyInjection.cs:26,60-69`, **coverage guard with exact set equality** `RlsIsolationPostgresTests.cs:283-314`, fail-closed `:183-186`, `WITH CHECK` `:206-230`, pooling `:249-273`. **AC-4 fails — the bypass surface is not narrow** |
| **US-PLT-004** | Observability & platform NFRs | Should | **PARTIAL** | AC-2 ✔ health probes; AC-3 ✔ **real values** (`PlatformMonitoringService.cs:130-140,352-356` — P95 from histogram, honest `null` when unavailable); AC-4 ✔ API-call counter + entity + 2 migrations. **AC-1/AC-5 wired but inert** — `ObservabilityExtensions.cs:71-77` gates on a resolved OTLP endpoint; `appsettings.json:135` is blank. **Zero IEEE-829 TCs name US-PLT-004** |
| **US-PLT-006** | Error tracking via self-hosted GlitchTip | Should | IMPLEMENTED | All 7 ACs spot-checked: tenant tags BE+FE, PII scrubber, inert-when-blank, additive Serilog sink, `SendDefaultPii=false`, backup/restore, 8 TCs. **AC-7 delivered as a logical `pg_dump` rather than a volume snapshot — equivalent-or-better, naming drift not a gap** |

---

## CONTRADICTIONS (beyond C-1)

**C-2 — Reverse drift: `STATUS.md:111` claims a deferral that shipped.** It states the per-tenant API-call counter was *"deliberately deferred as its own slice"* because a partial build *"would leave the `ApiCalls` gauge FAKE."* It is built exactly as specified — middleware, DI, entity, **two migrations both postdating the note.** The same line's claim that AC-3 KPIs are "hard-coded null" is also stale.

**C-3 — Reverse drift: `TEST-STATUS.md:175` claims US-PLT-005 AC-1/AC-2 are "NOT yet built".** AC-1 is fully built and test-bound; AC-2 is closed N/A by ADR. **The same file's sibling line `:174` and `BA/STATUS.md:112` already say the opposite — the two ledgers contradict each other on one story.**

**C-4 — MoSCoW drift** between `INDEX.md:221-224` and the story frontmatter for US-PLT-002/003.

**C-5 — `BA/STATUS.md` contradicts *itself* on US-PLT-002.** Line 105/109 asserts *"QA-verified 2026-06-30: live DB has 0 policies / 0 RLS-enabled tables, flag false"*; lines 194/283 of the same file say *"code COMPLETE, policies proven."* **A reader hitting line 109 first concludes RLS is unbuilt.**

---

## GAPS RANKED

1. **🔴 CRITICAL — unresolved tenant context disengages all four isolation layers.** *Smallest close:* make the unresolved, non-system case fail closed in `ConnectionRoutingInterceptor.SelectPrivileged()` — **privileged only when `IsSystemContext` is explicitly true, never merely because nothing resolved** — and 400 reserved-subdomain requests to `/api/v1/tenant/*`. **Add a US-PLT-002 AC that names the unresolved case, since none does today.** *Size: M — the predicate is S; proving no legitimate path relies on "unresolved ⇒ privileged" (Hangfire, seeding, health probes) is the M.*
2. **HIGH — 6 EF filter holes and no coverage guard.** *S per entity, M for the guard.*
3. **MED — PLT-001 AC-4: 170 of 172 specs mock the unwrapped shape.** The interceptor is correct but **unguarded — deleting it breaks nothing in CI.** *L, mechanical.*
4. **MED — PLT-001 AC-5: the one shared pagination type is wrong and dead.** *S to fix, M to consolidate the ≥11 duplicates.*
5. **MED — US-PLT-004 has zero TCs** despite health probes, the counter, latency histograms and domain meters shipping against it.
6. **LOW-MED — OTel dormant by default.** **Honest fail-safe design, not a defect** — but AC-1/AC-5 are unsatisfied in the shipped configuration.
7. **LOW — no AC for key-rotation cadence.** Confirmed: none of US-PLT-005's four ACs mentions it. `EncryptionKeyAgeWatchdogJob`, its recurring registration, the `encryption_key_activation` table and its test **all ship with no requirement behind them.** **An undocumented shipped capability — a reverse gap.**

**Lead #5 refined:** `HrmDomainMetrics.cs:20-42` has **no tenant dimension** (framed as PII avoidance), so *metrics* cannot yield per-tenant SLA. **But traces *are* tenant-tagged** (`TenantResolutionMiddleware.cs:164-165`), and per-tenant P95 is produced **outside OTel entirely** via `TenantLatencyBucket` + `PlatformMonitoringService.cs:352`. So my brief overstated the reach — **the SLA gap it implies is already covered by another mechanism.**

---

## COVERAGE SUMMARY

```
Rows: 16 | IMPLEMENTED: 10 | PARTIAL: 3 | MISSING: 0 | UNVERIFIABLE: 1 | N/A-by-design: 1 | CONTRADICTED (cross-cutting): 5
```

**Leg 3 is the weakest leg here** — PLT-001 AC-4 (170 specs untested against the real shape), US-PLT-004 (zero TCs), US-PLT-002 AC-4 (**the bypass path is never negatively tested**). Where automated tests exist they are strong: mutation-verified encryption arms, exact-equality RLS coverage guard.

**All the drift in this module runs in the *reverse* direction** — the ledgers understate what shipped (PLT-002, 004, 005). **That is the opposite of the module-wide pattern and is worth attention in its own right.**

---

## CONFIDENCE

Thorough: PLT-001 (90% — AC-4/AC-5 rest on grep counts, not opening all 172 specs), PLT-003 AC-1/2/3 (90% — **AC-3 rests on a 20-enum sample of 120; a mismatch could survive unsampled**), PLT-005 (95%), US-PLT-002 (85% story-level — **AC-5's migration-authoring convention not verified**), US-PLT-006 (85%).

**Not reached:** PLT-003 AC-4 (requires executing the gate); **whether the 6 unfiltered entities are *reachable* on the unresolved path in practice** — that would sharpen gap #2 from "structural hole" to "confirmed exposure"; `docs/QA/platform/TEST-MATRIX.md` binding claims.

---

## OUT-OF-LANE

- **type:** test-integrity · **severity:** HIGH · **where:** `TenantApiCallUsagePostgresTests.cs:26`, `GlitchTipErrorTrackingTests.cs:18` · **what:** **two `[Trait("TC", …)]` ID collisions** — `TC-PLT-004` and `TC-PLT-006` are each claimed by two unrelated stories. · **suggested-action:** renumber into a free range. **Until then any trait-filtered run silently mixes two stories, and TEST-STATUS's "automated coverage" claims for US-PLT-005 are inflated by tests belonging to other stories.**
- **type:** risk · **severity:** HIGH · **where:** `AppDbContext.cs` (6 entities absent from the 132 filters) · **suggested-action:** **add an EF-filter coverage guard mirroring the RLS one — same reflection predicate, same exact-equality assertion — so the two layers drift together or not at all**, then close the 6 holes it reports.
- **type:** doc-drift · **severity:** MED · **where:** `INDEX.md:221-224` vs story frontmatter · **what:** MoSCoW swapped for US-PLT-002/003. · **suggested-action:** reconcile to the frontmatter and note which is authoritative — **the gap-analysis depth rule keys off `priority:`.**
- **type:** doc-drift · **severity:** MED · **where:** `BA/STATUS.md:105-111` vs `QA/TEST-STATUS.md:174-175` · **what:** the two ledgers contradict each other on US-PLT-002 and US-PLT-005, **and `BA/STATUS.md` contradicts itself on US-PLT-002.**
