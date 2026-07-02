-- Per-module VOLUME seed for the `perf` throwaway tenant (on top of seed-perf-tenant.sql 5k employees).
-- Unblocks the leave/attendance/recruitment/audit perf TCs. THROWAWAY ONLY — teardown drops with the tenant.
-- Safety: every INSERT is scoped to :perf_tid. Never touches acme/techoneglobal.
-- Run: psql ... -v perf_tid="'11111111-2222-3333-4444-555555555555'" -f perf-volume-seed.sql
\set ON_ERROR_STOP on
BEGIN;

-- idempotent reset of volume rows (keep the 5k employees/roles from seed-perf-tenant.sql)
DELETE FROM leave_request  WHERE tenant_id = :perf_tid;
DELETE FROM leave_ledger   WHERE tenant_id = :perf_tid;
DELETE FROM leave_types    WHERE tenant_id = :perf_tid;
DELETE FROM attendance_log WHERE tenant_id = :perf_tid;
DELETE FROM shift          WHERE tenant_id = :perf_tid;
DELETE FROM applicant_stage_history WHERE tenant_id = :perf_tid;
DELETE FROM applicant      WHERE tenant_id = :perf_tid;
DELETE FROM vacancy        WHERE tenant_id = :perf_tid;
DELETE FROM audit_logs     WHERE tenant_id = :perf_tid;

-- ============ WS-A: LEAVE ============
-- copy acme's leave_types into perf (new ids)
INSERT INTO leave_types (id, name, code, color, description, annual_entitlement, accrual_frequency,
  carry_forward_limit, carry_forward_expiry_months, probation_eligible, documents_required,
  document_day_threshold, encashable, max_encash_days, half_day_allowed, hourly_allowed, gender,
  max_consecutive_days, negative_balance_allowed, negative_balance_limit, display_order, is_active,
  tenant_id, created_at, is_deleted, system_category)
SELECT gen_random_uuid(), name, code, color, description, annual_entitlement, accrual_frequency,
  carry_forward_limit, carry_forward_expiry_months, probation_eligible, documents_required,
  document_day_threshold, encashable, max_encash_days, half_day_allowed, hourly_allowed, gender,
  max_consecutive_days, negative_balance_allowed, negative_balance_limit, display_order, is_active,
  :perf_tid, now(), false, system_category
FROM leave_types WHERE tenant_id = (SELECT id FROM tenants WHERE subdomain='acme');

-- two working leave-type ids for ledger + requests
CREATE TEMP TABLE _lt ON COMMIT DROP AS
SELECT id, row_number() OVER (ORDER BY display_order) rn FROM leave_types WHERE tenant_id = :perf_tid;

-- leave_ledger: one ACCRUAL per employee for leave-type 1 (balance 20)
INSERT INTO leave_ledger (id, entry_type, employee_id, leave_type_id, leave_year, amount, balance_after,
  description, occurred_at, tenant_id, created_at, is_deleted)
SELECT gen_random_uuid(), 'Accrual', e.id, (SELECT id FROM _lt WHERE rn=1), 2026, 20, 20,
  'seed opening balance', now(), :perf_tid, now(), false
FROM employees e WHERE e.tenant_id = :perf_tid;

-- leave_request: >6000 rows, mixed states. 5000 base (one per employee) + 1200 extra pending/approved.
-- status distribution by (g % 5): 0 Pending, 1 Approved, 2 Approved(future-cancellable), 3 HrAssigned(lop), 4 Rejected
WITH e AS (SELECT id, row_number() OVER (ORDER BY employee_no) g FROM employees WHERE tenant_id = :perf_tid),
     lt1 AS (SELECT id FROM _lt WHERE rn=1)
INSERT INTO leave_request (id, employee_id, leave_type_id, start_date, end_date, is_half_day,
  half_day_session, total_days, reason, status, requested_at, attachment_urls, tenant_id, created_at,
  is_deleted, is_lop)
SELECT gen_random_uuid(), e.id, (SELECT id FROM lt1),
  date '2026-06-01' + (e.g % 25)::int , date '2026-06-01' + (e.g % 25)::int + (e.g % 3)::int,
  (e.g % 7 = 0), CASE WHEN e.g % 7 = 0 THEN 'AM' END,
  CASE WHEN e.g % 7 = 0 THEN 0.5 ELSE 1 + (e.g % 3) END,
  'seed', (ARRAY['Pending','Approved','Approved','HrAssigned','Rejected'])[1 + (e.g % 5)],
  now() - (e.g % 30) * interval '1 day', '{}'::text[], :perf_tid, now(), false, (e.g % 5 = 3)
FROM e;
-- extra 1200 pending (approval-queue volume) with future dates
WITH e AS (SELECT id, row_number() OVER (ORDER BY employee_no) g FROM employees WHERE tenant_id = :perf_tid LIMIT 1200),
     lt2 AS (SELECT id FROM _lt WHERE rn = (SELECT min(rn) FROM _lt WHERE rn>1))
INSERT INTO leave_request (id, employee_id, leave_type_id, start_date, end_date, is_half_day,
  total_days, reason, status, requested_at, attachment_urls, tenant_id, created_at, is_deleted, is_lop)
SELECT gen_random_uuid(), e.id, (SELECT id FROM lt2),
  date '2026-08-01' + (e.g % 20)::int, date '2026-08-01' + (e.g % 20)::int + 1, false, 2, 'seed-pending',
  'Pending', now(), '{}'::text[], :perf_tid, now(), false, false
FROM e;

