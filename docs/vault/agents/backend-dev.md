---
type: agent-note
agent: backend-dev
---

# @backend-dev

Persistent notes for the backend-dev agent.

Refer to the agent definition in [.claude/agents/team/backend-dev.md](../../../.claude/agents/team/backend-dev.md).

## Architecture conventions

- CQRS: Command/Query records in `Features/{Module}/Commands|Queries`, handlers delegate to a service interface in `Application/Common/Interfaces`.
- Service implementations live in `Infrastructure/Services/` and operate directly on `AppDbContext` (no generic repository implementation exists despite `IRepository<T>` being defined).
- Validators in `Features/{Module}/Validators/` using FluentValidation; auto-registered from assembly.
- DTOs and request records in `Features/{Module}/DTOs/`.
- Controllers are thin: dispatch via MediatR, wrap results in `ApiResponse<T>`.
- Use `Result<T>` / `Result` pattern for all business-logic outcomes (no exceptions).
- Use `sealed` on all concrete classes. Use `record` for DTOs and commands.
- Route convention: `api/v1/tenant/{resource}` for tenant-scoped endpoints.
- Permission-gating: `[RequirePermission("Module.Action")]` attribute on controller actions.

## Tenant-isolation checklist

1. Every service method must check `_tenantContext.IsResolved` and return early failure if not.
2. EF global query filter on `AppDbContext.OnModelCreating` for every tenant-scoped entity.
3. `TenantInterceptor` auto-stamps `TenantId` on `BaseEntity` adds, but the service should also set it explicitly for clarity.
4. Unique indexes that are tenant-scoped must include `TenantId` in the composite key.
5. Cross-tenant queries (e.g. parent lookups) are naturally prevented by the global filter -- no extra WHERE needed.
6. Parent entity validation (e.g. "parent department belongs to same tenant") is implicitly enforced by the global filter; an explicit exists-check suffices.

## Database / EF patterns

- snake_case naming via `UseSnakeCaseNamingConvention()` -- do NOT manually specify column names for BaseEntity properties (the convention handles it).
- Explicitly specify `HasColumnName("id")` only for the PK in configurations (matching existing pattern).
- Partial unique indexes on PostgreSQL: use `.HasFilter("is_deleted = false")` for soft-delete-aware uniqueness.
- Self-referencing FKs: use `DeleteBehavior.Restrict` to prevent cascade issues.
- Migrations auto-apply on startup via `DbInitializer.RunAsync`.
- When a FK target entity doesn't exist yet, store the column as a nullable UUID without FK constraint and leave a `TODO(US-XXX)` comment.
