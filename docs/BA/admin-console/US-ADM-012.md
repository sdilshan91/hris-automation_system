---
id: US-ADM-012
module: Admin Console — Platform / Tenant Governance
priority: Must Have
persona: System Admin / Tenant Admin
status: ready
created: 2026-07-06
updated: 2026-09-04
sprint: backlog
acceptance_criteria_count: 5
---

# US-ADM-012: Plan / Module Governance Enforcement (Runtime Gating + Usage Limits)

> **AUTHORED FROM SHIPPED CODE, 2026-09-04 (F4 / GAP-030).** This story was a stub written 2026-07-06;
> the code shipped 2026-07-30 in six phases. Sections §4–§10 are **reverse-engineered from what is
> actually in `src/`**, not from the stub's intent — where the two disagree, the code wins.
>
> **AC-3 is only partly met** (§3.1): 8 of the 9 numeric plan-limit fields are enforced, but
> `max_api_calls_per_month` is validated, metered and displayed and **never blocks anything**. It is
> recorded here as an unmet control, not as a working one.
>
> ⚠ **This module has two enforcement layers whose failure directions are OPPOSITE** — the module gate
> fails **open**, the limit gates fail **closed**. That asymmetry is deliberate and is BR-1/BR-2, not an
> accident. ⚠ **Error/status codes are inconsistent across the limit gates** (403 *and* 409 *and* 422;
> four gates carry no machine code at all). FR-20/FR-21 document the **current** state honestly; they do
> not describe an idealised scheme that does not exist.

## 1. Description
**As a** System Admin (entitlements) and Tenant Admin (predictable limits),
**I want** the platform to enforce the tenant's subscribed plan at runtime — gating disabled modules and
enforcing usage limits — not merely store the configuration,
**So that** entitlements are real, over-limit usage is prevented/flagged, and disabled features are inaccessible.

## 2. Preconditions
- Plan/module configuration exists per tenant (US-ADM-009) with module enablement flags and numeric limits.
- `tenants.enabled_modules` holds the **canonical** module vocabulary. Before US-ADM-012 phase 1 this
  column held two incompatible vocabularies (permission prefixes vs canonical keys); a gate over that
  data would have 403'd every request for the seeded and E2E tenants (ISSUE-335, resolved).
- A resolved, non-system tenant context (`ITenantContext`) — the gate is inert without one.

## 3. Acceptance Criteria (IEEE 830 §3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A tenant's plan has a module disabled | A user calls that module's API | The request is rejected with 403 (disabled-module) — enforced server-side, not just hidden in the UI. |
| AC-2 | A module is disabled | The SPA renders navigation/routes | An FE route guard blocks navigation to that module and hides its nav entry. |
| AC-3 | A tenant is at its storage/API/email/custom-field limit | An action would exceed the limit | The action is blocked (or flagged per limit type) with a clear "limit reached — upgrade" message; covers BUG-114 storage quota + custom-field cap. |
| AC-4 | Usage accrues over time | Usage is queried | Per-tenant usage counters (storage bytes, API calls, emails sent, custom-field count) are tracked and readable (feeds US-ADM-002 monitoring + US-PLT-004). |
| AC-5 | Two tenants on different plans | Enforcement runs | Each tenant is gated by its own plan; no cross-tenant entitlement bleed. |

### 3.1 AC verdicts against shipped code (verified 2026-09-04)

