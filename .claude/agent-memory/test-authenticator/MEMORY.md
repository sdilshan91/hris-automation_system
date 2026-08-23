# Test Authenticator Memory

- [Audit depth preference](feedback_audit-depth.md) — user pre-runs mutation checks; focus audits on right-things-coverage + missing boundary arms, not tautology re-checks.
- [BE unit test isolation gotcha](be-unit-test-isolation.md) — InMemory honors query filters but TestDbContextFactory omits TenantInterceptor; write-stamping isolation is unproven by unit tests
- [Absence-arm vacuity](absence-arm-vacuity.md) — a rejection arm asserting an ABSENCE passes both when the control rejects AND when the feature never ran; the positive arm is the real guardian (BUG-308)
- [Two-layer response fabrication](two-layer-response-fabrication.md) — service spec flushes an invented body + component spec mocks that service = the endpoint's real response DTO is never exercised; verify against the controller's `ProducesResponseType`.
- [Static-scan guard vacuity](static-scan-guards-vacuity.md) — source-scan guards in this repo keep shipping as decoration; always test the SPLITTER, not just the token
- [Audit starting point](feedback-audit-starting-point.md) — the user mutation-tests before asking; start from residual risk, don't re-derive their baseline
