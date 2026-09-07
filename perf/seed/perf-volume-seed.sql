-- Per-module VOLUME seed for the `perf` throwaway tenant (on top of seed-perf-tenant.sql 5k employees).
-- Unblocks the leave/attendance/recruitment/audit/performance perf TCs. THROWAWAY ONLY — teardown drops with the tenant.
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
DELETE FROM goal              WHERE tenant_id = :perf_tid;
DELETE FROM manager_review    WHERE tenant_id = :perf_tid;
DELETE FROM self_assessment   WHERE tenant_id = :perf_tid;
DELETE FROM cycle_participant WHERE tenant_id = :perf_tid;
DELETE FROM appraisal_cycle   WHERE tenant_id = :perf_tid;

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

-- ============ WS-D: PERFORMANCE (appraisal cycles / reviews / self-assessments / goals) ============
-- ENH-013(b). Unblocks the performance-dashboard NFR-1 perf TCs, which previously had ZERO rows at volume.
--
-- VOLUME SHAPE — why 5 cycles and not 1:
--   The dashboard has two structurally different read paths and one cycle only exercises one of them.
--     * overview / department drill-down / export resolve exactly ONE cycle
--       (ResolveCycleAsync = the cycle with the greatest start_date, ignoring status and type), so they
--       are sized by employees-per-cycle.
--     * trend takes `cycleIds` and, when that is OMITTED (the default the dashboard actually issues),
--       trends ALL non-probation cycles by running the whole LoadPopulationAsync pipeline once PER CYCLE
--       in a foreach. Its cost is therefore ~(population work) x (cycle count) — an N+1 over cycles.
--   With a single cycle that loop runs once and trend measures nothing that overview did not already
--   measure. So: 4 non-probation cycles, each carrying reviews for ALL 5000 employees, which makes the
--   default trend request do four full 5k population passes. Seeding historical cycles with a token
--   sample instead would make trend look cheap for the wrong reason — the endpoint would be fast because
--   the fixture was small, not because the query is good.
--
-- TRAP — the probation cycle is deliberately dated BEFORE the current one:
--   ResolveCycleAsync picks max(start_date) with no regard for type or status. A probation cycle dated
--   after FY2026 would become the DEFAULT cycle for overview/export, and because `includeProbation`
--   defaults to false the population resolves to EMPTY — a fast, green, completely meaningless 200.
--   PRF-CYC-05 therefore starts 2025-07-01, behind PRF-CYC-04 (2026-01-01). Do not re-date it forward.
--
-- Other deliberate choices:
--   * cycle_participant IS populated. AppraisalCycleService materializes participant rows when a cycle is
--     created, so an empty table would silently send the dashboard down the "no participants => every
--     active employee" fallback branch that a real cycle never takes.
--   * only Submitted manager_reviews contribute a score, so the current cycle is 70% Submitted / 30% Draft:
--     the completion counters and the Submitted-only score filter both have to do real work. Closed
--     historical cycles are 100% Submitted.
--   * scores fan out across the whole 1..5 rating scale (histogram buckets are all non-empty) and drift
--     +0.1 per cycle, so the trend line moves — a flat line cannot distinguish "computed" from "constant".
--   * is_calibration_enabled = false on every cycle: the overview only touches rating_calibration when it
--     is on, and that table is out of scope here. Turn it on only alongside a rating_calibration seed.
--   * fixed literal cycle ids (not gen_random_uuid()) so k6 scripts and TCs can pin `cycleId`/`cycleIds`
--     across re-runs.

-- cycle definition table drives every insert below: k = ordinal, pop = employees enrolled.
CREATE TEMP TABLE _prf_cycle ON COMMIT DROP AS
SELECT * FROM (VALUES
  ('5f000000-0000-4000-8000-0000000000d1'::uuid, 1, 'FY2023 Annual Review',  'Completed', 'Annual',    date '2023-01-01', date '2023-12-31', 5000, 100),
  ('5f000000-0000-4000-8000-0000000000d2'::uuid, 2, 'FY2024 Annual Review',  'Completed', 'Annual',    date '2024-01-01', date '2024-12-31', 5000, 100),
  ('5f000000-0000-4000-8000-0000000000d3'::uuid, 3, 'FY2025 Annual Review',  'Closed',    'Annual',    date '2025-01-01', date '2025-12-31', 5000, 100),
  ('5f000000-0000-4000-8000-0000000000d5'::uuid, 4, 'H2-2025 Probation',     'Closed',    'Probation', date '2025-07-01', date '2025-09-30',  300, 100),
  ('5f000000-0000-4000-8000-0000000000d4'::uuid, 5, 'FY2026 Annual Review',  'Active',    'Annual',    date '2026-01-01', date '2026-12-31', 5000,  70)
) v(cycle_id, k, cname, cstatus, ctype, sdate, edate, pop, submitted_pct);

