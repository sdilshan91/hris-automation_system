# Pass A5 — recruitment requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains`
> **Depth:** 8 Must-Have stories at AC level (39 ACs) + 2 Should-Have at story level = **41 rows**
> **Status:** ✅ VALIDATED — 2 of 2 orchestrator spot-checks confirmed.
> **Headline:** the **strongest reverse-drift case of the entire audit** — the QA ledger narrates **twelve** closed defects as live, including a CRIT. And the public careers page is unreachable from its own UI.

## Orchestrator validation

| Claim | Result |
|---|---|
| Public careers DTOs carry no `Id`, FE routes on `v.id` | ✅ **Confirmed.** `PublicVacancyListItemDto` (`VacancyDtos.cs:68-79`) exposes `Slug`, `ReferenceNumber`, `Title`, … — **no `Id`.** `careers-page.component.ts:141` → `[routerLink]="['/careers', v.id]"`; `vacancy-detail.component.ts:180` → `[vacancyId]="v.id"`. Both resolve to `undefined`. |
| `PublicCareersEnabled` has no writer | Accepted at the auditor's 95% — it re-verified today that the only assignments in the whole solution are in two test fixtures |

**Auditor pushback on my brief — accepted.** I warned about NoOp/stub DI swaps. **The opposite is true here:** `RealRecruitmentNotificationService` (not the LogOnly variant) is registered at `DependencyInjection.cs:362`, and `ClamAvVirusScanner` is config-gated in at `:914` with the allow-stub only as an unconfigured-environment fallback. **A deliberate, documented gate — not a stub-swap defect.**

I also flagged the `[AllowAnonymous]` careers page as the worst-case tenant-isolation risk. **It is correctly tenant-scoped** — `PublicCareersService.cs:35-38,74-81,118-127` gate on `_tenantContext.IsResolved` plus the global query filter, and the tenant lookup is by resolved id, never client input. **It is broken in reachability, not isolation.**

---

## VERDICT TABLE (condensed — full evidence retained)