| AC | Verdict | Evidence / reason |
|----|---------|-------------------|
| AC-1 | **MET** | `ModuleEntitlementMiddleware` maps 18 route prefixes to 12 modules (`:45-93`) and returns 403 `module_not_entitled` (`:133-138`); registered `Program.cs:774`, after tenant resolution and authz, before metering. **Caveat by construction:** the gate is a *positive* map, so anything unmapped falls open (BR-1). |
| AC-2 | **MET, with two uncovered modules** | `moduleGuard` (`module.guard.ts:85-96`) redirects to `/forbidden`; 20 route call-sites in `app.routes.ts`; nav filtered at `main-layout.component.ts:1165-1170` across 32 nav items. **But only 10 of 13 canonical modules have an FE guard — `Asset` and `CustomReportBuilder` have none** (CoreHR correctly has none, being always-on). For `Asset` the API 403s while the UI neither hides nor blocks the route. |
| AC-3 | **PARTIAL — the headline gap.** | 8 of the 9 numeric plan-limit fields are enforced (FR-14..FR-19). **`max_api_calls_per_month` is NOT.** It is validated on the plan (`SubscriptionPlanValidators.cs:55-56`), metered per tenant (`ApiCallCounterMiddleware.cs:80-101`) and displayed as a gauge (`PlatformMonitoringService.cs:553`, wired `:267-269`, `:360-361`) — but **no enforcement site exists anywhere in the codebase**. `ApiCallCounterMiddleware` never inspects a limit; its whole increment sits in a swallow-everything `try/catch` (`:96-100`) because "the meter is advisory; the request must not pay for its failure". A tenant may exceed its contracted API allowance without limit. **Do not read this AC as "the action is blocked" — for this key nothing is blocked.** Additionally the *messages* are inconsistent (FR-20/21) and four gates return no machine code, so a client cannot reliably branch on "limit reached". |
| AC-4 | **MET** | Employees, storage, API calls and email sends are all tracked and readable. **Note the asymmetry (FR-22):** only API calls have a persisted table; storage and email are computed live at read time. Custom-field *count* is surfaced per entity-type as `MaxAllowed` (`CustomFieldService.cs:121`). |
| AC-5 | **MET** | Enforcement reads the per-request scoped `ITenantContext` (`DependencyInjection.cs:93`); every limit gate resolves that tenant's own override→plan→snapshot chain; ~150 EF global query filters isolate the underlying counts (`AppDbContext.cs:270+`). |

**Net: 4 of 5 AC met, 1 partial.**

## 4. Functional Requirements (IEEE 830 §3.2)

**Module gating — server side (AC-1)**
- FR-1: A `ModuleEntitlementMiddleware` shall gate requests by mapping the **route prefix** to its owning
  module. **The route→module map IS the enforcement surface — not the controller set.** A controller
  whose prefix is absent from the map is ungated regardless of which module it belongs to
  (`ModuleEntitlementMiddleware.cs:45-93`).
- FR-2: The map shall contain **18 prefixes covering 12 modules** — every canonical module except
  `CoreHR`. `Leave` spans three prefixes (`/tenant/leave-entitlements`, `/tenant/leave-types`,
  `/leaves`); `Onboarding` spans three (`/onboarding`, `/offboarding`, `/exit-interviews`); `Payroll`
  spans two, including **`/api/v1/tenant/salary-grades`, which lives outside `/payroll`** — the headline
  trap that a name-level reading misses.
- FR-3: Matching shall be **first-wins**, so **ordering is load-bearing**. Two constraints are
  functional, not cosmetic: `/api/v1/onboarding/assets` (Asset) **must** precede `/api/v1/onboarding`
  (Onboarding), and `/api/v1/reports/custom` (CustomReportBuilder) **must** precede `/api/v1/reports`
  (Reporting). Reordering either silently reassigns the entitlement. Both are pinned by tests.
- FR-4: Matching shall be **segment-aware**, not raw `StartsWith` (`:154-156`): `/api/v1/leaves-archive`
  must not match `/api/v1/leaves`.
- FR-5: `Offboarding` and `/exit-interviews` shall gate under `Onboarding` — there is no separate
  Offboarding module in the canonical vocabulary. Whether offboarding should be independently sellable
  is an **open product question** (§10), not a defect.
- FR-6: On denial the middleware shall return **403** with `ApiResponse.Fail`, message *"This feature is
  not included in your organization's current plan."* and machine code **`module_not_entitled`**
  (`:133-138`, `:158-163`), logged at Information level.
- FR-7: The gate shall not read `ICurrentUser` — it must be anonymous-safe, since anonymous routes
  (`/api/v1/careers`) are gated (`:30-31`).
- FR-8: A sibling `ScimEntitlementMiddleware` shall gate `/scim/v2` with 403 `feature_not_entitled`
  (`ScimEntitlementMiddleware.cs:24, 66`). Note `/scim/v2` is **not** under `/api/`, so it is neither
  module-gated nor metered by the shared predicate.

**Module gating — fail-open semantics (the deliberate asymmetry)**
- FR-9: The gate shall **fail open** in every ambiguous case. A request passes unchecked when: (a) the
  tenant is unresolved or is the system context (`:109-113`); (b) the path is not `/api/`-prefixed or is
  on the 13-entry platform allow-list (`:118-122`, `PlatformApiPaths.cs:22-37`); (c) the route is
  unmapped, including **all CoreHR routes** (`:127-131`); (d) the tenant's module list is `null`
  (`PlanModules.cs:90-91`); (e) the list contains **any** token outside the canonical set — one
  unrecognised token condemns the whole list (`:102-103`); (f) the list is empty, which means
  unrestricted (`:109`).
