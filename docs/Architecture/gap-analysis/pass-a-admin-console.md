# Pass A1 — admin-console requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains`
> **Depth:** all 12 stories are Must Have → **one row per AC = 72 rows**, none collapsed. Largest module in the audit.
> **Status:** ✅ VALIDATED — 3 of 3 orchestrator spot-checks confirmed.
> **Headline:** 27 PARTIAL, **17 of them frontend**, 11 the exact pilot defect. And **a test case marked `pass` asserts a code path that does not exist.**

## Orchestrator validation

| Claim | Result |
|---|---|
| `TC-ADM-010-13` passes while asserting a `system_audit_log` row | ✅ **Confirmed, and it is the sharpest ledger finding of the exercise.** `docs/QA/admin-console/TC-ADM-010-13.md:7` is `status: pass`. No `SystemAuditLog` entity or `system_audit_log` table exists anywhere — the only mention is `IAuditLogPurgeService.cs:6`, which states outright: *"this platform **reuses the single audit table** with a system action."* **The TC cannot have verified what it claims.** |
| Three FE-called routes have no backend | ✅ **Confirmed — zero hits each** across all of `src/backend`: `assignable-roles` (0), `invite/csv` (0), `plan-overrides` (0). The Angular layer calls all three. |
| `WorkflowService` in-flight count is real, not hardcoded 0 | ✅ **Confirmed.** `WorkflowService.cs:460-466` is a real `WorkflowInstances.CountAsync(…)` + 409 `workflow_in_flight`, with a comment reading *"replacing the former hardcoded 0."* **The story text `US-ADM-011.md` is stale, not the code.** |

**Auditor pushback on the brief (both accepted):**
1. The brief said STATUS.md marks "nearly this whole module `[x]`" — it marks **all twelve**, including US-ADM-012, which its own story file still labels `[EPIC STUB]` with *"AC skeleton only; full detail to be authored before build."*
2. The brief framed forward drift as the dominant risk. In this module **reverse drift is nearly as costly** — five ACs are *better* than documented, including a CRIT (BUG-003) that still reads live. **Chasing phantoms is a real, ongoing tax on every triage pass.**

---

## COVERAGE SUMMARY

```
Requirements audited: 72 | IMPLEMENTED: 40 | PARTIAL: 27 | MISSING: 0 | CONTRADICTED: 5
```

**Nothing here is vapour.** Every AC has real code behind it, and several subsystems — US-ADM-011's runtime engine, the tenant lifecycle, `PlanLimitResolver`, the audit-log viewer — are genuinely well built with **real-Postgres** integration tests rather than InMemory theatre. **Zero MISSING.**

**Where the failures concentrate:** of 27 PARTIALs, **17 fail at the frontend**, and **11 are the exact pilot defect** — Angular coded against a contract the API never had, with Karma specs mocking the imagined shape. Only 8 are genuine backend leg-1 shortfalls.

**Why it stayed invisible is itself the evidence.** Every QA pass recorded in `docs/QA/TEST-STATUS.md:194-203` is explicitly a **"deep API pass"** driven by curl + JWT — and several lines even record the *correct* backend routes (line 198 lists `/tenant/users[/invite|/invite/bulk|/{id}/roles|/deactivate|…]`, precisely the contract the FE does not use). **The frontend was never exercised against a live API**, and the specs mock the FE's own wrong shape.

**By story:** US-ADM-011 strongest (12/12, backend-only by design). **US-ADM-005 weakest (0 of 6 clean — the backend is correct, the screen is not).** US-ADM-010 (2/6) and US-ADM-002 (2/5) follow.

Leg 3 is broadly healthy — 224 IEEE-829 TCs and 561 backend test files — but **zero `TC-ADM-011-*` files exist** despite twelve ACs.

---

## VERDICT TABLE (condensed; full evidence retained per row)

