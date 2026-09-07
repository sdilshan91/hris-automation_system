---
name: hrreport-sql-pushdown
description: ENH-455 — pushing HrReportService aggregation from LINQ-to-Objects into Postgres GROUP BY; which constructs translate, the empty-group trap, and how to capture the SQL as evidence
metadata:
  type: reference
---

`HrReportService` reports were written as "materialise the whole filtered employee population with
`ToListAsync`, then `GroupBy` in memory". ENH-455 pushed three of them into SQL. What is worth
keeping:

**What translates cleanly on Npgsql (verified by logged SQL, not assumed):**
- `GroupBy` on a composite anonymous key of `[Guid, enum, enum]` where both enums are stored via
  `HasConversion<string>()` → `GROUP BY department_id, employment_type, status` with
  `count(*)::int`. Converted enums are fine as group keys.
- `.Join(scopedEmployeeQuery, h => h.EmployeeId, e => e.Id, (h, e) => new {...})` → `INNER JOIN
  (SELECT ... FROM employees WHERE <tenant filter>)`. This is the right replacement for the
  "materialise employees → send ids back as `IN (...)`" round trip. **Both sides keep their own
  global query filter in the emitted SQL**, so tenant isolation survives the join — confirm it in
  the logged SQL rather than trusting it.

**What deliberately stays client-side, and why it is the correct answer:**
- `IsSeparatedValue` / `IsVoluntary` (BR-4/BR-3) are free-text keyword rules over
  `NewValue`+`Reason` with an `Enum.TryParse` arm. No faithful SQL translation; a `LIKE`
  approximation would change *which rows count as a separation*.
- Because separations are therefore already in memory, the month-truncation
  `new DateTime(d.Year, d.Month, 1)` costs nothing there — no reason to gamble on date-part
  translation. Partially pushed down + correct beats fully pushed down + throwing.
- Keep `IsActive(status)` in C# and instead `GroupBy(e => e.Status)` in SQL, then fold client-side
  over the ≤8 status buckets. That pushes the COUNT down **without** duplicating the BR-4 rule as
  an inline `Status == Active || Status == Probation` predicate.

**The empty-group trap — the real correctness risk in this kind of change.** LINQ-to-Objects code
that enumerates `Enum.GetValues<T>()` emits a 0 for a type with no employees; SQL `GROUP BY` emits
**no row at all**. Rewriting such a loop to be driven off the SQL groups silently drops those zero
points. Drive the enumeration off the enum and fold the buckets into it. See
[[feedback-guards-must-be-mutation-proven]] — this exact mutation was proven RED.

**Capturing SQL as evidence.** Add `.LogTo(sink, [DbLoggerCategory.Database.Command.Name],
LogLevel.Information)` to the test's `DbContextOptionsBuilder` and run the service against the
Testcontainers fixture; the emitted statements land in the sink verbatim. `ToQueryString()` does
not help when the query is private inside a service. Related: [[reference-reports-postgres-port]],
[[feedback-integration-tests-inmemory]].