- FR-10: CoreHR shall be gated by **omission from the map, not by an allow entry** — leaving it unmapped
  is what guarantees it can never be denied (`:42-44`).
- FR-11: The FE `isModuleEntitled` shall mirror the same three fail-open rules
  (`module.guard.ts:56-76`), so the two layers cannot disagree about what "unrestricted" means.
- FR-12: `CustomReportBuilder` shall be **pre-gated with zero live routes** — a deliberate decision
  (ISSUE-356, 2026-07-31). Neither prefix matches any controller today; the module was already
  *sellable* in the plan editor while nothing enforced it, so a tenant could be billed for it and denied
  it with identical behaviour. The map entry means the builder is gated the moment its routes exist.
  *(`/api/v1/attendance/reports/custom` gates under `Attendance`, not CustomReportBuilder — it matches
  the `/api/v1/attendance` prefix first.)*
- FR-13: `Asset` shall have **no dedicated controller** — its only route is `OnboardingAssetsController`
  at `/api/v1/onboarding/assets` (`:21`) — and **no FE guard or nav entry**. The API enforces it; the UI
  does not (AC-2 caveat).

**Usage-limit enforcement (AC-3)**
- FR-14: Nine numeric plan-limit fields shall exist, of which **eight are canonical override keys** in
  `PlanLimitKeys.All` (`PlanModules.cs:117-138`): `max_employees`, `max_storage_gb`,
  `max_api_calls_per_month`, `max_email_sends_per_month`, `max_custom_roles`,
  `max_custom_fields_per_entity`, `max_workflows`, `max_template_language_variants`. The ninth,
  `audit_log_retention_days`, is a **policy value rather than a limit** (`SubscriptionPlan.cs:83`) — it
  is not an override key and is enforced by the nightly purge job from the tenant snapshot
  (`AuditLogPurgeService.cs:41-52`).
- FR-15: Limits shall resolve **override → plan → tenant snapshot** via `PlanLimitResolver` /
  `PlanLimitLookup`, per US-ADM-009 FR-4.
- FR-16: `max_employees` shall be enforced at **three** independent sites: direct create
  (`EmployeeService.cs:1265-1268`), user invite (`UserManagementService.cs:414-415`) and bulk import
  (`BulkEmployeeImportService.cs:1229-1246`). **They use different denominators against the same cap** —
  the first two count `Employees.Where(IsActive)`, the invite path counts
  `UserTenants(Active) + UserInvitations(Invited) + staged` (`:406-412`). Block condition is `>=` in all
  three.
- FR-17: `max_storage_gb` shall be enforced on document upload (`EmployeeDocumentService.cs:240-249`)
  against a **shared** usage helper summing four size-bearing tables (`TenantStorageUsage.cs:23-32`), so
  the enforced total and the displayed gauge cannot drift. Block condition is `projected > limitBytes`
  (**strictly greater**). An **80% soft warning** is returned alongside a *successful* upload
  (`:252-255`) — a flag, not a block.
- FR-18: `max_custom_fields_per_entity` shall be capped **per `EntityType`** (the count filters on
  `EntityType`, `CustomFieldService.cs:178-181`), resolving override → plan → snapshot → a **hard
  default of 20** (`:32`, `:813-816`). This is the **only limit with a non-plan default**: the cap has
  never had an unlimited tier (`:781-782`).
- FR-19: `max_custom_roles` (`RoleService.cs:134-137`), `max_workflows` (`WorkflowService.cs:623-625`)
  and `max_template_language_variants` (`NotificationTemplateService.cs:114-117`) shall each be enforced
  on create.
- FR-20: **The status codes are inconsistent, and this FR records the current state rather than an
  intended one.** Three different 4xx codes are returned for the same semantic event ("plan cap
  reached"): **403** (employee direct-create, bulk-import cap-already-hit, storage, custom fields,
  custom roles), **409** (user invite, bulk-import would-exceed, workflows) and **422** (template
  language variants — the sole outlier). **`max_employees` is inconsistent with itself**: 403 from
  `EmployeeService`, 409 from `UserManagementService`, and both from `BulkEmployeeImportService`
  depending on branch. Tech-doc §47.2 specifies a single `PLAN_LIMIT_EXCEEDED` code with
  `{limit, current, planCode}` details; **no gate implements that shape.** Reconciling this is an open
  item (§10), not something to be written up as already done.
