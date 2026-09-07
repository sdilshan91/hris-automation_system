---
id: US-REC-011
module: Recruitment
priority: Should Have
persona: Tenant Admin / Recruiter
status: draft
created: 2026-09-07
sprint: backlog
acceptance_criteria_count: 6
findings: ISSUE-116
---

# US-REC-011: Per-Tenant Interview Reminder Lead Time

> **Traceability:** this is the **parked half** of [[ISSUE-116]]. The other half — the idempotency guard on
> both reminder jobs — ships as a T4 fix with no schema change
> (`docs/QA/plans/GAP-CLOSURE-QUEUE.md:485`). This story is the part that needs a tenant setting and a
> migration, and it closes a **BR-5 promise US-REC-005 already makes**.

## 1. Description
**As a** Tenant Admin,
**I want to** configure how many hours before an interview the reminder notification fires for my
organisation,
**So that** the reminder matches how my company actually schedules — a 24-hour default is wrong for a firm
that books same-day panels and equally wrong for one that wants three days' notice.

## 2. Background / Problem Statement
US-REC-005 **BR-5** states: *"The reminder notification lead time (default 24 hours) is configurable at the
tenant level via module configuration (§35.2.9)."* It is not. The value is read from app-global
configuration:

`src/backend/HRM.Infrastructure/Services/InterviewService.cs:474`
```csharp
var leadHours = _configuration.GetValue<int?>("Recruitment:InterviewReminderLeadHours") ?? DefaultReminderLeadHours;
```

`Recruitment:InterviewReminderLeadHours` is a single deployment-wide key. On a multi-tenant platform that
means **every tenant on the instance shares one lead time**, and no tenant admin can change it. The story is
a documented requirement that was never built, not a new idea.

The tech doc supports the tenant-level home: §35.2.9 lists Recruitment among the per-tenant *module
configurations*.

## 3. Preconditions
- The tenant has the Recruitment module enabled.
- The caller holds `Tenant.ManageSettings` (the permission already guarding the hiring-settings endpoint).
- Interview scheduling (US-REC-005) is operational.

## 4. Where the setting lives — DECIDED
**It belongs as a typed column on the `Tenant` entity, surfaced through the existing hiring-settings
endpoint. Not a new entity, and not an EAV table.**

- **No EAV.** `TenantSettingsService` states the convention explicitly (`TenantSettingsService.cs:19-21`):
  tenant settings are realised *"as TYPED COLUMNS on the `Tenant` entity (matching the codebase convention —
  no EAV table)"*. A key/value settings table is ruled out by the codebase, not by preference.
- **Not a new `RecruitmentSettings` entity.** No such entity exists, and standing one up for a single
  integer would be an unrequested abstraction. If recruitment settings later grow past a handful of scalars,
  extracting them is a cheap follow-up; doing it now is speculative.
- **`Tenant` already carries the recruitment-scoped precedent.** `Tenant.AutoCreateUserOnHire`
  (`Tenant.cs:259`) is a recruitment setting living as a typed column, exposed by
  `PUT /api/v1/tenant/settings/hiring` (`TenantSettingsController.cs:115-117`, `[RequirePermission("Tenant.ManageSettings")]`)
  through `HiringSettingsDto(bool AutoCreateUserOnHire, bool PublicCareersEnabled)`.
  The lead time extends that DTO — **no new endpoint, no new controller, no new permission**.

