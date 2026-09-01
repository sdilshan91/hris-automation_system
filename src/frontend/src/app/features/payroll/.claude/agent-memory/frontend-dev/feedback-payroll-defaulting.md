---
name: feedback-payroll-defaulting
description: In payroll wire→view-model mappers a default is a decision — omit request fields rather than sending a guess, and never collapse a nullable money bound to 0
metadata:
  type: feedback
---

When writing a wire→view-model mapper (or a view-model→request mapper) on a **money path**, pick the
least-claiming default, and omit rather than guess.

**Why:** the admin slice shipped a bug by defaulting an unknown lifecycle status to `'terminated'`,
painting red badges on healthy tenants. The payroll equivalents are worse because they are money:
`slabTo ?? 0` collapses the unbounded top tax band to an empty range (top earners pay nothing);
`wageCeilingAnnual ?? 0` means "ceiling of zero", i.e. no EPF contribution at all; and sending
`isActive: false` on a create body because the FE has no source for it would create every statutory
rule inactive — where **omitting** the key lets the server apply its own `= true` default.

**How to apply:**
- Nullable-on-the-schema bounds (`slabTo`, `wageCeilingAnnual`) → `?? null`, never `?? 0`.
- Non-nullable C# numerics are `?` in the generated types only because of the Swashbuckle artifact
  (see `core/api/index.ts`); `?? 0` there is filling a generator artifact — say so in a comment.
- Absent boolean flags → `false`. Absent enums → the catch-all member (`'Custom'`), never a
  meaningful one; an unknown rule must not hijack a typed editor.
- Request bodies: if the FE has no source for a field, **leave the key out**. Do not send a
  fabricated value, and do not invent a source. Flag it instead.

Related: [[project-payroll-statutory-contract-traps]].
