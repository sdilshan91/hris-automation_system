---
name: reference-sanitize-on-write-posture
description: Extending IHtmlSanitizer to a service that has none — the seven adopters, the three blank-check shapes and why the length check must move onto the sanitized value
metadata:
  type: reference
---

Seven services now sanitize free text on write: `OfferService`, `InterviewService`, `VacancyService`,
`ApplicantService`, `ReviewSignoffService`, and (ISSUE-144(b)/ISSUE-149(b)) `GoalProgressService` +
`RecommendationService`. `IHtmlSanitizer` → `GanssHtmlSanitizer` is already a **singleton** in
`HRM.Infrastructure/DependencyInjection.cs`, so adding the ctor param needs no DI change — only the
hand-rolled test construction sites break, and there are usually 2 per service (a `Service(...)` factory
plus a standalone `new XService(...)` in the unresolved-tenant arm).

**The ordering rule (from ISSUE-121) and its three shapes.** Sanitize BEFORE the blank check, always —
a string that is nothing but a payload sanitizes to empty and would otherwise be stored as a blank row.
The blank check wears three different disguises; find yours before editing:
1. **inline ternary** at the assignment — `X = string.IsNullOrWhiteSpace(in.X) ? null : in.X.Trim()`;
2. **a `Trim()` helper** that already returns null for blank (`RecommendationService:Trim`) — just wrap:
   `Trim(_sanitizer.Sanitize(x))`, and the ordering falls out for free;
3. **an early-return required-gate** far above the assignment (`GoalProgressService.AddCommentAsync`
   body_required; `RecommendationService` FR-3 justification_required). This is the dangerous one: leave
   it on the raw input and a pure-`<script>` value *satisfies a mandatory-field rule* and then stores
   nothing. Hoist the sanitize above the gate and check the sanitized value.

**Move the length check onto the sanitized value too.** Ganss HTML-**encodes** text (`&`→`&amp;`,
`<`→`&lt;`), so sanitizing can make a string LONGER. Checking the raw length against a bounded column
(`goal_comment.body` 500, `recommendation.custom_type_label` 100) turns a valid request into a
`DbUpdateException` 500. Checking the sanitized length turns it into a 422. Not every field has a
service-level length check — `justification` (4000) and `custom_type_label` (100) are validator-only,
so that expansion risk is still open across all seven adopters.

**Benign text really does round-trip byte-for-byte** — including the em dash in the pre-existing
`RecommendationServiceTests` assertion `"Retention risk — matched a competing offer."`, which became a
free over-sanitization guard the moment the sanitizer went in. Ordinary punctuation (`-`, `%`, `()`,
`;`, `!`) is safe to use in benign-arm literals; **avoid `&`, `<`, `>`** in a byte-for-byte assertion.

**How to apply:** when a story says "sanitize field X", grep for the blank check first, then decide
which of the three shapes it is. Related: [[reference-review-signoff-agreed-actions]],
[[reference-recommendation-write-audit]], [[feedback-guards-must-be-mutation-proven]].
