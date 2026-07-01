-- Functional-test THROWAWAY tenant `fntest` — clean EMP-NNNN employees so BUG-093 does NOT fire
-- (employee CREATE works here → agent can create/mutate freely; drop the whole tenant to clean up).
-- SAFETY: touches ONLY subdomain 'fntest' + '%@fn.test' users. Never acme/techoneglobal.
\set ON_ERROR_STOP on
\set fntid '3f000000-0000-4000-8000-00000000000f'
BEGIN;

-- reset
DELETE FROM employees   WHERE tenant_id = :'fntid';
DELETE FROM departments WHERE tenant_id = :'fntid';
DELETE FROM job_titles  WHERE tenant_id = :'fntid';
DELETE FROM role_permissions rp USING roles r WHERE rp.role_id = r.id AND r.tenant_id = :'fntid';
DELETE FROM user_tenant_roles utr USING user_tenants ut WHERE utr.user_tenant_id = ut.id AND ut.tenant_id = :'fntid';
DELETE FROM user_tenants WHERE tenant_id = :'fntid';
DELETE FROM roles        WHERE tenant_id = :'fntid';
DELETE FROM users        WHERE email LIKE '%@fn.test';
DELETE FROM tenants      WHERE id = :'fntid';

-- tenant
INSERT INTO tenants (id, subdomain, name, status, plan_id, enabled_modules, created_at, is_deleted,
  max_concurrent_sessions, idle_timeout_minutes, absolute_timeout_hours, max_failed_attempts,
  lockout_duration_minutes, progressive_lockout_enabled, min_password_length, require_uppercase,
  require_lowercase, require_digit, require_special_character, password_history_count)
VALUES (:'fntid', 'fntest', 'Functional Test Tenant', 'Active', 'default',
  (SELECT enabled_modules FROM tenants WHERE subdomain='acme'), now(), false,
  5,30,8,5,15,false,12,true,true,true,true,5);

-- roles + perms (from acme)
CREATE TEMP TABLE fn_rolemap ON COMMIT DROP AS
SELECT r.id old_id, gen_random_uuid() new_id, r.name, r.description, r.is_built_in
FROM roles r JOIN tenants t ON t.id=r.tenant_id WHERE t.subdomain='acme';
INSERT INTO roles (id, tenant_id, name, description, is_built_in, created_at)
SELECT new_id, :'fntid', name, description, is_built_in, now() FROM fn_rolemap;
INSERT INTO role_permissions (role_id, permission)
SELECT rm.new_id, rp.permission FROM role_permissions rp JOIN fn_rolemap rm ON rm.old_id=rp.role_id;

-- users (admin/hr/mgr/emp), password Admin@123! (hash from tenantadmin@acme.test)
CREATE TEMP TABLE fn_users ON COMMIT DROP AS
SELECT gen_random_uuid() uid, gen_random_uuid() ut_id, v.email, v.rolename
FROM (VALUES
  ('fntest-admin@fn.test','Tenant Admin'),
  ('fntest-hr@fn.test','HR Manager'),
  ('fntest-mgr@fn.test','Manager'),
  ('fntest-emp@fn.test','Employee')
) v(email, rolename);
INSERT INTO users (id, email, display_name, password_hash, is_active, failed_login_count,
  password_changed_at, mfa_enabled, created_at, tenant_id, lockout_count, mfa_failed_attempt_count)
SELECT u.uid, u.email, u.email, src.password_hash, true, 0, now(), false, now(), :'fntid', 0, 0
FROM fn_users u CROSS JOIN (SELECT password_hash FROM users WHERE email='tenantadmin@acme.test') src;
INSERT INTO user_tenants (id, user_id, tenant_id, status, created_at)
SELECT u.ut_id, u.uid, :'fntid', 'Active', now() FROM fn_users u;
INSERT INTO user_tenant_roles (user_tenant_id, role_id, assigned_at)
SELECT u.ut_id, rm.new_id, now() FROM fn_users u JOIN fn_rolemap rm ON rm.name=u.rolename;

-- 3 departments, 3 job titles, 2 locations
INSERT INTO departments (id, tenant_id, name, code, created_at, is_deleted, is_active)
SELECT gen_random_uuid(), :'fntid', 'FN Dept '||g, 'FND'||g, now(), false, true FROM generate_series(1,3) g;
INSERT INTO job_titles (id, tenant_id, title_name, created_at, is_deleted, is_active)
SELECT gen_random_uuid(), :'fntid', 'FN Title '||g, now(), false, true FROM generate_series(1,3) g;
INSERT INTO locations (id, tenant_id, name, time_zone, is_active, is_deleted, created_at)
SELECT gen_random_uuid(), :'fntid', 'FN Location '||g, 'UTC', true, false, now() FROM generate_series(1,2) g;

-- 10 employees EMP-0001..EMP-0010 (CLEAN numeric suffix → generator works for new creates)
WITH d AS (SELECT array_agg(id ORDER BY code) ids FROM departments WHERE tenant_id=:'fntid'),
     j AS (SELECT array_agg(id ORDER BY title_name) ids FROM job_titles WHERE tenant_id=:'fntid')
INSERT INTO employees (id, tenant_id, employee_no, first_name, last_name, email, date_of_joining,
  department_id, job_title_id, employment_type, status, is_active, created_at, is_deleted)
SELECT gen_random_uuid(), :'fntid', 'EMP-'||lpad(g::text,4,'0'), 'Fn'||g, 'Emp'||g, 'fnemp'||g||'@fn.test',
  date '2024-06-01', (SELECT ids[1+(g%3)] FROM d), (SELECT ids[1+(g%3)] FROM j),
  'FullTime', 'Active', true, now(), false
FROM generate_series(1,10) g;

UPDATE employees SET user_id=(SELECT uid FROM fn_users WHERE email='fntest-emp@fn.test')
WHERE tenant_id=:'fntid' AND employee_no='EMP-0001';

COMMIT;
SELECT 'tenant' k, subdomain v FROM tenants WHERE id=:'fntid'
UNION ALL SELECT 'employees', count(*)::text FROM employees WHERE tenant_id=:'fntid'
UNION ALL SELECT 'users', count(*)::text FROM users WHERE email LIKE '%@fn.test';
