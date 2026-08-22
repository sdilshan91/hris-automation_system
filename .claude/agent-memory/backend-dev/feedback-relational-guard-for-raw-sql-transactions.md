---
name: relational-guard-for-raw-sql-transactions
description: Guard EF transactions / raw SQL / ExecuteUpdate behind Database.IsRelational() so InMemory-provider tests don't throw
metadata:
  type: feedback
---

When a fix needs a DB-level primitive the EF **InMemory** provider doesn't support — `BeginTransactionAsync`, raw SQL (`ExecuteSqlInterpolatedAsync`, `FromSql`), `SELECT ... FOR UPDATE`, `ExecuteUpdateAsync` — gate it behind `_dbContext.Database.IsRelational()` and provide a plain in-memory fallback for the non-relational path.

**Why:** parts of this repo's integration tests run on the EF InMemory provider (see [[feedback-integration-tests-inmemory]]), which throws on transactions and raw SQL. Introducing those unguarded breaks existing tests, and the task rules forbid weakening/skipping tests to go green.

**How to apply:** used in `AuthService` BUG-045 fix (atomic failed-login counter). `BeginFailedAttemptScopeAsync` does `BeginTransactionAsync` + `SELECT 1 ... FOR UPDATE` + `Entry(user).ReloadAsync()` on Postgres, but returns `null` (no-op) when `!IsRelational()`; callers use null-safe `await using` + `if (scope is not null) await scope.CommitAsync()`. Real concurrency correctness (lost-update regression) is only assertable against Postgres/Testcontainers, which is the correct place for that test.
