---
name: reference-overtime-default-multiplier
description: BUG-456 — what PayrollOvertimeCalculator's defaultMultiplier actually prices, why the "legacy empty-buckets" path is unreachable, and the ARCH-004 KnownInert coupling that fires when you fix an inert optional parameter
metadata:
  type: reference
---

# Overtime `defaultMultiplier` — reachability and the ARCH-004 coupling

`PayrollOvertimeCalculator.Compute(defaultMultiplier)` is the **fallback rate, not the rate**. It applies in
exactly two places, and only one of them is reachable through the live payroll pull:

1. **Empty per-multiplier breakdown + positive approved minutes** (the "legacy attendance" branch the XML doc
   describes). **UNREACHABLE via `AttendancePayrollService`**: `ApprovedOvertimeMinutes` and
   `OvertimeMultiplierDetails` are both projected from the *same* `OvertimeAgg`, so an empty breakdown implies
   zero minutes. Do not write a test that tries to drive this through the run — it cannot be seeded.
2. **A bucket key that is not a positive decimal** — `ParseMultiplier`'s `m > 0m` guard. Bucket keys are
   `OvertimeRecord.Multiplier.ToString("0.##")`, so this fires when an APPROVED record carries `Multiplier <= 0`.
   Not producible by today's two creation paths (both go through `OvertimeMultiplierResolver` off
   validator-bounded settings), but entirely producible by an import/back-fill/pre-resolver row. **This is the
   shape to seed** when you need to exercise the default at run level.

**Why: a finding that says "the legacy path falls back to a hardcoded 1.5x" is describing the *code*, not the
*reachable* behaviour.** BUG-456's premise was right about the omission and slightly wrong about the route;
saying so is more useful than repeating the finding's framing.

**How to apply:** when threading a tenant setting into `Compute`, resolve it off the **same**
`AttendancePolicyResolver.For(map, locationId)` row that `FteScaledOvertimeBase` already uses — one resolve, not
two. Use the batched `LoadAllAsync`/`For` pair, never `ResolveForEmployeeAsync`, which lazily **creates** the
tenant-default row and would make a payroll run write attendance policy as a side effect (assert
`AttendanceSettings.Count() == 0` to pin that).

## The ARCH-004 baseline is a tripwire on both sides

`HRM.ArchitectureTests/InertOptionalParameterTests.cs` keeps a `KnownInert` allow-list *and* a second test,
`KnownInert_baseline_has_no_stale_entries`, that goes **RED when you fix an entry**. So wiring an inert optional
parameter is a two-file change: the call site **and** the baseline entry. Forgetting the second half fails the
build in a project (`HRM.ArchitectureTests`) most payroll filters don't even run.

Related: [[reference-payroll-fte-overtime-plumbing]], [[feedback-guards-must-be-mutation-proven]].
