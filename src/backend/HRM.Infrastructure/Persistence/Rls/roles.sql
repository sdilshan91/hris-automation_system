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
-- (see user-stories/platform/US-PLT-002.md §9.3):
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
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hrm_app') THEN
    CREATE ROLE hrm_app LOGIN PASSWORD :'hrm_app_password';
  END IF;
END
$$;
ALTER ROLE hrm_app NOBYPASSRLS;

-- ── Privileged role (schema owner, bypasses RLS) ────────────────────────────
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hrm_owner') THEN
    CREATE ROLE hrm_owner LOGIN PASSWORD :'hrm_owner_password' BYPASSRLS;
  END IF;
END
$$;

-- Schema/table privileges (adjust schema name as needed; default 'public').
GRANT USAGE ON SCHEMA public TO hrm_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hrm_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hrm_app;
-- Future tables created by hrm_owner (migrations) automatically grant to hrm_app.
ALTER DEFAULT PRIVILEGES FOR ROLE hrm_owner IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hrm_app;
ALTER DEFAULT PRIVILEGES FOR ROLE hrm_owner IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO hrm_app;