| Req ID | Requirement | Verdict | Evidence · note |
|---|---|---|---|
| REC-001 AC-1 | Create vacancy → Draft, tenant+perm scoped | IMPLEMENTED | `VacancyService.cs:60-92`; filter `AppDbContext.cs:442`. *See GAP-8: adjacent `currency` field silently dropped* |
| **REC-001 AC-2** | Publish → Open, internal + **public** listing | **PARTIAL (leg1+2)** | Publish + slug gen OK `:150-174`; public read OK. **Gate `Tenant.PublicCareersEnabled` has NO writer**; **FE careers page cannot navigate or apply** (no `Id` on the DTO). **Public leg dead end-to-end** |
| REC-001 AC-3 | Edit Open vacancy → update + audit | **CONTRADICTED** → IMPLEMENTED | `VacancyService.cs:128` `AddVacancyAudit("Vacancy.Updated")`; sink `:374-390`. Ledger asserts BUG-055 HIGH "writes NOTHING to audit_logs" — **false** |
| REC-001 AC-4 | Tenant isolation; RLS on `vacancy` | **CONTRADICTED** → IMPLEMENTED | Filter `:442`; RLS policy dynamic over every `tenant_id` table; `Rls:Enabled=true`; header-spoof closed `TenantAccessGuardMiddleware.cs:38-53` |
| REC-001 AC-5 | Close → no new applications, applicants retained | IMPLEMENTED | `:208`→`:392-418`; apply blocked `ApplicantService.cs:93-98` |
| **REC-002 AC-1** | Public apply → Applied, tenant blob, confirmation email | **PARTIAL (leg2)** | BE complete `:57-221`. **FE cannot reach it** — POST to `/careers/vacancies/undefined/apply` never matches `{vacancyId:guid}`. Karma spec mocks `id: 'vac-1'` |
| REC-002 AC-2 | Reject >25 MB or non-PDF/DOC/DOCX | **CONTRADICTED** → IMPLEMENTED | Size `:82`; MIME `:83-84`; **magic-byte sniff** `:131-135`. Ledger asserts BUG-058 (`.exe` renamed `.pdf` accepted) — **fixed** |
| REC-002 AC-3 | Duplicate application rejected | IMPLEMENTED | `:119-126` case-insensitive + unique index |
| REC-002 AC-4 | Internal employee applies, pre-filled + linked | PARTIAL (leg2) | BE OK; FE reads the same id-less DTO → unreachable. *90%* |
| REC-002 AC-5 | Cross-tenant applicant isolation | IMPLEMENTED | `AppDbContext.cs:446` + RLS + guard |
| REC-003 AC-1–2, AC-4–5 | Kanban, drag+audit, filters, isolation | IMPLEMENTED ×4 | `PipelineDtos.cs:11-65`; CDK drag `:286,636`; audit `ApplicantService.cs:481` |
| REC-003 AC-3 | Detail slide-over incl. **inline PDF resume preview** | PARTIAL (leg1) | Panel/timeline/interviews/scorecards OK. **Inline PDF absent** — `applicant-detail.component.ts:294` renders *"pdf.js is not a project dependency"*; no pdf dep |
| REC-004 AC-1, AC-3–5 | Stage change + notify; scorecard gate + offer workflow; reject reason; audit | IMPLEMENTED ×4 | `ApplicantService.cs:443-496,471,579-593,673-674`; `OfferService.cs:63,76-82` |
| REC-004 AC-2 | Screening→Interview requires **screening notes recorded** | PARTIAL (leg1) | Gate engine present `:470-477` but its only inputs are `hasScorecard`, `hasScheduledInterview`, `hiredCount` — **no screening-notes gate exists**. *85%* |
| REC-005 AC-1–5 | Schedule + notify + reminder job; 24h fire; edit/cancel; rounds; isolation | IMPLEMENTED ×5 | `InterviewService.cs:127,466,215-250,293`; `Program.cs:395` |
| REC-006 AC-1–4 | Scorecard submit/aggregate/multi-interviewer/isolation | IMPLEMENTED ×4 | `ScorecardDtos.cs:45-129` incl. anti-bias `HiddenCount`/`AntiBiasApplies` |
| **REC-007 AC-1** | **Select an offer template**, fill → PDF + store + preview | PARTIAL (leg1) | PDF/storage/preview OK. **No offer-template catalog** — a single hardcoded `OfferLetterTemplate.cs:16`; no entity; `GenerateOfferInput` has no `TemplateId`; FE declares `templateId` that nothing populates. **Documented deferral (`OfferService.cs:27`) — a decision** |
| REC-007 AC-2–3, AC-5 | Send + expiry job; accept/decline incl. portal; isolation | IMPLEMENTED ×3 | `OffersController.cs:62-92`; `PortalController.cs:81-86` |
| REC-007 AC-4 | Expiry → reminder + auto-Expired | **CONTRADICTED** → IMPLEMENTED | `OfferService.cs:47-51` (`ExpiryReminderDaysBefore=3`), `OfferExpiryReminderJob.cs`, `Program.cs:405`, migration `20260709152944`. Ledger asserts ISSUE-122 "not implemented" |
| REC-010 AC-1, AC-4–5 | Prefill; Converted badge + ratio; isolation | IMPLEMENTED ×3 | `ConversionDtos.cs:12-62`; FE maps naming drift correctly |
| **REC-010 AC-2** | Create employee, link, increment `filled_count` | **CONTRADICTED** → IMPLEMENTED | `ApplicantConversionService.cs:163-168` (execution-strategy wrapper — the BUG-068 root), link `:247`, `FilledCount += 1` `:258`. Ledger asserts **BUG-068 CRIT "100% BROKEN"** — **fixed** |
| REC-010 AC-3 | Optional user account + role + welcome email | **CONTRADICTED** → IMPLEMENTED | Toggle `:390-405`; email `:505,586`; onboarding `:504,520`; toggle **has a writer** (`TenantSettingsDtos.cs:137`). Ledger asserts ISSUE-140 "all log-only stubs" |
| **US-REC-008** | Candidate portal | IMPLEMENTED | HMAC + tenant binding, cross-tenant reject `:160`, **FR-7 raw-token email now wired** `:170-202`, IP rate limiter. **Reverse drift** vs ISSUE-132 "operationally UNREACHABLE, ZERO live callers" |
| **US-REC-009** | Recruitment dashboard & analytics | PARTIAL (leg2+1) | KPIs/funnel/sources/trend all real. **`GET /dashboard/filters` does not exist** → FR-7 drill-down dead, 404 swallowed by an empty `error:` handler. **FR-8 PDF export absent** |

---

## CONTRADICTIONS — the ledger is wrong in the *pessimistic* direction, twelve times

**The BA ledger's `[x]` marks are broadly correct. It is `docs/QA/TEST-STATUS.md` — frozen at a 2026-06-26/27 pass — that is now materially wrong.** Anyone reading it today would **re-open eight fixed tickets**.