- FR-21: **Four enforcement sites return no machine-readable code at all** — `EmployeeService.cs:1266-1268`,
  `CustomFieldService.cs:182-183` and both `BulkEmployeeImportService` branches (`:1230-1232`, `:1240-1245`)
  — because `Result.Failure` defaults `errorCode` to `null` (`Result.cs:28-29`). The codes that do exist
  are `plan_limit_reached`, `storage_quota_exceeded`, `custom_role_limit_reached`,
  `workflow_limit_reached` and `variant_limit_reached`. A client therefore **cannot** branch reliably on
  "limit reached" — it must fall back to string matching for four of the gates.
- FR-22: *NOT IMPLEMENTED (AC-3)* — **`max_api_calls_per_month` has no enforcement site.** It is
  validated, metered and displayed only. `ApiCallCounterMiddleware` never inspects a limit
  (`:80-101`). No `PlanLimitLookup.ResolveAsync(..., MaxApiCallsPerMonth, ...)` exists outside
  `PlatformMonitoringService`. Tech-doc §35.3 says this limit is "Enforced where: Rate limiter"; the
  rate limiter is not plan-aware.

**Fail-closed semantics on the limit gates**
- FR-23: An **unresolvable `plan_id`** (a `plan_id` matching no subscription plan) shall be treated as a
  **configuration error, not as "unlimited"** — `EffectivePlanLimit.IsConfigurationError`
  (`PlanLimitLookup.cs:58`). Treating it as unlimited is precisely how a paid cap silently stopped
  existing (BUG-307).
- FR-24: A deployment with **zero** plan rows shall be treated as resolvable — "not using plan-based
  limiting" — rather than as an error (`PlanLimitLookup.cs:160-168, 181`).
- FR-25: Three HTTP sites shall fail closed with **403 `plan_unresolvable`** —
  `UserManagementService.cs:395-397`, `RoleService.cs:121-123`, `WorkflowService.cs:608-610`. **Three
  more fail closed with 403 but carry no code** — `EmployeeService.cs:1251-1252`,
  `BulkEmployeeImportService.cs:1213-1214`, `EmployeeDocumentService.cs:224-226`. *(So the statement
  "unresolvable plan → 403 `plan_unresolvable`" is true for 3 of 6 HTTP sites.)*
- FR-26: Three sites **cannot** return an error because they return a bare value, and shall instead fall
  back to `PlanLimitLookup.StrictestConfiguredAsync` — the tightest cap any configured plan sells
  (`:96-116`): `CustomFieldService.cs:798-811` (else 20), `NotificationTemplateService.cs:177-188`
  (else 2), and `RealNotificationDispatcher.cs:195-214`, which explicitly refuses to fall back to zero
  because that would suppress **every** non-mandatory email (`:199-205`).

**Email cap — silent suppression**
- FR-27: `max_email_sends_per_month` shall be enforced in the dispatcher
  (`RealNotificationDispatcher.cs:226-234`), counting `Channel == Email && Status == Sent && SentAt >= monthStart`
  (`:222-225`).
- FR-28: **Suppression shall be invisible to the caller.** Over cap, the dispatcher writes a
  `NotificationDelivery` row with status `Suppressed` and reason `email_send_limit_reached`, then
  returns — **no exception, no 4xx, no result**. `DispatchAsync` returns `Task`; the caller is never
  told the email did not go out. This is a deliberate design choice with an operational cost: the only
  evidence is the delivery ledger.
- FR-29: **Mandatory security email shall bypass the cap entirely** — the whole cap block is wrapped in
  `if (!isMandatory)` (`:183`). A hit marketing/notification quota must never suppress a password reset
  (`:180-182`).

**Usage counters (AC-4)**
- FR-30: Per-tenant usage shall be recorded by two different mechanisms: **API calls persist** to
  `tenant_api_usage` (monthly grain, `TenantApiUsage.cs:17-30`), whereas **storage and email have no
  table** and are computed live — `TenantStorageUsage.ComputeBytesAsync` (`:21-33`) and
  `TenantEmailSendUsage.CountSentThisMonthByTenantAsync` (`:32-47`). Consequence: the API-call gauge
  lags by up to one flush interval; the other two are exact-at-read.
- FR-31: Four gauges shall be built — Employees, Storage (in **megabytes**), API calls, Email sends —
  resolving limits through the same `PlanLimitResolver` as the gates so gauge and enforcement agree
  (`PlatformMonitoringService.cs:424-451`, `:455-459`). **Only 4 of the 9 limit fields have a gauge**;
  custom roles, custom fields, workflows, template variants and audit retention have none.
- FR-32: Full usage/metering behaviour is specified in **US-PLT-004** FR-24..FR-35 and is not restated
  here.