### US-ADM-001 Tenant provisioning
| AC | Verdict | Evidence · note |
|---|---|---|
| 1 | PARTIAL | `TenantProvisioningService.cs:127-260`; Real welcome-email service DI `:714`. **Holiday-calendar template and the 1-step manager-approval workflow are NOT seeded**; welcome email sends a forgot-password link, not FR-4's 72h set-password token |
| 2 | IMPLEMENTED | Exact 16-name reserved list in all four places; debounced availability endpoint; FE/BE DTO match |
| 3 | IMPLEMENTED | `:151-172` — `IgnoreQueryFilters()` global-user lookup, `ownerExists` carried to email |
| 4 | PARTIAL | **leg2 FE** — BE emits `id`; `tenant.models.ts:73` declares `tenantId` and uses it as the `@for` track key → duplicate-key breakage with ≥2 tenants |
| 5 | IMPLEMENTED | Format + 3–50 length on both layers |
| 6 | IMPLEMENTED | RLS on by default (`appsettings.json:20-21`). *85% — Development overrides to false* |

### US-ADM-002 Platform monitoring
| AC | Verdict | Evidence · note |
|---|---|---|
| 1 | PARTIAL | P95 and error rate now real, but **window is 24h; the AC says 5 min**. Error rate null unless GlitchTip configured |
| 2 | IMPLEMENTED | All four gauges `Available: true`; Green/Amber/Red/Breached bands present |
| 3 | PARTIAL | Breach panel exists but is **quota-driven**; the error-rate queue is GlitchTip-dependent and unsurfaced. *70%* |
| 4 | PARTIAL | **Highest-value leg2.** BE populates `latencyTrend24h` + `topErrors` (`:344-357`); the FE declares both, **never reads them**, and renders unconditional *"Not available — requires observability pipeline"*. Top Errors has no UI at all |
| 5 | IMPLEMENTED | Aggregates only; `TenantId = null` marks rows system-scoped |

### US-ADM-003 Impersonation
| AC | Verdict | Evidence · note |
|---|---|---|
| 1 | PARTIAL | Token/claims/session/expiry real; **FE does not open the tenant subdomain in a new tab** (self-documented deviation) |
| 2 | PARTIAL | Writes stamped, banner mounted globally. **Read/"view" actions produce no audit row** (deliberate per US-ADM-008 §10) |
| 3 | IMPLEMENTED | Lazy expiry + status gate + `ActionsCount`. Caveat: only non-GET increments |
| 4 | **CONTRADICTED** | Code correct (`DependencyInjection.cs:817` = Real notifier); **`TC-ADM-003-15` is stale** (claims log-only/blocked). Reverse drift |
| 5 | IMPLEMENTED | Read-only flag when Suspended; enforced in the MediatR pipeline |
| 6 | IMPLEMENTED | Exact string *"Support impersonation is read-only."* **Risk:** writes detected by `*Command` suffix — a controller writing outside MediatR bypasses the gate. *80%* |

### US-ADM-004 Tenant lifecycle
| AC | Verdict | Evidence · note |
|---|---|---|
| 1 | IMPLEMENTED | Real (not LogOnly) lifecycle notifier; job pause achieved implicitly via `Active\|Trial` filtering |
| 2 | PARTIAL | **leg2 FE** — backend gate + exact message correct; **no Angular suspension-notice route/component exists** |
| 3 | PARTIAL | All mechanics real; grace period is a **fixed 30-day default, not plan-configurable** (BUG-002) |
| 4 | IMPLEMENTED | Real-Postgres coverage (`TenantDataDeletionPostgresTests.cs`) |
| 5 | IMPLEMENTED | `past_due` branch deliberately absent — no billing exists. A decision |
| 6 | IMPLEMENTED | Reverts to prior state, cancels + removes scheduled jobs |

