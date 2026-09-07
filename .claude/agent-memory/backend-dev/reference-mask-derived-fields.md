---
name: mask-derived-fields
description: When permission-masking a DTO field, grep the entity for computed properties derived from it — RecommendationDto.BudgetCharge re-exposed the exact bonus figure the five masked fields hid (BUG-533).
metadata:
  type: reference
---

`Recommendation.BudgetCharge => BonusAmount ?? IncrementAmount ?? 0m` is an expression-bodied
computed property projected onto `RecommendationDto`. BUG-533's agreed fix was "null the five
compensation fields in `BuildDto` when `CanSeeCompensation` is false" — applied literally, an
HR Officer still received the exact bonus figure through `BudgetCharge`, so the fix would have been
cosmetic.

**How to apply:** before finishing any permission-mask or redaction change, grep the *entity* for
`=>` computed members and the DTO projection for anything downstream of the masked fields — a mask
is only as strong as its derivations. Mask non-nullable derived numerics to `0m` rather than making
them nullable: the wire shape stays identical, so no OpenAPI regeneration and no FE change
(see [[regen-openapi-with-any-api-change]] for why that matters here).

Prove it with its own mutation arm. Reverting only the derived-field mask must go RED on its own —
otherwise nothing binds it and the leak silently returns. See [[guards-must-be-mutation-proven]].