**Plan-change propagation**
- FR-33: A plan edit that changes the module list shall propagate to running tenants via a
  **synchronous, in-transaction sweep** (`SubscriptionPlanService.cs:314-333`), chosen over a Hangfire
  job so the plan edit and the swept tenant snapshots commit together (`SaveChangesAsync` at `:145`).
- FR-34: The sweep shall be gated on an **order-insensitive** `SetEquals` comparison, so a price/name/
  limit-only edit skips it entirely (`:129-142`).
- FR-35: The sweep shall update **only `EnabledModules` and `UpdatedAt`** (`:328-329`). It does **not**
  re-stamp `Tenant.MaxEmployees` or `Tenant.AuditLogRetentionDays`, so a plan edit changing a numeric
  limit leaves those denormalised snapshots stale — whereas `ChangeTenantPlanAsync` **does** re-stamp
  both (`TenantLifecycleService.cs:322-327`). This asymmetry is real and is flagged in §10.
- FR-36: Tenants shall join a plan by **string code with no FK** (`t.PlanId == plan.Code`); a dangling
  `PlanId` is simply never matched (fail-open at the sweep, fail-closed at the gates — FR-23).
- FR-37: Cache invalidation shall be **post-commit and best-effort**, outside the transaction and
  null-guarded (`:149-150`), so an invalidation failure leaves stale entitlements until TTL expiry.
- FR-38: Plan changes shall validate the plan's module list against the canonical vocabulary, rejecting
  unknown keys with 400 `module_invalid` (`SubscriptionPlanService.cs:122, 337-345`). **Nothing
  validates a tenant's `EnabledModules` snapshot on write** — which is exactly why the FR-9(e) fail-open
  backstop exists.

**Downgrade policy**
- FR-39: A downgrade shall be **allowed** — a smaller plan is permitted and never deletes data
  (`TenantLifecycleService.cs:282-289`).
- FR-40: Existing over-cap rows shall be **grandfathered**; removed modules are gated forward by the
  module gate and lowered caps forward by the limit gates' `count >= cap` rule. **No over-cap validation
  runs during a plan change** (`:291-328`).
- FR-41: Plan change shall reject: empty code → 400 `plan_code_required`; missing tenant → 404
  `tenant_not_found`; the system tenant → 403 `system_tenant_protected`; a missing or archived plan →
  400 `plan_invalid` (`:294-315`).

**Multi-tenant isolation (AC-5)**
- FR-42: `ITenantContext` shall be **scoped** (`DependencyInjection.cs:93`); every gate reads the
  current request's tenant only.
- FR-43: Underlying counts shall be tenant-isolated by ~150 EF global query filters
  (`AppDbContext.cs:270+`), predicate
  `x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId)`.
- FR-44: `PlanLimitOverride`, `TenantLifecycleEvent` and `TenantScheduledJob` shall be **deliberately
  excluded** from query filters (`AppDbContext.cs:802-809`) because the monitoring path reads them
  cross-tenant; the exclusion is registered with a reason in the coverage guard's allow-list rather than
  silently omitted.

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1 (Performance): The module gate shall add no database round-trip — it reads `EnabledModules` off
  the already-resolved `ITenantContext` and does a prefix scan over a static 18-entry array. It sits
  after authn/authz and before metering (`Program.cs:774`).
- NFR-2 (Security — server-side authority): FE gating is **convenience, never the control**. AC-2's
  guard hides and redirects; AC-1's middleware is the enforcement. A tenant that bypasses the SPA must
  still be 403'd.
- NFR-3 (Security — anonymous safety): The gate must not dereference user identity; anonymous routes
  (`/api/v1/careers`) are gated and must not throw (FR-7).
- NFR-4 (Safety — fail-open by construction): The module gate's positive-list design means a new
  controller is **ungated until someone maps it**. This trades false denials for false allows
  deliberately (BR-1); the cost is that adding a module route without a map entry is a silent
  entitlement hole.
- NFR-5 (Safety — fail-closed on misconfiguration): The limit gates take the opposite trade: an
  unresolvable plan denies rather than grants (FR-23/BR-2).
- NFR-6 (Consistency): The gauge and the enforcement gate shall resolve limits through the same
  primitive so the displayed number and the enforced number agree
  (`PlatformMonitoringService.cs:455-459`). **Known deviation:** the gauge path calls `PlanLimitResolver`
  directly rather than `PlanLimitLookup`, so it has **no `IsConfigurationError` handling** — a tenant
  with an unresolvable `plan_id` sees "unlimited" in the console while the gates 403 the same tenant.
  Recorded as a known inconsistency (§10), not as a satisfied requirement.