## 5. Acceptance Criteria (IEEE 830 §3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A Tenant Admin is configuring hiring settings | They `PUT /api/v1/tenant/settings/hiring` with `interviewReminderLeadHours = 48` | The value is persisted on that tenant's `Tenant` row and returned in the `HiringSettingsDto` response |
| AC-2 | Tenant A has a lead time of 48 and Tenant B has 4 | An interview is scheduled in each tenant for the same start time | Tenant A's reminder is scheduled for 48 h before and Tenant B's for 4 h before — the two tenants observe **different** fire times from the same input |
| AC-3 | A tenant has never configured a lead time | An interview is scheduled | The reminder fires at the **24-hour default** (`DefaultReminderLeadHours = 24`, `InterviewService.cs:34`), preserving today's behaviour byte-for-byte for every existing tenant |
| AC-4 | A Tenant Admin submits a lead time that is negative, zero, or absurdly large | The request is validated | It is rejected **422** with a field-level message; the stored value is unchanged. (Today an invalid app-global value is silently coerced to the default at `InterviewService.cs:475` — configuration errors must be *reported* at the settings boundary, not swallowed at the scheduling boundary) |
| AC-5 | An interview already has a reminder scheduled | The tenant's lead time is subsequently changed | The already-scheduled reminder is handled per BR-4 (unchanged, deterministically) — a settings edit does not silently retime interviews that are already booked |
| AC-6 | A user in Tenant A is authenticated | They attempt to read or write Tenant B's hiring settings | Only Tenant A's row is reachable; `LoadCurrentTenantAsync` resolves strictly by `ITenantContext.TenantId` and there is no tenant id parameter to manipulate (`TenantSettingsService.cs:22-27`) |

## 6. Functional Requirements (IEEE 830 §3.2)
- FR-1: The system SHALL add a nullable typed column `interview_reminder_lead_hours` (integer) to the `tenants` table via a CLI-generated EF migration.
- FR-2: The system SHALL extend `HiringSettingsDto` and `UpdateHiringSettingsRequest` with the lead time, served and updated by the existing `PUT /api/v1/tenant/settings/hiring` endpoint under `Tenant.ManageSettings`.
- FR-3: `InterviewService.ScheduleReminder` SHALL resolve the lead time as **tenant value → app-global config → 24 h const**, replacing the current two-step chain at `InterviewService.cs:474`.
- FR-4: The system SHALL validate the lead time at the settings boundary against the range agreed in OQ-1 and reject out-of-range values with a field-level error.
- FR-5: The system SHALL audit lead-time changes with before/after, consistent with every other mutating method on `TenantSettingsService` (NFR-4 of US-ADM-006).
- FR-6: The system SHALL evict the cached tenant config key `t:{tenantId}:config` on update, as the sibling settings methods already do.
- FR-7: The **frontend SHALL gain a "Hiring" tab** on the tenant settings page with a lead-time input. There is no hiring tab and no corresponding service method today, so the entire FE surface for this endpoint — including the existing `autoCreateUserOnHire` toggle it already serves — is net-new work inside this story.

## 7. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: **Tenant isolation** — the setting is read and written only through `LoadCurrentTenantAsync`, which resolves by `ITenantContext.TenantId`; there is no cross-tenant read path and the cache key is tenant-prefixed.
- NFR-2: Resolving the lead time SHALL NOT add a database round trip per scheduled interview — read it from the already-cached tenant config, not with a fresh query inside `ScheduleReminder`.
- NFR-3: The default SHALL be preserved exactly for unconfigured tenants; this change must be behaviour-neutral on upgrade.
- NFR-4: The settings form SHALL meet WCAG 2.1 AA (labelled numeric input, error text programmatically associated).

## 8. Business Rules
- BR-1: The reminder lead time is a **per-tenant** setting; the app-global `Recruitment:InterviewReminderLeadHours` key remains only as the fallback for tenants that have not configured one.
- BR-2: The default remains 24 hours (US-REC-005 BR-5).
- BR-3: Only `Tenant.ManageSettings` holders may change it.
- BR-4: Changing the lead time affects **subsequently scheduled** reminders. Already-scheduled reminders are not retimed — see OQ-2 if the product wants otherwise.
- BR-5: An invalid value is rejected at the settings boundary, never silently coerced.

## 9. Offer-side sibling — EXPLICITLY OUT OF SCOPE
`OfferService.cs:51` holds `private const int ExpiryReminderDaysBefore = 3;` — a bare constant with **no
`IConfiguration` read at all**, so it is not even app-globally tunable. It is deliberately **not** in this
story:

