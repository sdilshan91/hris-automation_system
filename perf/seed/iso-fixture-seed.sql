-- Cross-tenant ISOLATION test fixture — dedicated THROWAWAY tenants only.
-- SAFETY: every tenant here uses an 'iso*' subdomain + fixed UUID. Teardown deletes ONLY these.
-- Never touches acme/techoneglobal/platform. All logins: password 'Admin@123!' (hash from tenantadmin@acme.test).
\set ON_ERROR_STOP on
\set isoa       '2a1a0000-0000-4000-8000-0000000000a1'
\set isob       '2b1b0000-0000-4000-8000-0000000000b1'
\set isosus     '25500000-0000-4000-8000-000000000055'
\set isoterming '25600000-0000-4000-8000-000000000056'
\set isoterm    '25700000-0000-4000-8000-000000000057'

BEGIN;

CREATE TEMP TABLE _isot(tid uuid) ON COMMIT DROP;
INSERT INTO _isot VALUES (:'isoa'),(:'isob'),(:'isosus'),(:'isoterming'),(:'isoterm');
DELETE FROM employees   WHERE tenant_id IN (SELECT tid FROM _isot);
DELETE FROM departments WHERE tenant_id IN (SELECT tid FROM _isot);
DELETE FROM job_titles  WHERE tenant_id IN (SELECT tid FROM _isot);
DELETE FROM role_permissions rp USING roles r WHERE rp.role_id = r.id AND r.tenant_id IN (SELECT tid FROM _isot);
DELETE FROM user_tenant_roles utr USING user_tenants ut WHERE utr.user_tenant_id = ut.id AND ut.tenant_id IN (SELECT tid FROM _isot);
DELETE FROM user_tenants WHERE tenant_id IN (SELECT tid FROM _isot);
DELETE FROM roles        WHERE tenant_id IN (SELECT tid FROM _isot);
DELETE FROM users        WHERE email LIKE '%@iso.test';
DELETE FROM tenants      WHERE id IN (SELECT tid FROM _isot);

INSERT INTO tenants (id, subdomain, name, status, plan_id, enabled_modules, created_at, is_deleted,
  max_concurrent_sessions, idle_timeout_minutes, absolute_timeout_hours, max_failed_attempts,
  lockout_duration_minutes, progressive_lockout_enabled, min_password_length, require_uppercase,
  require_lowercase, require_digit, require_special_character, password_history_count,
  suspended_at, suspended_reason, termination_scheduled_at)
SELECT v.id, v.sub, v.nm, v.st, 'default',
       (SELECT enabled_modules FROM tenants WHERE subdomain='acme'),
       now(), false, 5,30,8,5,15,false,12,true,true,true,true,5,
       v.susp_at, v.susp_reason, v.term_at
FROM (VALUES
  (:'isoa'::uuid,       'isoa',       'ISO Tenant A',        'Active',      NULL::timestamptz, NULL::text,  NULL::timestamptz),
  (:'isob'::uuid,       'isob',       'ISO Tenant B',        'Active',      NULL,              NULL,        NULL),
  (:'isosus'::uuid,     'isosus',     'ISO Suspended',       'Suspended',   now(),             'iso test',  NULL),
  (:'isoterming'::uuid, 'isoterming', 'ISO Terminating',     'Terminating', NULL,              NULL,        now() + interval '30 days'),
  (:'isoterm'::uuid,    'isoterm',    'ISO Terminated',      'Terminated',  NULL,              NULL,        now() - interval '1 day')
) v(id, sub, nm, st, susp_at, susp_reason, term_at);

CREATE TEMP TABLE iso_rolemap ON COMMIT DROP AS
SELECT t.tid, r.id AS old_id, gen_random_uuid() AS new_id, r.name, r.description, r.is_built_in
FROM _isot t
CROSS JOIN (SELECT r.* FROM roles r JOIN tenants tt ON tt.id=r.tenant_id WHERE tt.subdomain='acme') r;
INSERT INTO roles (id, tenant_id, name, description, is_built_in, created_at)
SELECT new_id, tid, name, description, is_built_in, now() FROM iso_rolemap;
INSERT INTO role_permissions (role_id, permission)
SELECT rm.new_id, rp.permission FROM role_permissions rp JOIN iso_rolemap rm ON rm.old_id=rp.role_id;

