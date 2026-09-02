---
name: payroll-fte-overtime-plumbing
description: GAP-022/G1 — the seams that thread AttendanceSettings policy + Employee.Fte into the payroll run's overtime base, and why a pure-calculator unit test could not catch it
metadata:
  type: project
---

`PayrollOvertimeCalculator.Compute` gained `fte` + `fteScaledBase` trailing-optional parameters (CAL-6)
but `PayrollRunProcessor.ComputeOvertime` kept calling it with four arguments for months, so
`AttendanceSettings.FteScaledOvertimeBase` was persisted, API-settable and completely inert — part-timers'
overtime was priced at the full-time hourly base (under-paid ~50% at 0.5 FTE). Fixed by loading
`AttendancePolicyResolver.LoadAllAsync` once per run and resolving `.For(map, emp.LocationId)` per employee.

**Why:** trailing-optional parameters on a pure calculator make a wiring gap invisible — the code compiles,
every calculator unit test stays green, and only an integration arm through `ProcessAsync` can see it.
`OvertimeFteBaseTests`' own header even conceded it "proves the MATH, not the plumbing".

**How to apply:** whenever a pure domain calculator gains a new optional parameter fed by tenant policy,
the SAME change must add a run-level (Testcontainers) arm asserting the persisted money figure — otherwise
assume the parameter is inert. `AttendancePolicyResolver.LoadAllAsync` + `.For()` is the batched,
per-location policy seam for anything looping over every employee in the tenant (payroll run, jobs); the
per-employee `ResolveForEmployeeAsync` is an N+1 there and also lazily WRITES a settings row, which a
payroll run must never do. Related: [[reference-payroll-proration-shift-aware]],
[[feedback-integration-tests-inmemory]].