### US-ADM-005 User management — **0 of 6 clean**
| AC | Verdict | Evidence · note |
|---|---|---|
| 1 | PARTIAL | `IUserRoleRef.id` ≠ wire `roleId`; `employeeId` ≠ `linkedEmployeeId`; the `status='invited'` filter **can never match** (no `Invited` enum member) |
| 2 | PARTIAL | Backend + exact message correct. FE posts to **`/tenant/users/invite/csv`, which does not exist** (real: `invite/bulk`) → 404. `assignable-roles` **has no backend route at all** |
| 3 | PARTIAL | BE `PUT /tenant/users/{userTenantId}/roles`; FE calls `PUT /tenant/users/roles` → 404 |
| 4 | PARTIAL | BE `{userTenantId}/deactivate`; FE posts to `/deactivate` with the id in the body → 404 |
| 5 | PARTIAL | Same shape mismatch → 404. Backend `CrossTenantScope` revocation is correct |
| 6 | PARTIAL | Missing-membership returns 404 as specified; the header-spoof path returns **403 where the AC demands 404** — a recorded decision |

### US-ADM-006 Tenant settings
| AC | Verdict | Evidence · note |
|---|---|---|
| 1 | PARTIAL | **BE emits `orgProfile`; FE reads `s.org`** → the Organization Profile tab renders blank. Write path fine |
| 2 | PARTIAL | Login logo/colour render correctly; **upload posts to `/branding/upload`, which does not exist** (BE has three slot-specific routes) → an admin cannot upload branding. Email logo appears write-only. *70%* |
| 3 | IMPLEMENTED | Typed `Tenant` columns; FE tab matches |
| 4 | IMPLEMENTED | Real per-tenant policy + `PasswordPolicyValidator` + history check. **BUG-004 genuinely fixed** |
| 5 | PARTIAL | **BUG-003 (CRIT) genuinely closed.** Returns 403 where the AC specifies 404 — documented decision |

### US-ADM-007 Workflow configuration
| AC | Verdict | Evidence · note |
|---|---|---|
| 1 | PARTIAL | BE emits `lastModifiedAt`; FE reads `updatedAt` → "Last modified" column permanently blank |
| 2 | IMPLEMENTED | Step DTOs match FE 1:1 |
| 3 | PARTIAL | **BE keys `PUT` on `LineageId`; the FE model has no `lineageId`** and sends the version row's `Id`. Works on the first edit (they coincide), **404s on every subsequent edit** |
| 4 | IMPLEMENTED | Override > plan > snapshot resolution; FE surfaces the server message verbatim |
| 5 | **CONTRADICTED** | Config **and** live routing both shipped; `TC-ADM-007-15/-16` still `[DEFERRED]`. Reverse drift |

### US-ADM-008 Audit log
| AC | Verdict | Evidence · note |
|---|---|---|
| 1 | IMPLEMENTED | **The one admin FE module whose every URL matches the backend exactly** |
| 2 | IMPLEMENTED | Combined filters + JSONB keyword search |
| 3 | IMPLEMENTED | Real computed field-level diff + `SensitiveFieldMasker` |
| 4 | PARTIAL | Export + self-audit real; the **>10k Hangfire/email branch does not exist** — always synchronous with a `Deferred: true` flag |
| 5 | PARTIAL | No update/delete endpoint, but **NFR-3's DB-level enforcement is absent** — no REVOKE, trigger, or interceptor guard. Convention only |

### US-ADM-009 Subscription plans
| AC | Verdict | Evidence · note |
|---|---|---|
| 1–4 | IMPLEMENTED ×4 | Field-for-field DTO match incl. nested feature flags; limits read live so propagation is immediate |
| 5 | PARTIAL | **leg2 FE.** Backend resolution excellent, consumed at 7+ enforcement points. FE calls `/system/tenants/{id}/plan-overrides` — **no such route** (real: `/system/plans/overrides`). **Every Custom Limits action 404s** |

