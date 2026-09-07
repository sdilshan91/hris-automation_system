---
name: full-replace-put-defaults
description: For a FULL-REPLACE PUT DTO, a mapper's `??` default must equal the DTO's OWN record default — "least-claiming" is the wrong rule there and silently mutates settings
metadata:
  type: feedback
---

On a **full-replace** endpoint (PUT applies every field as sent; an OMITTED field takes the DTO's
default rather than keeping its stored value), a mapper's `?? fallback` must be **the DTO's own
declared default**, not the least-claiming value.

**Why:** the usual money rule ([[feedback-payroll-defaulting]]) says default to the value that
cannot over-claim. That rule is right for read-only projections and **wrong here**, because whatever
the mapper substitutes is what the next save **writes back**. On
`AttendanceSettingsDto.weekdayOvertimeMultiplier` (DTO default `1.5`), a "safer-looking" `?? 1.0`
would not be conservative — a plain GET-then-PUT with nothing edited would **downgrade the tenant's
overtime premium from 1.5x to 1.0x**, i.e. the FE destroys a money setting nobody touched.
Mirroring the DTO default makes an untouched round-trip a provable no-op, because omitting the field
server-side yields the same value. Booleans still default `false` — that IS both the DTO default and
the non-claiming value, so the two rules agree there.

**How to apply:** when you see "FULL REPLACE" / "an omitted field RESETS that setting" / "BUG-117
class" in a DTO doc-comment:
1. Read the C# record initializers and copy those defaults into an exported `*_DEFAULTS` const.
2. Make the request type **required-field**, derived from the generated schema, so a forgotten field
   is a compile error rather than a silent reset:
   ```ts
   type K = Exclude<keyof SettingsWire, 'locationId' | 'locationName'>;   // read-only scope fields
   export type SettingsRequestWire = { [P in K]-?: SettingsWire[P] };
   ```
   (Every generated prop is optional, so a plain object literal missing a key still type-checks —
   this is the guard against that. Related: [[mutually-exclusive-wire-fields]].)
3. In the component, keep the last server-read policy and **refuse to save if the GET failed** —
   saving form defaults over an unread policy is the same defect by another route.
4. Assert **completeness** in the spec: collect the required keys and assert none are missing from
   `req.request.body`. A verb-only assertion proves nothing (ISSUE-500 / [[proving-spec-arm-fails]]).

Worked example: `attendance.models.ts` (`ATTENDANCE_SETTINGS_DEFAULTS`,
`toAttendanceSettingsWire`) and `attendance-settings.component.ts`, ISSUE-438.