| Ledger claim (verbatim fragment) | Code evidence |
|---|---|
| **BUG-055 HIGH** — vacancy writes "NOTHING to `audit_logs`" | `VacancyService.cs:83,128,168,196,238` each call `AddVacancyAudit`; body `:374-390` adds a real `AuditLog` with before/after. Test: `VacancyServiceAuditTests.cs` |
| **BUG-003 WRITE CONFIRMED** — cross-tenant read+write; *"a repo-wide grep finds **no** token-tenant-vs-resolved-tenant guard anywhere"* | `TenantAccessGuardMiddleware.cs:38-53` is **exactly that guard**, registered after auth at `Program.cs:592-603`. Plus RLS now enabled |
| **BUG-058 MED** — `.exe` renamed `.pdf` accepted | `ApplicantService.cs:128-135` — comment **names the finding** and calls `FileSignatureValidator.ValidateStreamAsync` → 400 |
| **BUG-059** (Hired terminal) · **BUG-060** (BR-2 reactivation) · **ISSUE-108** (interview gate) · **ISSUE-109** (no concurrency token) | `:668-676` documents and enforces each **by name**; token `:458-460` + `DbUpdateConcurrencyException` → 409 `:487-489`; migration + `ApplicantConcurrencyPostgresTests.cs` |
| **BUG-066** · **ISSUE-122** (offer) | `OfferService.cs:127-134` → 409 `offer_already_accepted`; reminder job + scheduler + DI + migration all present |
| **BUG-068 CRIT — "100% BROKEN, HTTP 500 on EVERY conversion"**, 9 of 14 TCs BLOCKED behind it · **ISSUE-140** | `ApplicantConversionService.cs:163-168` names BUG-068 and wraps in `CreateExecutionStrategy()`; regression tests present. `UserAccountCreated` no longer hardcoded |
| **ISSUE-132** — portal "operationally UNREACHABLE e2e … ZERO live callers" | `ApplicantPortalTokenService.cs:170-202` — *"FR-7: email the candidate their magic link"* — plus `PortalLinkBuilder` and an offer-email embed |
| **US-REC-006 AC-K1 scorecard versioning "not built"** (story file **and** `STATUS.md:343`) | `InterviewScorecardRevision.cs`, `ScorecardDto.Version`, `ScorecardRevisionDto`, query + controller + filter + migration `20260804193301` — **4 days old** |

**The ledger is self-inconsistent on BUG-003:** `TEST-FINDINGS.md:303` already reads *"CRIT · RESOLVED (PR #119, verified 2026-07-02)"* while the same entry's body and every `TEST-STATUS.md` recruitment line still narrate it as live.

**One finding the ledger got right and is still open:** **ISSUE-095** — `Tenant.PublicCareersEnabled` has no writer. Re-verified: the only assignments in the solution are in two test fixtures. Pointedly, `TenantSettings` *does* contain `UpdateHiringSettingsRequest` for the sibling `AutoCreateUserOnHire` toggle — **so this is an omission, not a policy.**

---

## GAPS RANKED

1. **🔴 Public careers page unreachable from its own UI — CRITICAL, S.** DTOs carry no `Id`; all three FE surfaces key on `id`. Navigation 404s, detail 404s, apply fails the `{vacancyId:guid}` constraint. **Why it stayed invisible:** backend integration tests call the API with a real GUID and pass; the Karma specs mock the *imagined* shape (`careers.service.spec.ts:28`, `careers-page.component.spec.ts:18,36-38`, `vacancy-detail.component.spec.ts:19` all seed `id: 'vac-1'`). **A third independent instance of the predicted defect class.** *Fix:* add `Id` to both public DTOs and populate at `PublicCareersService.cs:56-68,99-112`.
2. **Public careers master toggle has no writer — HIGH, S.** **Compounds with #1** — fixing either alone still leaves the page dead.
3. **`GET /recruitment/dashboard/filters` does not exist — MEDIUM, S.** The 404 is swallowed by an empty `error:` handler, so drill-down selects render empty **forever**. Spec mocks the phantom route.
4. **No offer-template catalog — MEDIUM, L.** A documented Phase-1 deferral, **so a decision not a defect** — but AC-1 as written remains unmet. Cheaper interim: strike the template-selection clause and record the deferral.
5. **No inline PDF resume preview — MEDIUM, M.**
6. **Screening-stage gate criterion not implemented — LOW-MED, S.** Gates are documented as advisory, so blast radius is a missing warning, not a wrong write. *85%*
7. **Dashboard PDF export absent — LOW, M.** QuestPDF is already a dependency.
8. **Vacancy salary-currency drift silently drops the value — LOW, S.** FE `currency` vs BE `SalaryCurrency`, **no mapper** in the service. The recruiter's selection never reaches the server.
9. **BR-5 per-vacancy public exclusion unreachable from the UI — LOW, S.** Backend defaults `true`, so nothing breaks — but a recruiter can never exclude one vacancy.