- NFR-7 (Usability): A denial shall tell the user it is a *plan* limitation, not a permissions error, so
  the remedy (upgrade) is discoverable. **Partly unmet:** the FE redirects to a generic `/forbidden`
  page with no plan-specific explanation (`module.guard.ts:94`), and four limit gates return no machine
  code to branch on (FR-21).
- NFR-8 (Multi-tenancy): No entitlement, cache key, counter or gauge may be shared across tenants;
  isolation is enforced by scoped context + query filters + RLS (FR-42..FR-44).
- NFR-9 (Auditability): Plan changes are audited via US-ADM-009 NFR-3; module-gate denials are logged at
  Information with module, tenant id and path (`:133-136`).
- NFR-10 (Availability): Metering failure must never fail a request (US-PLT-004 NFR-3); entitlement
  failure must never *pass* one. These are opposite by design (BR-2).

## 6. Business Rules
- BR-1: **The module gate fails OPEN.** Unmapped route, non-canonical vocabulary, empty module list,
  unresolved tenant and system context all pass. Rationale: a partly-configured tenant must stay usable,
  and CoreHR must never be deniable. The cost is accepted: an unmapped module route is ungated.
- BR-2: **The limit gates fail CLOSED.** An unresolvable `plan_id` denies with 403 rather than being read
  as unlimited (BUG-307). **BR-1 and BR-2 point in opposite directions on purpose** — wrongly denying a
  feature is recoverable; wrongly granting unlimited paid capacity is not.
- BR-3: **CoreHR is always enabled and can never be denied**, enforced by omission from the route map.
- BR-4: **A module may be sellable before it is buildable.** `CustomReportBuilder` is grantable in the
  plan editor and pre-gated with zero live routes, so it cannot ship ungated later (FR-12).
- BR-5: **`NULL` limit means unlimited; an unresolvable plan does not** (US-ADM-009 BR-3 + FR-23). The
  two used to arrive at the gates as an indistinguishable bare `null`.
- BR-6: **Per-tenant overrides beat plan values**, which beat the tenant snapshot (US-ADM-009 FR-4).
- BR-7: **Lowering a limit never removes existing data.** Downgrade is *allow, grandfather, enforce
  forward*: over-cap tenants keep what they have and simply cannot add more (FR-39/FR-40).
- BR-8: **A plan edit propagates to running tenants in the same transaction** — entitlements must never
  be enforced from a plan state no admin can see (FR-33).
- BR-9: **Mandatory security email is never suppressed by a quota.** A password reset must go out even
  when the marketing quota is exhausted (FR-29).
- BR-10: **Email suppression is silent to the caller** and evidenced only by a `Suppressed` delivery row
  (FR-28). Anyone reasoning about "did the email send?" must read the ledger, not the return value.
- BR-11: **Custom fields have no unlimited tier** — the cap falls back to a hard 20 (FR-18).
- BR-12: **Platform, admin and auth traffic is never gated**, so a plan misconfiguration can never lock a
  tenant out of login, settings, audit logs or its GDPR data export (FR-9(b)).
- BR-13: **`max_api_calls_per_month` is currently advisory only** — it is measured and shown but does not
  constrain. Until FR-22 is built, this limit is a reporting figure, not an entitlement.

## 7. Data Requirements
- **Input (per tenant):** `tenants.enabled_modules` (JSONB array of canonical keys — 13-value
  vocabulary), `tenants.plan_id` (string code, **no FK**), plus denormalised snapshots
  `tenants.max_employees`, `tenants.max_custom_fields`, `tenants.audit_log_retention_days`.
- **Input (per plan):** `subscription_plan.enabled_modules` + the nine numeric limit fields (FR-14).
- **Input (per tenant override):** `plan_limit_override` — `tenant_id`, `limit_key` (**snake_case**, must
  be in `PlanLimitKeys.All`), `value`, `expires_at`.
- **Canonical module vocabulary (13):** `CoreHR, Leave, Attendance, Recruitment, Onboarding, Payroll,
  Performance, Training, Asset, Benefits, Reporting, CustomReportBuilder, PublicCareersPage`
  (`PlanModules.cs:10-29`), duplicated deliberately in `module.guard.ts:17-31` (core must not import
  from a feature) and again in `plan.models.ts`.
- **Enforcement surface:** the 18-entry `RouteModuleMap` (FR-2) — this is data as much as code; changing
  its order changes behaviour.