### US-ADM-010 Data export
| AC | Verdict | Evidence · note |
|---|---|---|
| 1 | PARTIAL | **leg2 FE.** FE puts the entity array into `scope` (typed `string?`) → JSON binding fails → **400 on every partial export**. Entity codes snake_case vs PascalCase registry; `formatOptions` nested where BE expects flat |
| 2 | PARTIAL | **3 of 5 artifacts built** (CSVs, `audit_log.jsonl`, manifest with real SHA-256). Documents ZIP and schema PDF absent. **The "signed URL" is an unsigned `/files/…` path with no registered route.** `Delimiter`/`DateFormat` never read |
| 3 | PARTIAL | Expiry + cleanup correct; **the emailed link points at the dead `/files/…` route** |
| 4 | IMPLEMENTED | Export allowed during `terminating`; integration-tested |
| 5 | IMPLEMENTED | Client-supplied tenant id ignored entirely |
| 6 | **CONTRADICTED** | Only **one** (tenant) audit row is written; **no `system_audit_log` write path exists anywhere**, yet `TC-ADM-010-13` asserts one and passes |

### US-ADM-011 Workflow runtime — **12 of 12, the strongest story in the module**
| AC | Verdict | Evidence |
|---|---|---|
| 1–7, 9–12 | IMPLEMENTED ×11 | Instance snapshotting, advance/complete, conditional skip, parallel all-approve/any-reject, idempotent SLA escalation via CAS `ExecuteUpdateAsync`, delegation, version pinning, tenant filters, approver 403, and `CreateExecutionStrategy` + `FOR UPDATE` concurrency — all with **real-Postgres** integration tests |
| 8 | **CONTRADICTED** | Real `CountAsync` + 409 (`WorkflowService.cs:460-466`); the story header still claims a hardcoded 0. Reverse drift |

### US-ADM-012 Module governance
| AC | Verdict | Evidence · note |
|---|---|---|
| 1 | IMPLEMENTED | Correctly ordered after auth/access/status. **Unmapped routes fail open by design** — a standing risk for new controllers |
| 2 | IMPLEMENTED | Route guard + nav hiding + a `module-key-drift` spec |
| 3 | PARTIAL | **3 of 4 enforced.** BUG-114 storage quota genuinely fixed. The **API-call limit is metered but never blocked** — no 429/403 on `MaxApiCallsPerMonth` |
| 4 | **CONTRADICTED** | All four gauges live including ApiCalls; `STATUS.md:236` still says ApiCalls is "deliberately unavailable pending US-PLT-004" — **which `STATUS.md:111` records as complete the same day.** The ledger contradicts itself |
| 5 | IMPLEMENTED | Per-request context; counters keyed `(TenantId, YearMonth)`. No two-tenant test — structural, not asserted. *85%* |

---

## GAPS RANKED

1. **The Users admin screen is non-functional end to end (ADM-005). Size: S. Blast radius: an entire Must-Have screen.** Seven of twelve service calls target routes that do not exist. **Smallest fix: correct eight URLs and two field names — no backend change needed**, the backend is correct and well tested.
2. **Plan limit overrides cannot be managed from the UI (ADM-009 AC-5). S.** Backend resolver is excellent; pure UI wiring loss.
3. **Partial data export 400s from the UI (ADM-010 AC-1). S.** Full export works; every entity-subset selection fails.
4. **Company Settings: Organization Profile blank, branding unuploadable (ADM-006). S.** Rename one field; split the upload method three ways.
5. **Editing a workflow a second time 404s (ADM-007 AC-3). S — but it silently corrupts the core value proposition of versioning.**
6. **Monitoring hides data the backend already returns (ADM-002 AC-4). S.** Bind two charts; flip `MetricsStatus` so it stops lying.
7. **Audit-log immutability is a convention, not a control (ADM-008 AC-5 / NFR-3). M. Compliance-relevant.** Anything with the app's DB credentials can rewrite the trail. *Note: interacts with `AuditLogPurgeJob`, which needs DELETE — the purge must run under a separate role.*
8. **Export bundle missing 2 of 5 artifacts and the download link is dead (ADM-010 AC-2/3). M–L.** GDPR Art. 20 portability is the story's stated purpose. **Pointing the email at the authenticated `/download` route is S and closes the user-visible break.**
9. **API-call plan limit metered but never enforced (ADM-012 AC-3). M. Billing-integrity.** Storage, email and custom-field caps *are* enforced — this is the odd one out.
10. **New tenants get no default workflow, so the ADM-011 engine never fires for them. S. Cross-story.** `TenantProvisioningService.cs:28-34` skips workflow seeding, justified by *"no configurable-workflow entity yet"* — **now false.** Every new tenant runs the AC-11 legacy fallback, leaving parallel steps, SLA escalation and delegation dormant until an admin hand-authors a workflow. **The highest-leverage small fix in the module.**
11. **Smaller documented deviations** — suspension notice page, plan-configurable grace period (BUG-002), >10k export branch, welcome-email token type, impersonation new tab, 24h vs 5-min error window, and 403-vs-404 on three ACs (**a recorded decision — leave it alone**).

