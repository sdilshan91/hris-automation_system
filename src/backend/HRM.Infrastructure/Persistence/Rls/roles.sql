-- US-PLT-002 — PostgreSQL role provisioning for Row-Level Security (RLS).
--
-- OPS / BOOTSTRAP SCRIPT — run ONCE per database by a DBA/superuser. It is NOT
-- executed by the application or by EF migrations (the app must never own role DDL).
-- Passwords are supplied at run time via psql variables so no secret lives in source:
--
--   psql -h <host> -U postgres -d <db> \
--        -v hrm_app_password="$HRM_APP_PASSWORD" \
--        -v hrm_owner_password="$HRM_OWNER_PASSWORD" \
--        -f roles.sql
--
-- Two roles implement the "non-bypass app role + privileged connection" design
-- (see docs/BA/platform/US-PLT-002.md §9.3):
--
--   hrm_app    — the RUNTIME role. LOGIN, NO BYPASSRLS. Every normal request connects
--                as this role, so RLS policies always apply (even to raw SQL). Wired to
--                ConnectionStrings:DefaultConnection.
--   hrm_owner  — the PRIVILEGED role. Owns the schema (DDL) and has BYPASSRLS. Used ONLY
--                by migrations, DbInitializer seeding, the tenant-resolution lookup, and
--                system/admin + cross-tenant background jobs. Wired to
--                ConnectionStrings:PrivilegedConnection.
--
-- NOTE: until the Phase-4 switch-on migration enables RLS and Rls:Enabled is set true,
-- these roles are optional — the app runs on DefaultConnection as today.

-- ── Runtime role (RLS always enforced) ──────────────────────────────────────
-- NOTE: the CREATE ROLE must stay at psql TOP LEVEL (not inside a DO $$…$$ block):
-- psql only interpolates :'var' in the outer SQL text, NOT inside dollar-quoted
-- bodies, so a `DO $$ … CREATE ROLE … PASSWORD :'hrm_app_password' … $$` never
-- substitutes the password (it errors with "syntax error at or near ':'"). We keep
-- idempotency with a conditional `\gexec`: the SELECT emits the CREATE statement only
-- when the role is absent, and \gexec runs it (no-op if the role already exists).
SELECT format('CREATE ROLE hrm_app LOGIN PASSWORD %L', :'hrm_app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hrm_app')\gexec
ALTER ROLE hrm_app NOBYPASSRLS;

-- ── Privileged role (schema owner, bypasses RLS) ────────────────────────────
SELECT format('CREATE ROLE hrm_owner LOGIN PASSWORD %L BYPASSRLS', :'hrm_owner_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hrm_owner')\gexec

-- Schema/table privileges (adjust schema name as needed; default 'public').
GRANT USAGE ON SCHEMA public TO hrm_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hrm_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hrm_app;
-- Future tables created by hrm_owner (migrations) automatically grant to hrm_app.
ALTER DEFAULT PRIVILEGES FOR ROLE hrm_owner IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hrm_app;
ALTER DEFAULT PRIVILEGES FOR ROLE hrm_owner IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO hrm_app;

-- ── Audit immutability (GAP-005) ────────────────────────────────────────────
-- The audit trail is append-only. Until now that was a CODE CONVENTION only —
-- AuditLogController says as much ("append-only by code convention … REVOKE DEFERRED") — while the
-- runtime role held UPDATE and DELETE on every table, audit included. Anything holding the app's
-- credentials (a SQL-injection foothold, a stray ExecuteDelete, a compromised connection string)
-- could silently rewrite the one record class the rest of the compliance story rests on.
--
-- hrm_app keeps SELECT + INSERT (the app must still write audit rows on every request) and loses
-- UPDATE + DELETE. This must come AFTER the grants above, which are deliberately broad.
--
-- Verified safe before revoking — every legitimate mutation path already runs PRIVILEGED:
--   * AuditLogPurgeService is the ONLY code that deletes audit rows (RemoveRange), and its caller
--     AuditLogPurgeJob calls SetSystemContext() first, which routes to hrm_owner.
--   * TenantDataDeletionService only ADDS audit rows (AC-4 retains them) and likewise runs in system
--     context.
--   * No FK into audit_logs cascades a delete (users has no such reference), so ordinary row deletes
--     elsewhere cannot reach these tables.
-- If a future path needs to mutate audit rows, route it through the privileged connection rather than
-- widening this grant back.
REVOKE UPDATE, DELETE ON audit_logs FROM hrm_app;
REVOKE UPDATE, DELETE ON employee_field_audit_logs FROM hrm_app;

-- NEW-AUDIT-TABLE RULE: ALTER DEFAULT PRIVILEGES above grants UPDATE/DELETE on FUTURE tables to
-- hrm_app, so a newly-added audit table starts out mutable. Add its REVOKE here when you add it —
-- the same standing obligation as the NEW-TENANT-TABLE RLS RULE. Re-running roles.sql is idempotent
-- and re-applies every revoke above.