- **Output (denial):** `ApiResponse.Fail` — `{ success:false, message, code, errors[], timestamp }`.
  Codes in use: `module_not_entitled`, `feature_not_entitled` (SCIM), `plan_limit_reached`,
  `storage_quota_exceeded`, `custom_role_limit_reached`, `workflow_limit_reached`,
  `variant_limit_reached`, `plan_unresolvable` — **and `null` at four sites** (FR-21).
- **Output (usage):** four gauges (used, limit, percent, band) via `GET /api/v1/system/monitoring/*`,
  `Monitoring.View` permission.
- **Counters:** `tenant_api_usage` (persisted, monthly); storage and email derived at read time (FR-30);
  custom-field count derived per entity-type.
- **Tables affected:** `tenants`, `subscription_plan`, `plan_limit_override`, `tenant_api_usage`,
  `notification_delivery` (Suppressed rows), `system_audit_log`.

## 8. UI/UX Notes
- **Route guard:** `moduleGuard(module)` returns `router.createUrlTree(['/forbidden'])` on denial
  (`module.guard.ts:94`) — a redirect, not a `false`. **Gap:** the `/forbidden` page is generic; there is
  no plan-specific message and no upgrade call-to-action, so a user cannot tell "not entitled" from "not
  permitted" (NFR-7).
- **Nav filtering:** `visibleNavItems()` filters in order persona → **module entitlement** → tenant-role
  → permission (`main-layout.component.ts:1158-1180`); 32 nav items carry a `module`. Because
  `isModuleEntitled` fails open, an item is hidden only when the tenant has an authoritative canonical
  list omitting that module.
- **Coverage gap:** `Asset` and `CustomReportBuilder` have **no** route guard and **no** nav item, and
  `PublicCareersPage` has no nav item (it is outside the authenticated shell). For `Asset` the API 403s
  while the UI offers no signal.
- **Gauges:** the System Admin console shows four usage gauges with a colour band; storage is displayed
  in MB. Five limit fields have no gauge (FR-31).
- **Storage soft warning:** at 80% the upload **succeeds** and returns a warning string — surface it as a
  non-blocking banner, not an error (FR-17).
- **Upgrade path:** tech-doc §47.2 envisages a `{limit, current, planCode}` payload driving a "you have
  100 of 100 — upgrade" message. **No gate emits that shape today** (FR-20), so the UI cannot render it.
- **Known FE defects (filed, not design):** the plan-override admin UI calls
  `/system/tenants/{id}/plan-overrides` while the API serves `/system/plans/overrides` — all three
  override calls are a live 404; and `LIMIT_FIELDS` uses camelCase keys the backend rejects as
  `limit_key_invalid`, includes `auditLogRetentionDays` (never a valid override key) and omits
  `maxTemplateLanguageVariants`. Net: **`PlanLimitOverride` is enforced everywhere but settable only by
  direct API call, not through the console.**

## 9. Dependencies
- **US-ADM-009** — plan/module configuration, `plan_limit_override`, and the override→plan resolution
  order this story enforces at runtime.
- **US-ADM-002** — surfaces the usage gauges.
- **US-PLT-004** — supplies `tenant_api_usage` and the shared `PlatformApiPaths` predicate; note the
  dependency is mutual: US-PLT-004's AC-4 feeds this story's AC-4, and this story's AC-3 gap on
  `max_api_calls_per_month` is the enforcement half that US-PLT-004 deliberately did not build.
- **US-PLT-002** — RLS beneath the query filters (AC-5 defence in depth).
- **US-NTF-006** — the `NotificationDelivery` ledger that email counting and suppression ride on.
- **US-CHR-012** — custom-field definitions the per-entity cap applies to.
- **US-ADM-004** — tenant lifecycle; `ChangeTenantPlanAsync` is the plan-change entry point.

## 10. Assumptions & Constraints
- **Constraint (accepted):** the module gate is a **positive list**, so coverage is only as good as the
  map. Adding a module's controller without a map entry ships it ungated, and nothing fails loudly. The
  mitigation is the ordering tests, not the type system.
- **Open question (product):** should **Offboarding** be independently sellable? It currently gates under
  `Onboarding` (FR-5). Splitting it ripples into `PlanModules`, the plan editor, the FE
  `CANONICAL_MODULES`, the ISSUE-353 drift guard and the normalization migration's canonical literal.
  **Needs a decision — do not resolve by guessing.**
