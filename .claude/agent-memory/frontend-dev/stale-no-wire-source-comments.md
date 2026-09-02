---
name: stale-no-wire-source-comments
description: "// No wire source — defaulted (reported)" comments in *.models.ts mappers are unverified claims; grep the DTO in api-types.ts before trusting one
metadata:
  type: feedback
---

Treat every `// No wire source — defaulted (reported)` / "genuinely absent from THIS
payload" comment in a `*.models.ts` mapper as an **unverified claim**, not evidence.
Grep the specific DTO block in `src/app/core/api/generated/api-types.ts` before
believing it — and before filing a backend gap.

**Why:** ISSUE-379 and fix-queue item G8 were both filed as *backend* gaps for fields
the payload was already carrying (`teamRanking`, `availableExportFormats`,
`ratingScaleMax`, `cycleName`, `finalScore`). Hardcoded `[]`/`''`/`0` under a confident
comment is self-sealing: the next reader trusts the comment, so the widget stays blank
for weeks. In the performance mappers, 7 of 8 such comments checked were wrong.

**How to apply:** (a) verify per-DTO — a field name existing somewhere in the 40k-line
generated file is not proof it is on *that* payload; (b) when you fix one, **rewrite the
comment in the same diff** — leaving a false comment beside a fixed line recreates the
bug; (c) if a field really is absent, say so and name the DTO you checked, so the next
person can re-verify cheaply.

Related: [[wire-migration-envelope-and-defaults]], [[wire-export-format-token-mismatch]].