1. **Different unit and semantics** — days-before-*expiry* on an offer, versus hours-before-*start* on an interview. They are not one setting with two call sites, and the queue's own re-check records that "the lead-time halves are not comparable" (`GAP-CLOSURE-QUEUE.md:446`).
2. **The codebase already ruled on it.** The docstring at `OfferService.cs:47-50` states that making it tenant-configurable "is a SEPARATE concern and is intentionally NOT built here". Folding it in would reverse a recorded decision inside an unrelated story.
3. Bundling them would double the migration surface and the AC count for no shared logic.

**It should be filed as its own follow-up.** Flagged as an out-of-lane `GAP` in the run report so it is
tracked rather than lost in this paragraph.

## 10. Data Requirements
**tenants (added column):**
| Field | Type | Notes |
|-------|------|-------|
| interview_reminder_lead_hours | integer, nullable | NULL ⇒ fall back to app-global config, then to the 24 h const |

**Input:** `interviewReminderLeadHours` on `UpdateHiringSettingsRequest`.
**Output:** the same field echoed on `HiringSettingsDto`.

## 11. UI/UX Notes
- New "Hiring" tab on the tenant settings page, alongside the existing tabs.
- Numeric input labelled "Interview reminder lead time (hours)" with helper text naming the default and the accepted range.
- The tab also surfaces the existing `autoCreateUserOnHire` toggle, which currently has a live backend endpoint and **no UI at all**.

## 12. Dependencies
- **US-REC-005** — owns interview scheduling and states BR-5, the requirement this closes.
- **US-ADM-006** — owns the tenant settings surface, the audit and cache-eviction conventions followed here.
- **US-REC-010** — owns `PUT /settings/hiring` (the `autoCreateUserOnHire` toggle) that this extends.
- **[[ISSUE-116]]** — the idempotency half ships separately; the two halves must not be conflated.

## 13. Open Questions — MUST be answered before this story is `ready`
- **OQ-1 (valid range).** What are the minimum and maximum accepted lead times? A lower bound above 0 is
  clearly needed (a 0-hour reminder fires at start time). *Recommendation (confidence 65%):* accept 1–168
  hours (1 h to 7 days). This is a product call and it is an acceptance criterion (AC-4), so it cannot be
  guessed.
- **OQ-2 (retiming existing reminders).** When a tenant changes the lead time, should already-scheduled
  reminders be rescheduled? BR-4 currently says no. *Recommendation:* keep "no" — retiming would require
  re-enumerating every future interview and rewriting Hangfire jobs, and the `ReminderJobId` swap machinery
  already exists for the *reschedule* case only. If the product wants retiming, that is a materially bigger
  story and should be split out.

## 14. Assumptions & Constraints
- Migrations are CLI-generated (`dotnet ef migrations add`); the snapshot is not hand-edited.
- `Tenant` rows are not tenant-filtered (the row *is* the tenant), so isolation relies on `LoadCurrentTenantAsync` resolving by `ITenantContext.TenantId` — this is the established pattern and must be followed rather than reinvented.
- The reminder job itself is unchanged by this story; only the computation of `fireAtUtc` moves.

## 15. Test Hints
- **The isolation-relevant arm:** configure Tenant A = 48 and Tenant B = 4, schedule an interview in each at the same UTC start, and assert two *different* `fireAtUtc` values. A single shared value is the bug this story exists to remove.
- Unconfigured tenant → assert the fire time is exactly start − 24 h (regression guard for AC-3).
- Fallback chain: with the tenant value NULL and the app-global key set to 6, assert 6 h; then set the tenant value and assert it wins.
- Validation: submit 0, −1, and a value above the agreed maximum → 422 each, with the stored value unchanged.
- Audit: change the lead time and assert an audit row with before/after.
- Cache: change the lead time and assert `t:{tenantId}:config` is evicted and the next schedule uses the new value.
- Isolation: as a Tenant A admin, attempt to read/write hiring settings while presenting Tenant B context; assert Tenant A's row is the only one reachable.
- FE: the Hiring tab renders, persists both fields, and shows a field-level error for an out-of-range value.