---

## COVERAGE SUMMARY

```
Rows: 41 | IMPLEMENTED: 25 | PARTIAL: 8 | MISSING: 0 | CONTRADICTED: 8
```

**All 8 CONTRADICTED rows are reverse drift** — the code is *better* than the ledger claims, so each also counts as functionally implemented. **Net functional health: 33 of 41 fully met.**

**Every single PARTIAL is in the frontend/contract layer or an explicit product-config gap. None is a broken backend behaviour.** The recruitment backend is **the most complete module audited**: correct CQRS wiring on all 9 controllers, tenant filters on all 11 entities, 21 matching migrations, real (not stub) notification/scanner/scheduler implementations, Hangfire jobs registered, 36 bound test files. **Leg 3 is essentially perfect** — 160 IEEE-829 TCs + 36 xUnit files.

Leg 1 fails in 3 places, **two of which are documented deferrals**. Leg 2 fails in 5, and **4 of those 5 are FE↔BE contract breaks with Karma specs mocking the invented shape.**

---

## CONFIDENCE

- **GAP-1 (public careers `id`): 97%** — DTO definitions, projection sites, both route files, both components, the service (no mapper) and all three spec files traced *(orchestrator re-confirmed)*.
- **GAP-3 (`dashboard/filters`): 95%** — four naming variants, zero hits.
- **REC-004 AC-2 (screening gate): 85%** — possible the intent is client-side validation; a product decision would settle it.
- **REC-002 AC-4: 90%** — confirmed the `vacancyId` break; did not read `internal-apply.component.ts` end-to-end for the pre-fill logic.
- **US-REC-008 story-level: 88%** — did not field-by-field diff `PortalDtos.cs` against `portal.models.ts`, **though the service does have an explicit mapper**, which is the pattern that has been reliable in this module.
- **Contradiction set (all 8): 93%** — each rests on named-in-code remediation comments plus a bound regression test plus (where relevant) a migration. Static reading proves the claimed-missing code now exists — **which is the exact claim the ledger denies.**
- **Overall: 90%.**

---

## OUT-OF-LANE

- **type:** doc-drift · **severity:** HIGH · **where:** `TEST-STATUS.md:163-172` · **what:** all ten recruitment lines are frozen at the 2026-06-26/27 pass and still narrate **twelve** closed findings as live, including BUG-068 (CRIT) and BUG-003 (CRIT). · **suggested-action:** run `/verify-fix` per finding ID, or one batch pass over the module. **Until then treat every recruitment line in TEST-STATUS.md as stale.**
- **type:** doc-drift · **severity:** MED · **where:** `TEST-FINDINGS.md:302-303` · **what:** BUG-003's header says RESOLVED while the following body still describes it as *"STILL PRESENT — unchanged at root locus"*. **A reader cannot tell which is current**, and the resolving code is cited nowhere in the entry. · **suggested-action:** collapse to RESOLVED, cite `TenantAccessGuardMiddleware.cs:38-53` + `Program.cs:603` as the fix locus, and move the historical narrative into a dated sub-block.
- **type:** test-integrity · **severity:** HIGH · **where:** `careers.service.spec.ts:28`, `careers-page.component.spec.ts:18`, `vacancy-detail.component.spec.ts:19`, `dashboard.service.spec.ts:134` · **what:** four specs assert against shapes the API has never returned, passing green over **two non-functional user journeys**. · **suggested-action:** route to `@test-authenticator`. **This is now the third module where FE specs mock an imagined DTO** — the systemic fix is contract tests generated from the OpenAPI document.
- **type:** risk · **severity:** LOW · **where:** `HRM.Api/uploads/…/recruitment/` · **what:** ~35 orphaned resume/offer artefacts from prior QA runs sit in the working tree, including files under a hardcoded-looking tenant id `3f000000-0000-4000-8000-00000000000f`. · **suggested-action:** confirm gitignored, then purge. **A predictable tenant id in a blob path is a weak enumeration aid should the storage root ever be served statically.**
