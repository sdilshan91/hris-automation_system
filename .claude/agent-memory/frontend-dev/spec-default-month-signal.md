---
name: spec-default-month-signal
description: Attendance HR components default month()/period signals to the CURRENT month at runtime; specs must assert against component.month(), not a hardcoded yyyy-MM literal
metadata:
  type: feedback
---

Attendance HR views (monthly-summary, payroll-integration, …) initialise their
`month()` signal to the **current** month via a `currentMonthIso()` helper. Mock
fixtures use a fixed period like `'2026-05'` for data *shape*, but the component
still calls the service with `month()` = today's month.

**Why:** US-ATT-009 spec failed `toHaveBeenCalledOnceWith('2026-05')` — actual arg
was `'2026-06'` (the run-date month). The fixture period was a red herring; the
service is called with the live signal value.

**How to apply:** when asserting the month/period arg a component passes to the
service, use `component.month()` (or `component.period()`), not a literal — unless
the test first calls `onMonthChange('2026-05')` to pin it. Pairs with
[[signal-async-dom-detectchanges]].
