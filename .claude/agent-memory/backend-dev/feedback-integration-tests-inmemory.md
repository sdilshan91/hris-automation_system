---
name: feedback-integration-tests-inmemory
description: Integration tests in HRM.Tests use InMemory through the real DI/MediatR pipeline, not Testcontainers
metadata:
  type: feedback
---

Write "integration" tests in `HRM.Tests/Integration/` using the **EF Core InMemory provider driven
through a real DI container + MediatR pipeline** (compose `ServiceCollection`, register
`ITenantContext`/`ICurrentUser`/`AppDbContext`/the feature service + `AddMediatR`, send the command).
Do NOT use Testcontainers, even when a story explicitly asks for it.

**Why:** the repo verify gate (`/implement-all`, CI `ci-gate.yml`) runs `dotnet test` with NO
PostgreSQL service bound, and Docker is unavailable in the local agent sandbox — a Testcontainers
test would red the gate it must pass. PostgreSQL-specific schema (partial unique indexes, text[],
numeric) is instead validated by the separate `migrations` CI job that applies the generated
migration to real Postgres. This is the established pattern in every `*IntegrationTests.cs` file
(e.g. LeaveRequestIntegrationTests has a long header comment explaining it).

**How to apply:** mirror an existing `*IntegrationTests.cs` exactly — a `MutableTenantContext` to
flip the acting tenant per request is the idiom for proving multi-tenant isolation. Tenant isolation
is proven by the `ITenantContext`-driven global query filters, which the InMemory pipeline honours.
See [[reference-attendance-module]].