INSERT INTO appraisal_cycle (id, name, status, type, participant_scope, start_date, end_date,
  goal_setting_start, goal_setting_end, self_assessment_start, self_assessment_end,
  manager_review_start, manager_review_end, rating_scale_max, self_weight_percent,
  is360enabled, is_anonymous_feedback, is_calibration_enabled, min360peer_reviewers,
  signoff_auto_close_days, scope_department_ids, ratings_published_on,
  tenant_id, created_at, is_deleted)
SELECT c.cycle_id, c.cname, c.cstatus, c.ctype, 'AllEmployees',
  c.sdate::timestamptz, c.edate::timestamptz,
  c.sdate::timestamptz,                          (c.sdate + 30)::timestamptz,
  (c.edate - 60)::timestamptz,                   (c.edate - 30)::timestamptz,
  (c.edate - 30)::timestamptz,                   c.edate::timestamptz,
  5, 30, false, false, false, 2, 7, '[]'::jsonb,
  CASE WHEN c.cstatus IN ('Closed','Completed') THEN c.edate::timestamptz END,
  :perf_tid, now(), false
FROM _prf_cycle c;

-- employees enrolled per cycle: the first `pop` by employee_no. g is reused by every insert below so a
-- given employee gets the same deterministic score/status across re-runs.
CREATE TEMP TABLE _prf_pop ON COMMIT DROP AS
SELECT c.cycle_id, c.k, c.sdate, c.edate, c.submitted_pct, e.id AS employee_id, e.g
FROM _prf_cycle c
JOIN (SELECT id, row_number() OVER (ORDER BY employee_no) g FROM employees WHERE tenant_id = :perf_tid) e
  ON e.g <= c.pop;

-- cycle_participant: 1 row per (cycle, employee). Satisfies ix_cycle_participant_tenant_id_cycle_id_employee_id.
INSERT INTO cycle_participant (id, cycle_id, employee_id, tenant_id, created_at, is_deleted)
SELECT gen_random_uuid(), p.cycle_id, p.employee_id, :perf_tid, now(), false FROM _prf_pop p;

-- self_assessment: 1 row per (cycle, employee). weighted_self_score spans the 1..5 scale.
INSERT INTO self_assessment (id, cycle_id, employee_id, status, weighted_self_score, submitted_at,
  tenant_id, created_at, is_deleted)
SELECT gen_random_uuid(), p.cycle_id, p.employee_id,
  CASE WHEN (p.g % 100) < p.submitted_pct THEN 'Submitted' ELSE 'Draft' END,
  CASE WHEN (p.g % 100) < p.submitted_pct
       THEN round((1.0 + ((p.g * 11 + p.k * 5) % 41) / 10.0)::numeric, 2) END,
  CASE WHEN (p.g % 100) < p.submitted_pct THEN (p.edate - 45)::timestamptz END,
  :perf_tid, now(), false
FROM _prf_pop p;

-- manager_review: 1 row per (cycle, employee) — satisfies the partial UNIQUE
-- ix_manager_review_tenant_id_cycle_id_employee_id (tenant_id, cycle_id, employee_id) WHERE is_deleted=false.
-- Only Submitted rows carry scores; Draft rows leave every score column NULL, which is what the
-- dashboard's completion counters are supposed to notice.
-- reviewer_employee_id has no FK but is pointed at a real employee anyway (first employee of the same
-- department) so team-scope queries resolve against live rows.
INSERT INTO manager_review (id, cycle_id, employee_id, reviewer_employee_id, status,
  weighted_manager_score, final_score, self_score_at_submit, summary_comment, flag, submitted_at,
  is_locked, signoff_status, tenant_id, created_at, is_deleted)