- **Open question (API contract):** the 403/409/422 divergence and the four code-less gates (FR-20/21)
  should be reconciled against tech-doc §47.2's `PLAN_LIMIT_EXCEEDED` shape. Changing them is a
  **breaking API change** for any client already branching on the current codes, so it needs a decision,
  not a refactor.
- **Known inconsistency:** the gauge path lacks `IsConfigurationError` handling, so an unresolvable
  `plan_id` displays as "unlimited" while the gates 403 — the displayed and enforced numbers disagree in
  exactly the BUG-307 case (NFR-6).
- **Known inconsistency:** `max_employees` is counted with two different denominators (FR-16), so the
  invite path and the create path can disagree about whether a tenant is at cap.
- **Known gap:** a plan edit re-stamps `EnabledModules` but not `MaxEmployees`/`AuditLogRetentionDays`
  (FR-35), so those snapshots can go stale until the tenant's plan is changed outright.
- **Assumption:** the canonical vocabulary is duplicated in three places (BE `PlanModules`, FE
  `module.guard.ts`, FE `plan.models.ts`) and kept in step by a drift guard rather than by a shared
  artefact. Accepted so core need not import from a feature.
- **Assumption:** Phase 1 billing is manual; enforcement blocks usage but triggers no billing event or
  automatic upgrade.
- **Constraint:** entitlement caching is best-effort post-commit (FR-37); a stale entitlement can persist
  until TTL after a cache-invalidation failure.
- **Constraint:** `max_api_calls_per_month` cannot be enforced in `ApiCallCounterMiddleware` as written —
  the counter is deliberately fail-open and advisory (US-PLT-004 NFR-3). Enforcement needs a distinct
  gate with a decision about whether to 429 or 403, and whether to block mid-month.

## 11. Test Hints
- **AC-1:** disable Recruitment → `GET /api/v1/recruitment/*` returns 403 `module_not_entitled`; a
  CoreHR route (`/api/v1/employees`) still returns 200 for the same tenant.
- **FR-3 (ordering — the highest-value arms):** with `Onboarding` disabled but `Asset` enabled,
  `/api/v1/onboarding/assets` must be **allowed**; with `Asset` disabled but `Onboarding` enabled it must
  be **denied**. Mirror for `/api/v1/reports/custom` vs `/api/v1/reports`. Reversing either map entry
  must fail these tests.
- **FR-2 (the trap):** disable Payroll and assert `/api/v1/tenant/salary-grades` is 403 — a
  controller-set-based reading of the gate misses this route entirely.
- **FR-4:** `/api/v1/leaves-archive` must not be gated by the `/api/v1/leaves` entry.
- **BR-1 (fail-open, four arms):** `enabled_modules` = `null`, `[]`, containing a non-canonical token,
  and an unmapped route — each must **pass**. A mutant that flips any to deny must fail.
- **FR-7:** anonymous `GET /api/v1/careers/*` with `PublicCareersPage` disabled → 403 and **no**
  NullReferenceException.
- **BR-2 / FR-23 (fail-closed):** set `plan_id` to a code matching no plan → employee create is **403**,
  not unlimited. This is the BUG-307 regression arm; a mutant returning "unlimited" must fail.
- **FR-24:** with **zero** plan rows configured, the same call must **succeed** — "no plans" is not a
  configuration error.
- **AC-3 (must fail until built):** drive a tenant past `max_api_calls_per_month` and assert the request
  is still served. This documents FR-22's gap; it is not a passing control.
- **FR-18:** cap 20 for `employee` → the 21st `employee` custom field is refused while the 1st `department`
  field still succeeds (proves the cap is per-`EntityType`, not global).
- **FR-17:** upload to exactly the limit → allowed (`>` not `>=`); one byte over → 403
  `storage_quota_exceeded`. At 80% assert a **successful** upload carrying a warning.
- **FR-28/FR-29:** exceed the email cap → a non-mandatory email produces a `Suppressed` delivery row and
  **no 4xx**; a mandatory password-reset in the same state is still `Sent`.
- **FR-33/FR-34:** edit a plan's module list → running tenants' `enabled_modules` change in the same
  transaction. Edit only the price → assert the sweep did **not** run (`UpdatedAt` unchanged).
- **FR-39/FR-40:** tenant with 150 employees downgraded to a 100 cap → all 150 remain readable, and the
  151st create is refused.
- **AC-5 (mandatory isolation):** two tenants, different plans, interleaved concurrent requests — each is
  gated by its own plan; assert no entitlement bleed and that tenant A's usage never appears in B's
  gauge. Include the `PayrollReportExport` storage arm, which previously summed every tenant's bytes
  into one tenant's figure.