-- ============ WS-B: ATTENDANCE ============
-- shifts (for bulk shift assignment TC)
INSERT INTO shift (id, name, type, start_time, end_time, break_duration_minutes, grace_period_minutes,
  minimum_hours, working_days, is_default, is_active, tenant_id, created_at, is_deleted)
SELECT gen_random_uuid(), 'Perf Shift '||g, 'Fixed', time '09:00', time '18:00', 60, 15, 8,
  ARRAY[1,2,3,4,5], g=1, true, :perf_tid, now(), false
FROM generate_series(1,3) g;

-- attendance_log: 5000 employees x 30 June days = 150k rows. ~10% late, ~5% early, ~8% overtime.
INSERT INTO attendance_log (id, employee_id, clock_in, clock_out, source, status, total_work_minutes,
  overtime_minutes, early_departure_minutes, is_early_departure, is_late, late_by_minutes, late_minutes,
  tenant_id, created_at, is_deleted)
SELECT gen_random_uuid(), e.id,
  ((date '2026-06-01' + d) + time '09:00') AT TIME ZONE 'UTC' + CASE WHEN e.g % 10 = 0 THEN interval '15 min' ELSE interval '0' END,
  ((date '2026-06-01' + d) + time '18:00') AT TIME ZONE 'UTC' - CASE WHEN e.g % 20 = 0 THEN interval '20 min' ELSE interval '0' END + CASE WHEN e.g % 12 = 0 THEN interval '45 min' ELSE interval '0' END,
  'WEB',
  CASE WHEN e.g % 12 = 0 THEN 'OVERTIME' WHEN e.g % 20 = 0 THEN 'SHORT_DAY' ELSE 'COMPLETE' END,
  540, CASE WHEN e.g % 12 = 0 THEN 45 ELSE 0 END,
  CASE WHEN e.g % 20 = 0 THEN 20 ELSE 0 END, (e.g % 20 = 0),
  (e.g % 10 = 0), CASE WHEN e.g % 10 = 0 THEN 15 ELSE 0 END, CASE WHEN e.g % 10 = 0 THEN 15 ELSE 0 END,
  :perf_tid, now(), false
FROM (SELECT id, row_number() OVER (ORDER BY employee_no) g FROM employees WHERE tenant_id = :perf_tid) e
CROSS JOIN generate_series(0,29) d;

-- ============ WS-C: RECRUITMENT + AUDIT ============
-- one open vacancy + 200 applicants across stages + stage history
INSERT INTO vacancy (id, reference_number, title, status, employment_type, headcount, filled_count,
  description, publish_to_public_careers, tenant_id, created_at, is_deleted)
VALUES ('4f000000-0000-4000-8000-0000000000c1', 'VAC-PERF-001', 'Perf Engineer', 'Open', 'FullTime', 5, 0,
  'seed vacancy', true, :perf_tid, now(), false);

INSERT INTO applicant (id, vacancy_id, application_reference_number, first_name, last_name, email, phone,
  resume_storage_key, resume_file_name, stage, source, is_internal, applied_at, tenant_id, created_at, is_deleted)
SELECT gen_random_uuid(), '4f000000-0000-4000-8000-0000000000c1', 'APP-'||lpad(g::text,4,'0'),
  'Cand'||g, 'Test'||g, 'cand'||g||'@perf.test', '070'||lpad(g::text,7,'0'),
  'seed/resume'||g||'.pdf', 'resume'||g||'.pdf',
  (ARRAY['Applied','Applied','Screening','Interview','Offer'])[1 + (g % 5)],
  'CareerSite', false, now() - (g % 60) * interval '1 day', :perf_tid, now(), false
FROM generate_series(1,200) g;

INSERT INTO applicant_stage_history (id, applicant_id, from_stage, to_stage, changed_at, tenant_id, created_at, is_deleted)
SELECT gen_random_uuid(), a.id, 'Applied', a.stage, now() - interval '1 day', :perf_tid, now(), false
FROM applicant a WHERE a.tenant_id = :perf_tid AND a.stage <> 'Applied';

-- 50k audit_logs spread across 12 months (for audit-query perf TC)
INSERT INTO audit_logs (id, tenant_id, event_type, action, resource_type, resource_id, detail,
  created_at, is_impersonation_action)
SELECT gen_random_uuid(), :perf_tid,
  (ARRAY['PayrollRun','PayslipView','SalaryUpdate','Login','Export'])[1 + (g % 5)],
  (ARRAY['Create','Read','Update','Delete','Download'])[1 + (g % 5)],
  'Payroll', 'res-'||(g % 1000), 'seed audit '||g,
  timestamptz '2025-07-01 00:00:00+00' + (g % 365) * interval '1 day' + (g % 86400) * interval '1 second',
  false
FROM generate_series(1,50000) g;

COMMIT;

SELECT 'leave_types', count(*) FROM leave_types WHERE tenant_id=:perf_tid
UNION ALL SELECT 'leave_ledger', count(*) FROM leave_ledger WHERE tenant_id=:perf_tid
UNION ALL SELECT 'leave_request', count(*) FROM leave_request WHERE tenant_id=:perf_tid
UNION ALL SELECT 'attendance_log', count(*) FROM attendance_log WHERE tenant_id=:perf_tid
UNION ALL SELECT 'shift', count(*) FROM shift WHERE tenant_id=:perf_tid
UNION ALL SELECT 'vacancy', count(*) FROM vacancy WHERE tenant_id=:perf_tid
UNION ALL SELECT 'applicant', count(*) FROM applicant WHERE tenant_id=:perf_tid
UNION ALL SELECT 'audit_logs', count(*) FROM audit_logs WHERE tenant_id=:perf_tid;
