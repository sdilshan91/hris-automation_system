---
name: wire-migration-envelope-and-defaults
description: Migrating a feature's *.models.ts to generated Schema<> wire types — the envelope already unwraps, so alias the INNER dto; and how to pick mapper defaults
metadata:
  type: project
---

When migrating a feature off `http.get<IFoo>()` casts onto generated contract types
(`import type { Schema } from '@core/api'`), two things are easy to get wrong and cost a whole pass:

**1. Alias the INNER dto, never the envelope.** `apiEnvelopeInterceptor` (core/interceptors) strips
`{ success, data }` globally before any service sees the body. So an endpoint the contract declares as
`ApiResponseOfAttendanceShiftDto` must be typed `Schema<'AttendanceShiftDto'>`. Typing the envelope
compiles and then fails at runtime on every field.
Corollary for specs: `req.flush(x)` delivers `x` straight to the mapper, so **every flushed body in a
service spec is a WIRE body**. Fixtures shaped like the view model are objects the server has never sent —
that is precisely how renames survive a green suite.

**Why:** the generated types come from the API's own OpenAPI document, and every property is optional (`?`)
because Swashbuckle emits no `required` for non-nullable C# reference types. So the types catch WRONG and
MISSPELLED fields — the whole of the observed drift — but prove nothing about presence. Every field needs
an explicit default, and a default is a decision.

**2. Pick the default that cannot over-claim, and comment why at the site.** In pay/approval domains the
asymmetry is what matters, not the "safe-looking" value:
- a `succeeded`/`isApproved` flag defaults **false** — `?? true` removes a request from a manager's queue
  and toasts success the backend never confirmed;
- a lock/freeze flag defaults **true** (fail closed) — but only when the legitimate "never locked" case is
  a *null body*, which the service must short-circuit (`res ? mapX(res) : null`) rather than mapping;
- a poll `status` defaults to the NON-terminal value so the caller keeps polling — and that requires the
  poll to be bounded, or you have traded a false success for an infinite loop;
- a pay multiplier has no honest number: `0` renders "0x", `1` silently renders straight time. Use `NaN`
  when the formatter already renders it as "—", and verify nothing does arithmetic on it;
- a status/enum union narrows through a guard with a documented fallback, never a blind `as`. The fallback
  must read as "unknown", never as the permissive value.

**How to apply:** derive the DTO names from `contracts/openapi/hrm-v1.json` `components.schemas`, worked
examples in `features/payroll/models/payslip.models.ts` and `features/admin/tenants/models/tenant.models.ts`.
A VM field with no wire source is a FINDING, not a `?? null` — see [[wire-migration-parallel-split]].
