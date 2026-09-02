---
name: reference-datetime-json-boundary
description: BUG-431 — global UTC DateTime JSON converter at the API boundary; what it covers (request bodies) and what it does NOT (query-string dates, SignalR)
metadata:
  type: reference
---

`HRM.Api/Json/UtcDateTimeJsonConverter.cs` + `UtcNullableDateTimeJsonConverter` are registered globally in
`Program.cs` `AddControllers().AddJsonOptions(...)`, next to the `JsonStringEnumConverter`. On read they coerce
`Kind=Unspecified` → UTC (stamp, no shift) and `Local` → `ToUniversalTime()`. Write is left at the framework
default, so no response shape changes.

**Why:** a date-only JSON value (`"2026-01-01"` — what an Angular `<input type="date">` emits) deserializes as
`Kind=Unspecified`, and Npgsql refuses to write that to a `timestamptz` column
(`ArgumentException: Cannot write DateTime with Kind=Unspecified…`) → unhandled `DbUpdateException` → HTTP 500,
while the same value with a `Z` suffix returned 201. 26+ bare `DateTime` request properties were exposed to it.

**How to apply / boundaries worth knowing before re-litigating this:**
- Covers **request-body JSON only**. Query-string `DateTime` params (18 of them, `[FromQuery] DateTime?`) bind
  through MVC's own `DateTimeModelBinder` (`AdjustToUniversal`), never through JSON converters — those yield
  `Kind=Utc` but interpret an offset-less value as **server-local**, so they can shift a filter boundary on a
  non-UTC host rather than 500. `AuditLogService.cs:~208/215` hand-normalises for exactly this reason.
- Malformed dates were **already** a 400 before the fix: `SystemTextJsonInputFormatter` catches `JsonException`
  → ModelState → `ValidationFilter`. Keep converter failures as `JsonException` (use `TryGetDateTime`, not
  `GetDateTime`, whose `FormatException` is a riskier path).
- `Program.cs`'s **second** converter site is SignalR `AddJsonProtocol` — a separate hub-payload pipeline, not a
  request-deserialisation path (`NotificationHub` has no client-invokable methods). Deliberately not registered there.
- Do **not** "fix" this by switching DTO properties to `DateOnly` — that is a wire-contract change.

Related: [[feedback-integration-tests-inmemory]], [[reference-attendance-module]].
