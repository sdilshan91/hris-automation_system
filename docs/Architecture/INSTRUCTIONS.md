# Architecture — INSTRUCTIONS

- **Advisory pass:** `/advisor [--radar|--adr|--deadcode|--module]` → writes here (`advisory-reports/`, updates `radar/`).
- **Security gate:** `/security-audit [scope]` → writes [`security-reviews/`](security-reviews/); run before opening a PR.
- **Record a decision:** add an ADR-lite note to [`../vault/decisions/`](../vault/decisions/) and index it in [`DECISIONS.md`](DECISIONS.md).
- **Conventions:** the three-layer multi-tenancy (resolution → write-stamp → read query-filter) is non-negotiable; see [`../../CLAUDE.md`](../../CLAUDE.md) Architecture section.
