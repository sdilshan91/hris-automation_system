# Test Authenticator Memory

- [Audit depth preference](feedback_audit-depth.md) — user pre-runs mutation checks; focus audits on right-things-coverage + missing boundary arms, not tautology re-checks.
- [BE unit test isolation gotcha](be-unit-test-isolation.md) — InMemory honors query filters but TestDbContextFactory omits TenantInterceptor; write-stamping isolation is unproven by unit tests
- [Absence-arm vacuity](absence-arm-vacuity.md) — a rejection arm asserting an ABSENCE passes both when the control rejects AND when the feature never ran; the positive arm is the real guardian (BUG-308)
