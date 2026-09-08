-- Idempotent bulk seed for the dedicated `perf` load-test tenant.
-- Bypasses the broken employee-no generator (BUG-093) by inserting explicit employee_no.
-- Replicates the 8 built-in roles + permissions (from acme, else e2e, else any seeded tenant — DF-53) so a
-- perf admin can log in. Self-contained: works on a fresh/throwaway DbInitializer DB (platform+e2e only).
-- Safe: touches ONLY the perf tenant id + perfadmin@perf.test user.
--
-- Run (1k):  psql ... -v perf_tid="'11111111-2222-3333-4444-555555555555'" -v emp_count=1000 -f seed-perf-tenant.sql
-- Run (5k):  psql ... -v perf_tid="'11111111-2222-3333-4444-555555555555'" -v emp_count=5000 -f seed-perf-tenant.sql
-- Optional:  add -v user_pool=N to size the ISSUE-534 login pool (default 30, see below).
--
-- Login after seed:  POST /api/v1/auth/login  { "email":"perfadmin@perf.test", "password":"Admin@123!" }
--                    header X-Tenant-Subdomain: perf   (password "Admin@123!": copied from tenantadmin@acme.test
--                    when acme exists, else a freshly-derived bcrypt of the same password — DF-53)
\set ON_ERROR_STOP on

-- ISSUE-534: how many perfuserNNN@perf.test logins to create alongside perfadmin. The GLOBAL rate limiter
-- (Program.cs) is 300 req/min per (tenantId, userId), so a load test that authenticates as ONE user is
-- capped at 300 req/min and every excess request comes back 429 in ~2ms -- which drags p95 DOWN and makes a
-- throttled run LOOK like a fast one. The k6 scripts spread their VUs across this pool so the partition key
-- actually varies. Default 30; see perf/scripts/lib.js for the arithmetic that picks it.
\if :{?user_pool}
\else
\set user_pool 30
\endif

-- DF-53: crypt()/gen_salt() (the fallback admin hash below) need pgcrypto. Idempotent; the perf seed runs as
-- an admin/ops user on a throwaway or dev DB.
CREATE EXTENSION IF NOT EXISTS pgcrypto;

BEGIN;

-- ---- reset (idempotent): drop any prior perf rows, FK order ----
DELETE FROM employees   WHERE tenant_id = :perf_tid;
DELETE FROM departments WHERE tenant_id = :perf_tid;
DELETE FROM job_titles  WHERE tenant_id = :perf_tid;
DELETE FROM role_permissions rp USING roles r WHERE rp.role_id = r.id AND r.tenant_id = :perf_tid;
DELETE FROM user_tenant_roles utr USING user_tenants ut WHERE utr.user_tenant_id = ut.id AND ut.tenant_id = :perf_tid;
DELETE FROM user_tenants WHERE tenant_id = :perf_tid;
DELETE FROM roles        WHERE tenant_id = :perf_tid;
-- ISSUE-534: the perfuserNNN pool shares this tenant_id, so it must go too or `DELETE FROM tenants`
-- below fails on users.tenant_id. user_tenants + refresh_tokens cascade off users (UserConfiguration).
DELETE FROM users        WHERE tenant_id = :perf_tid
                           AND (email = 'perfadmin@perf.test' OR email LIKE 'perfuser%@perf.test');
DELETE FROM tenants      WHERE id = :perf_tid;

-- ---- tenant ---- (all NOT-NULL/no-DB-default cols supplied explicitly; EF defaults are app-side only)
INSERT INTO tenants (id, subdomain, name, status, plan_id, enabled_modules, created_at, is_deleted,
                     max_concurrent_sessions, idle_timeout_minutes, absolute_timeout_hours,
                     max_failed_attempts, lockout_duration_minutes, progressive_lockout_enabled,
                     min_password_length, require_uppercase, require_lowercase, require_digit,
                     require_special_character, password_history_count)
