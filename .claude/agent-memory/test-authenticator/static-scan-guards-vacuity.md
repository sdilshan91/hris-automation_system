---
name: static-scan-guards-vacuity
description: Static source-scan guard tests in HRM (PlanLimitLookupUsageGuardTests, EmployeeFieldAuditPairingGuardTests) keep turning out to be decoration — always test the SPLITTER, not just the token
metadata:
  type: project
---

This repo uses "static source-scan guard" xUnit tests — a test that reads `src/**/*.cs` as text and asserts
the **absence** of a pattern (e.g. a field-audit write with no paired central audit write). Three separate
versions of `EmployeeFieldAuditPairingGuardTests` shipped before one could fail: a brace-depth splitter broke
on interpolated strings (yielded zero regions → vacuous pass), and a token that was a **substring** of the
thing it was scanning for made the check unfalsifiable.

**Why it matters:** an absence-assertion over an empty input set always passes, and reads like coverage. The
usual mitigation here — a "positive guardian" arm asserting the writers still exist — only covers
*file-discovery* vacuity. It does **not** notice a splitter/regex regression, which is the failure mode that
has actually occurred twice.

**How to apply:** when auditing one of these, always (1) re-implement the splitter in a throwaway script and
check every occurrence of the scanned token is attributed to a region, (2) check the guard's pattern is
anchored on something refactor-stable (the entity type `new XLog`, not the receiver `_dbContext.X`), and
(3) check whether "delegates to a helper" is inferred from a bare name mention — that turns any same-file
method that happens to write centrally into a free pass. Related: [[feedback-audit-starting-point]].
