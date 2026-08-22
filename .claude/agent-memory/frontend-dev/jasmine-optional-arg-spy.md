---
name: jasmine-optional-arg-spy
description: Jasmine toHaveBeenCalledWith records the EXACT arg count — don't assert a trailing `undefined` for an omitted optional arg
metadata:
  type: feedback
---

When a component calls a service method and omits a trailing optional argument
(e.g. `bulkApprove(ids)` where the signature is `bulkApprove(ids, comment?)`),
the Jasmine spy records a **one-element** args array — not `[ids, undefined]`.

So `expect(spy).toHaveBeenCalledWith(ids, undefined)` FAILS with
"Expected $.length = 1 to equal 2".

**Why:** Jasmine compares the actual `arguments` object length, and an omitted
optional param is genuinely absent, not `undefined`.

**How to apply:** Assert only the args actually passed —
`expect(spy).toHaveBeenCalledWith(ids)`. To assert the omission explicitly when
the component DOES pass an explicit `undefined` (e.g. a normalized empty comment),
make the source pass it explicitly; otherwise match the real call shape.