VALUES (:perf_tid, 'perf', 'Performance Test Tenant', 'Active', 'default', '[]'::jsonb, now(), false,
        5, 30, 8, 5, 15, false, 12, true, true, true, true, 5);

-- ---- departments (10) ----
INSERT INTO departments (id, tenant_id, name, code, created_at, is_deleted, is_active)
SELECT gen_random_uuid(), :perf_tid, 'Perf Dept ' || g, 'PD' || lpad(g::text, 2, '0'), now(), false, true
FROM generate_series(1, 10) g;

-- ---- job titles (8) ----
INSERT INTO job_titles (id, tenant_id, title_name, created_at, is_deleted, is_active)
SELECT gen_random_uuid(), :perf_tid, 'Perf Title ' || g, now(), false, true
FROM generate_series(1, 8) g;

-- ---- roles + permissions (DF-53: self-contained) ----
-- Source the built-in roles from acme if present, else e2e, else ANY tenant that already has them seeded.
-- A freshly-migrated DB (DbInitializer) has only platform + e2e, so the old `WHERE subdomain='acme'` copied
-- zero roles → a permission-less perf admin. This COALESCEs the source so the seed works on any DB.
CREATE TEMP TABLE perf_role_src ON COMMIT DROP AS
SELECT t.id AS tenant_id
FROM tenants t
WHERE EXISTS (SELECT 1 FROM roles r WHERE r.tenant_id = t.id AND r.is_built_in = true)
ORDER BY (t.subdomain = 'acme') DESC, (t.subdomain = 'e2e') DESC, t.created_at
LIMIT 1;

CREATE TEMP TABLE perf_role_map ON COMMIT DROP AS
SELECT r.id AS old_id, gen_random_uuid() AS new_id, r.name, r.description, r.is_built_in
FROM roles r
WHERE r.tenant_id = (SELECT tenant_id FROM perf_role_src)
  AND r.is_built_in = true;

INSERT INTO roles (id, tenant_id, name, description, is_built_in, created_at)
SELECT new_id, :perf_tid, name, description, is_built_in, now() FROM perf_role_map;

INSERT INTO role_permissions (role_id, permission)
SELECT rm.new_id, rp.permission
FROM role_permissions rp JOIN perf_role_map rm ON rm.old_id = rp.role_id;

-- ---- perf admin user (password hash copied from tenantadmin@acme.test => "Admin@123!") ----
CREATE TEMP TABLE perf_ids ON COMMIT DROP AS
SELECT gen_random_uuid() AS admin_uid, gen_random_uuid() AS admin_ut_id;

-- DF-53: always create the perf admin (FROM perf_ids → exactly one row). Copy acme's tenantadmin hash when
-- present (identical to the prior dev-DB behaviour, password "Admin@123!"); otherwise derive a fresh bcrypt of
-- the SAME password so a fresh/throwaway DB (no acme) still logs in with "Admin@123!" — instead of inserting
-- zero user rows and then hitting fk_user_tenants_users_user_id on the user_tenants insert below.
INSERT INTO users (id, email, display_name, password_hash, is_active, failed_login_count,
                   password_changed_at, mfa_enabled, created_at, tenant_id, lockout_count, mfa_failed_attempt_count)
SELECT p.admin_uid, 'perfadmin@perf.test', 'Perf Admin',
       COALESCE(
         (SELECT u.password_hash FROM users u JOIN tenants t ON t.id = u.tenant_id
          WHERE u.email = 'tenantadmin@acme.test' AND t.subdomain = 'acme' LIMIT 1),
         crypt('Admin@123!', gen_salt('bf', 10))
       ),
       true, 0, now(), false, now(), :perf_tid, 0, 0
FROM perf_ids p;

INSERT INTO user_tenants (id, user_id, tenant_id, status, created_at)
SELECT p.admin_ut_id, p.admin_uid, :perf_tid, 'Active', now() FROM perf_ids p;