CREATE TEMP TABLE iso_usermap ON COMMIT DROP AS
SELECT gen_random_uuid() AS uid, gen_random_uuid() AS ut_id, v.tid, v.email, v.rolename
FROM (VALUES
  (:'isoa'::uuid,       'isoa-admin@iso.test',       'Tenant Admin'),
  (:'isoa'::uuid,       'isoa-auditor@iso.test',     'Auditor'),
  (:'isoa'::uuid,       'isoa-emp@iso.test',         'Employee'),
  (:'isob'::uuid,       'isob-admin@iso.test',       'Tenant Admin'),
  (:'isosus'::uuid,     'isosus-admin@iso.test',     'Tenant Admin'),
  (:'isosus'::uuid,     'isosus-emp@iso.test',       'Employee'),
  (:'isoterming'::uuid, 'isoterming-admin@iso.test', 'Tenant Admin'),
  (:'isoterm'::uuid,    'isoterm-admin@iso.test',    'Tenant Admin')
) v(tid, email, rolename);
INSERT INTO users (id, email, display_name, password_hash, is_active, failed_login_count,
                   password_changed_at, mfa_enabled, created_at, tenant_id, lockout_count, mfa_failed_attempt_count)
SELECT um.uid, um.email, um.email, src.password_hash, true, 0, now(), false, now(), um.tid, 0, 0
FROM iso_usermap um CROSS JOIN (SELECT password_hash FROM users WHERE email='tenantadmin@acme.test') src;
INSERT INTO user_tenants (id, user_id, tenant_id, status, created_at)
SELECT um.ut_id, um.uid, um.tid, 'Active', now() FROM iso_usermap um;
INSERT INTO user_tenant_roles (user_tenant_id, role_id, assigned_at)
SELECT um.ut_id, rm.new_id, now()
FROM iso_usermap um JOIN iso_rolemap rm ON rm.tid=um.tid AND rm.name=um.rolename;

INSERT INTO departments (id, tenant_id, name, code, created_at, is_deleted, is_active)
SELECT gen_random_uuid(), tid, 'ISO Dept', 'ISOD', now(), false, true FROM _isot;
INSERT INTO job_titles (id, tenant_id, title_name, created_at, is_deleted, is_active)
SELECT gen_random_uuid(), tid, 'ISO Title', now(), false, true FROM _isot;
INSERT INTO employees (id, tenant_id, employee_no, first_name, last_name, email, date_of_joining,
                       department_id, job_title_id, employment_type, status, is_active, created_at, is_deleted)
SELECT gen_random_uuid(), d.tenant_id, 'ISO-' || substr(d.tenant_id::text,1,4) || '-' || g,
       'Iso' || g, 'Emp' || g, 'isoemp' || g || '_' || substr(d.tenant_id::text,1,4) || '@iso.test',
       date '2025-01-01', d.id, j.id, 'FullTime', 'Active', true, now(), false
FROM (SELECT tenant_id, id FROM departments WHERE name='ISO Dept' AND tenant_id IN (SELECT tid FROM _isot)) d
JOIN (SELECT tenant_id, id FROM job_titles WHERE title_name='ISO Title' AND tenant_id IN (SELECT tid FROM _isot)) j
  ON j.tenant_id = d.tenant_id
CROSS JOIN generate_series(1,2) g;

UPDATE employees SET user_id = (SELECT uid FROM iso_usermap WHERE email='isoa-emp@iso.test')
WHERE tenant_id = :'isoa' AND employee_no = 'ISO-' || substr(:'isoa',1,4) || '-1';

COMMIT;
SELECT t.subdomain, t.status, (SELECT count(*) FROM employees e WHERE e.tenant_id=t.id) AS emps
FROM tenants t WHERE t.subdomain LIKE 'iso%' ORDER BY t.subdomain;