SELECT gen_random_uuid(), p.cycle_id, p.employee_id, r.reviewer_id,
  CASE WHEN sub.is_sub THEN 'Submitted' ELSE 'Draft' END,
  CASE WHEN sub.is_sub THEN sc.mgr END,
  CASE WHEN sub.is_sub THEN round(sc.mgr * 0.70 + sc.slf * 0.30, 2) END,
  CASE WHEN sub.is_sub THEN sc.slf END,
  CASE WHEN sub.is_sub THEN 'seed manager review' END,
  (ARRAY['None','None','None','None','None','None','None','Recognition','Promotion','Pip'])[1 + (p.g % 10)],
  CASE WHEN sub.is_sub THEN (p.edate - 15)::timestamptz END,
  sub.is_sub AND p.k <= 4,
  CASE WHEN NOT sub.is_sub THEN 'NotStarted'
       WHEN p.k <= 4 THEN 'SignedOff'
       ELSE (ARRAY['NotStarted','NotesAdded','PendingEmployeeSignOff','SignedOff'])[1 + (p.g % 4)] END,
  :perf_tid, now(), false
FROM _prf_pop p
CROSS JOIN LATERAL (SELECT ((p.g % 100) < p.submitted_pct) AS is_sub) sub
CROSS JOIN LATERAL (SELECT
    least(5.0, round((1.0 + ((p.g * 13 + p.k * 7) % 41) / 10.0 + p.k * 0.1)::numeric, 2)) AS mgr,
    round((1.0 + ((p.g * 11 + p.k * 5) % 41) / 10.0)::numeric, 2) AS slf) sc
LEFT JOIN LATERAL (
  SELECT m.id AS reviewer_id FROM employees m
  WHERE m.tenant_id = :perf_tid
    AND m.department_id = (SELECT department_id FROM employees WHERE id = p.employee_id)
  ORDER BY m.employee_no LIMIT 1
) r ON true;

-- goal: 3 per (cycle, employee), weights 40/30/30 = 100. No unique index on goal, so no collision risk.
INSERT INTO goal (id, cycle_id, employee_id, title, description, category, weight, target_value,
  measurement_unit, due_date, parent_goal_id, status, tenant_id, created_at, is_deleted)
SELECT gen_random_uuid(), p.cycle_id, p.employee_id,
  'Perf goal ' || n || ' / C' || p.k, 'seed goal',
  (ARRAY['Kpi','Competency','Project'])[n],
  (ARRAY[40,30,30])[n],
  (10 * n + (p.g % 90))::text, (ARRAY['Percent','Count','Score'])[n],
  p.edate - 30, NULL,
  CASE WHEN p.k <= 4 THEN 'Finalized'
       ELSE (ARRAY['Draft','Submitted','Acknowledged','Finalized'])[1 + (p.g % 4)] END,
  :perf_tid, now(), false
FROM _prf_pop p CROSS JOIN generate_series(1,3) n;


COMMIT;

SELECT 'leave_types', count(*) FROM leave_types WHERE tenant_id=:perf_tid
UNION ALL SELECT 'leave_ledger', count(*) FROM leave_ledger WHERE tenant_id=:perf_tid
UNION ALL SELECT 'leave_request', count(*) FROM leave_request WHERE tenant_id=:perf_tid
UNION ALL SELECT 'attendance_log', count(*) FROM attendance_log WHERE tenant_id=:perf_tid
UNION ALL SELECT 'shift', count(*) FROM shift WHERE tenant_id=:perf_tid
UNION ALL SELECT 'vacancy', count(*) FROM vacancy WHERE tenant_id=:perf_tid
UNION ALL SELECT 'applicant', count(*) FROM applicant WHERE tenant_id=:perf_tid
UNION ALL SELECT 'audit_logs', count(*) FROM audit_logs WHERE tenant_id=:perf_tid
UNION ALL SELECT 'appraisal_cycle', count(*) FROM appraisal_cycle WHERE tenant_id=:perf_tid
UNION ALL SELECT 'cycle_participant', count(*) FROM cycle_participant WHERE tenant_id=:perf_tid
UNION ALL SELECT 'self_assessment', count(*) FROM self_assessment WHERE tenant_id=:perf_tid
UNION ALL SELECT 'manager_review', count(*) FROM manager_review WHERE tenant_id=:perf_tid
UNION ALL SELECT 'goal', count(*) FROM goal WHERE tenant_id=:perf_tid;
