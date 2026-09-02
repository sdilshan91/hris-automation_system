---
name: mutually-exclusive-wire-fields
description: Replace-mode wire contracts — omit the losing field entirely (never send it empty), and hand-write request interfaces because generated request schemas are all-optional
metadata:
  type: feedback
---

When a BE contract offers two **mutually exclusive** task/item-set fields (legacy "extras" vs. new
"authoritative set" — e.g. `additionalTasks` vs `resolvedTasks` on assign-checklist), the FE must send
**exactly one and omit the other entirely** — not `[]`.

**Why:** the BE deliberately 400s on "both supplied" rather than picking a winner, because silently
discarding one of two sets is invisible data loss (that *was* BUG-441: the screen echoed its whole task
list into the legacy "extras" field, so the server created `template.Tasks` **plus** the echo — every
template task twice, all at `startDate + 0`, destroying the officer's inline due-date edits). Sending
`[]` may pass today's validator (`Must(t => t is null || t.Count == 0)`) but it encodes "I am also using
the legacy path", which is a lie the next validator tightening will break.

**How to apply:**
- Make BOTH fields optional in the FE interface, document the exclusivity on each, and have the mapper
  set only one key. `JSON.stringify` drops `undefined`, so an omitted key is genuinely absent on the wire.
- Assert absence explicitly in the spec: `expect(arg.additionalTasks).toBeUndefined()`.
- A replace-mode row carries a **concrete date**, not an offset — the offset shape is what could not
  express the user's edit in the first place. Also check for fields deliberately ABSENT from the new
  contract (`responsibleUserId` is re-resolved server-side); don't echo them back just because the
  form control holds one.

**Do NOT alias the generated schema for a request body.** `api-types.ts` emits every request field as
optional (`title?: string | null`), so aliasing it lets a payload compile with no `dueDate` — exactly the
omission that is a 400. Hand-write the request interface with the genuinely required fields REQUIRED, and
comment it as mirroring the generated schema name so drift is greppable. (This is a deliberate exception
to the "migrate hand-written models to generated types" push in `.claude/rules/frontend.md`, which is
sound for *response* types where the server owns nullability.)

See [[proving-spec-arm-fails]] for the before/after protocol used to prove the corrected arm fails first,
and [[wire-migration-envelope-and-defaults]] for the sibling wire-shape rules.
