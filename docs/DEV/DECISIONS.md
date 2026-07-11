# DEV — DECISIONS

- **EF migrations:** never hand-write — always `dotnet ef migrations add` (see [`../../CLAUDE.md`](../../CLAUDE.md)).
- **Secrets:** `.env` / user-secrets only, never committed (`secret-guard` hook enforces).
- **Git hooks / test integrity:** no `--no-verify`; never weaken a test/config to force a green gate (`no-verify-guard`, `test-integrity-guard`, `config-protection-guard`).
- **Retry-vs-tracked-state (BUG-068 class):** manual tx / row-lock under `EnableRetryOnFailure` must wrap in `CreateExecutionStrategy().ExecuteAsync`.
