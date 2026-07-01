-- Teardown for the dedicated `perf` load-test tenant.
-- SAFETY: deletes ONLY rows belonging to the perf tenant id / perf admin user.
-- Never a blanket tenant_id delete on a shared tenant (per 2026-06-27 policy).
-- Run:  psql ... -v perf_tid="'11111111-2222-3333-4444-555555555555'" -f teardown-perf-tenant.sql
\set ON_ERROR_STOP on

BEGIN;

-- children first (FK order)
DELETE FROM employees   WHERE tenant_id = :perf_tid;
DELETE FROM departments WHERE tenant_id = :perf_tid;
DELETE FROM job_titles  WHERE tenant_id = :perf_tid;

DELETE FROM role_permissions rp
  USING roles r
  WHERE rp.role_id = r.id AND r.tenant_id = :perf_tid;

DELETE FROM user_tenant_roles utr
  USING user_tenants ut
  WHERE utr.user_tenant_id = ut.id AND ut.tenant_id = :perf_tid;

DELETE FROM user_tenants WHERE tenant_id = :perf_tid;
DELETE FROM roles        WHERE tenant_id = :perf_tid;

-- the synthetic perf admin user (global table) — match by exact email + home tenant
DELETE FROM users WHERE email = 'perfadmin@perf.test' AND tenant_id = :perf_tid;

DELETE FROM tenants WHERE id = :perf_tid;

COMMIT;

-- residue check (should all be 0)
SELECT 'employees'    AS tbl, count(*) FROM employees   WHERE tenant_id = :perf_tid
UNION ALL SELECT 'departments', count(*) FROM departments WHERE tenant_id = :perf_tid
UNION ALL SELECT 'job_titles',  count(*) FROM job_titles  WHERE tenant_id = :perf_tid
UNION ALL SELECT 'user_tenants',count(*) FROM user_tenants WHERE tenant_id = :perf_tid
UNION ALL SELECT 'roles',       count(*) FROM roles       WHERE tenant_id = :perf_tid
UNION ALL SELECT 'perf_user',   count(*) FROM users WHERE email='perfadmin@perf.test'
UNION ALL SELECT 'tenant',      count(*) FROM tenants WHERE id = :perf_tid;
