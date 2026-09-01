---
name: inmemory-required-nav-projection
description: EF InMemory returns an empty list when you project through a required navigation that has a query filter; resolve names via separate lookups
metadata:
  type: feedback
---

When a service projects through a **required navigation that itself has a global query filter**
(e.g. `_dbContext.Employees.Select(e => new { Name = e.Department.Name })`, where Department has a
tenant+soft-delete `HasQueryFilter`), the **EF Core InMemory provider returns an EMPTY result set** —
silently, no exception. This bit US-PRF-007: the whole dashboard population came back as 0 scored
employees and every aggregation test failed with count 0 / KeyNotFound.

**Why:** 120 test files still build their context on the InMemory provider (no Postgres/Docker — see
[[integration-tests-inmemory]]), so the verify gate — `bash scripts/run-backend-tests.sh`, never raw
`dotnet test` (ISSUE-312) — exercises them there. InMemory's handling of a required navigation whose related entity has
a query filter differs from Npgsql and effectively filters out all rows in the projection.

**How to apply:** in any read/aggregation service that runs under InMemory tests, do NOT project
related-entity scalar fields through the navigation. Instead select the raw FK ids, then resolve the
display names (department, job title, etc.) via **separate tenant-scoped lookup queries** into a
dictionary. Same file also hit the sibling gotcha: a captured `HashSet`/`IReadOnlySet.Contains` in an
EF `Where` is not translated by InMemory — use `List.Contains` for `IN`-style predicates.