INSERT INTO user_tenant_roles (user_tenant_id, role_id, assigned_at)
SELECT p.admin_ut_id, rm.new_id, now()
FROM perf_ids p, perf_role_map rm
WHERE rm.name = 'Tenant Admin';

-- ---- ISSUE-534: login pool (perfuser001..:user_pool), all Tenant Admin ----
-- Same password as perfadmin ("Admin@123!"): the hash is COPIED from the row just inserted above rather
-- than re-derived, so it costs nothing and cannot drift from perfadmin's credential.
INSERT INTO users (id, email, display_name, password_hash, is_active, failed_login_count,
                   password_changed_at, mfa_enabled, created_at, tenant_id, lockout_count, mfa_failed_attempt_count)
SELECT gen_random_uuid(), 'perfuser' || lpad(g::text, 3, '0') || '@perf.test', 'Perf User ' || g,
       (SELECT u.password_hash FROM users u WHERE u.email = 'perfadmin@perf.test' AND u.tenant_id = :perf_tid),
       true, 0, now(), false, now(), :perf_tid, 0, 0
FROM generate_series(1, :user_pool) g;

INSERT INTO user_tenants (id, user_id, tenant_id, status, created_at)
SELECT gen_random_uuid(), u.id, :perf_tid, 'Active', now()
FROM users u
WHERE u.tenant_id = :perf_tid AND u.email LIKE 'perfuser%@perf.test';

INSERT INTO user_tenant_roles (user_tenant_id, role_id, assigned_at)
SELECT ut.id, rm.new_id, now()
FROM user_tenants ut
JOIN users u ON u.id = ut.user_id
JOIN perf_role_map rm ON rm.name = 'Tenant Admin'
WHERE ut.tenant_id = :perf_tid AND u.email LIKE 'perfuser%@perf.test';

-- ---- employees (:emp_count) round-robin across depts + titles ----
WITH d AS (SELECT array_agg(id ORDER BY code) AS ids FROM departments WHERE tenant_id = :perf_tid),
     j AS (SELECT array_agg(id ORDER BY title_name) AS ids FROM job_titles WHERE tenant_id = :perf_tid)
INSERT INTO employees (id, tenant_id, employee_no, first_name, last_name, email, date_of_joining,
                       department_id, job_title_id, employment_type, status, is_active, created_at, is_deleted)
SELECT gen_random_uuid(), :perf_tid, 'PERF-' || lpad(g::text, 6, '0'),
       'First' || g, 'Last' || g, 'perf' || g || '@perf.test',
       date '2024-01-01' + ((g * 7) % 700),
       (SELECT ids[1 + (g % array_length(ids, 1))] FROM d),
       (SELECT ids[1 + (g % array_length(ids, 1))] FROM j),
       (ARRAY['FullTime','PartTime','Contract','Intern'])[1 + (g % 4)],
       'Active', true, now(), false
FROM generate_series(1, :emp_count) g;

COMMIT;

-- ---- summary ----
SELECT 'tenant'      AS what, subdomain::text AS detail FROM tenants WHERE id = :perf_tid
UNION ALL SELECT 'employees', count(*)::text FROM employees   WHERE tenant_id = :perf_tid
UNION ALL SELECT 'departments', count(*)::text FROM departments WHERE tenant_id = :perf_tid
UNION ALL SELECT 'job_titles',  count(*)::text FROM job_titles  WHERE tenant_id = :perf_tid
UNION ALL SELECT 'roles',       count(*)::text FROM roles       WHERE tenant_id = :perf_tid
UNION ALL SELECT 'perf_admin',  count(*)::text FROM users WHERE email='perfadmin@perf.test'
UNION ALL SELECT 'perf_pool',   count(*)::text FROM users WHERE tenant_id = :perf_tid AND email LIKE 'perfuser%@perf.test';
