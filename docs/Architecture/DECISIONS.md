# Architecture — DECISIONS

Architecture Decision Records live in the vault (single source of truth). This is the index.

- **ADR store:** [`../vault/decisions/`](../vault/decisions/) — e.g. the tenant-isolation model (shared-DB + RLS, hybrid seam open).
- **Standing rules** (from the active plan): every new `tenant_id` table adds its dormant `tenant_isolation` RLS policy in-migration; cache = read + auto-evict (tenant-prefixed keys); RLS enablement is config-gated + reversible + OFF by default.
- Clean Architecture + CQRS/MediatR; EF Core snake_case on PostgreSQL — see [`hrm_technical_document_v4.0.md`](hrm_technical_document_v4.0.md) and [`../../CLAUDE.md`](../../CLAUDE.md).