---

## CONFIDENCE

**Overall: 88%.** Eight highest-consequence claims were re-verified by the auditor rather than trusted to its tracing sub-agents; the orchestrator independently re-verified three more.

- **ADM-002 AC-3 — 70%**: could not confirm whether the panel is quota-only or also error-rate-driven. *Settled by:* loading `/admin/monitoring` with GlitchTip configured.
- **ADM-006 AC-2 email-logo consumption — 70%**: no template appears to render `EmailLogoUrl`; the template layer was not exhaustively read.
- **ADM-003 AC-6 — 80%**: writes detected by `*Command` suffix; not all 80 controllers enumerated.
- **ADM-001 AC-6 — 85%**: RLS on in base config with strong test evidence, but Development disables it.
- **ADM-012 AC-5 — 85%**: bleed prevented structurally; no two-tenant test asserts it.

**Static-only limits.** Every FE↔BE mismatch is a static contract inference (route string vs `[Http*]` attribute; TS field vs DTO property under default camelCase — confirmed at `Program.cs:196-203` that only a `JsonStringEnumConverter` is registered, no naming-policy override). **Each is falsifiable in about a minute with the app running**, and all eleven are consistent with the QA ledger's own record that only the API layer was ever tested.

---

## OUT-OF-LANE

- **type:** doc-drift · **severity:** MED · **where:** `docs/QA/admin-console/TC-ADM-010-13.md` · **what:** a TC with `status: pass` asserts *"an export row exists in the system audit log"*, but no `SystemAuditLog` entity or table exists anywhere. · **suggested-action:** re-run and flip to `fail`, or rewrite the step against the tenant `audit_log`. **Audit sibling TCs for the same fabricated assertion.**
- **type:** risk · **severity:** HIGH · **where:** the whole DataExport feature + `TC-ADM-010-16` · **what:** `LocalFileStorage.GetSignedUrl` returns an unsigned `/files/{tenantId}/{path}` and **no such route is registered** — every export-ready email contains a dead link. · **suggested-action:** file as a BUG (the feature appears to work but delivers nothing); point the email at the authenticated `/data-exports/{id}/download` route as the S-sized interim fix.
- **type:** test-integrity · **severity:** HIGH · **where:** `src/frontend/src/app/features/admin/**/*.spec.ts` (systemic) · **what:** admin FE specs mock the frontend's own incorrect shapes, so they pass green over eleven features that cannot work against the real API. **This is the mechanism behind most of this audit's PARTIAL verdicts.** · **suggested-action:** route to `@test-authenticator`; consider generated OpenAPI types or a Playwright smoke per admin screen so FE↔BE drift fails a gate.
- **type:** risk · **severity:** MED · **where:** `ModuleEntitlementMiddleware.cs:21-28,126-127` · **what:** routes absent from `RouteModuleMap` **fail open by design** — every new controller is ungated until someone remembers to add it. · **suggested-action:** a startup assertion or test that every routed controller prefix is either mapped or explicitly allowlisted.
- **type:** doc-drift · **severity:** MED · **where:** `TEST-FINDINGS.md:29,302` (BUG-003); `US-ADM-011.md` header · **what:** BUG-003 is RESOLVED and verified in code, but its "affected surfaces" block still reads as live prose. `US-ADM-011.md` describes as broken a delete guard that is fixed. · **suggested-action:** sweep the closed-CRIT entries so **a resolved critical stops consuming triage attention every pass**.
