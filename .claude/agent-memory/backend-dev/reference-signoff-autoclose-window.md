---
name: reference-signoff-autoclose-window
description: ENH-012 sign-off auto-close window — the 0-semantics and override-precedence decisions, and the config-override pattern for making a Hangfire-only setting testable
metadata:
  type: reference
---

`AppraisalCycle.SignoffAutoCloseDays` (US-PRF-006 BR-3) shipped as a column that only the Hangfire
sweep read — no API surface, no trigger endpoint. ENH-012(b) exposed it and added a config override.

**0-semantics decision.** Internally 0 is well-defined: "no grace period, close on the next sweep".
On the **tenant-facing API it is REJECTED** (`invalid_signoff_autoclose_days`, range 1..365 via
`AppraisalCycle.Min/MaxSignoffAutoCloseDays`).
**Why:** an HR admin typing 0 into "days to sign off" almost certainly means *disable*, not *close
everything instantly* — accepting it makes the most destructive reading the one the system performs.
Rejecting removes the ambiguity from the surface where it would become a support ticket, while
keeping one unambiguous internal meaning for the override.
**How to apply:** there is still **no way to disable** auto-close (a tenant must set 365). If someone
asks for a disable value, it needs a new nullable/flag, not a reinterpretation of 0.

**Override precedence decision.** `Performance:SignoffAutoCloseDaysOverride` **beats** the per-cycle
value. **Why:** the column is `NOT NULL DEFAULT 7`, so every cycle always has a value — an override
that only filled a gap would apply to nothing and be dead code, which is the exact problem it exists
to fix. It cannot change production silently because it is absent from every committed
`appsettings*.json`, and applying it logs a WARNING on every sweep. Range 0..365 (wider than the
tenant range, so a test can force immediate close); out-of-range is warned and **ignored**, degrading
to the tenant's window rather than closing early.
**How to apply:** reuse this shape — override-wins + absent-from-config + warn-on-apply +
ignore-out-of-range — whenever a per-entity setting also needs a test/ops escape hatch. The blessed
precedent is `Recruitment:ScorecardLockPeriodHours` (`ScorecardService.ResolveLockPeriodHours`).

**Behaviour tests need TWO directions.** A single "cycle window honoured" arm is weak: it passes
against a hardcoded default whichever way it leans. Pin a window LONGER than the default (expect
0 closed) *and* one SHORTER (expect 1 closed). Verified: mutating the per-cycle read to always
return the default turns both red. See [[feedback-guards-must-be-mutation-proven]].

The auto-close sweep's clock is already a parameter (`AutoCloseOverdueAsync(DateTime nowUtc)`) — that
is the test clock, no `TenantClock` seam needed. See [[reference-attendance-summary-freshness]].
