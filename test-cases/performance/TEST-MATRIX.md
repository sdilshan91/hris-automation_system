---
module: Performance Management
total_user_stories: 9
total_test_cases: 164
created: 2026-06-16
updated: 2026-06-16
status: in-progress
---

# Performance Management -- Test Matrix

> US-PRF-001 (Manager Sets Goals/KPIs for Team Members) is the FIRST Performance Management story and establishes `test-cases/performance/` (dir + TEST-MATRIX + the root Performance Management section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/security/performance/accessibility (TC-PRF-001-01..12) + 4 dedicated multi-tenant isolation on the new `goals` table (TC-PRF-ISO-001..004). The Performance module reuses the same per-story-suffix functional ID scheme as Recruitment/Payroll (TC-PRF-{NNN}-XX) with a separate running ISO counter (TC-PRF-ISO-NNN). All 5 acceptance criteria of US-PRF-001 are covered.
>
> KEY notes: happy path (TC-PRF-001-01) open-window goal form -> add goals summing to exactly 100% in 5% increments -> Save -> goals persisted tenant-scoped + linked to employee+cycle + employee notified (AC-1/2, FR-1/2/3/7); negatives -- weights summing to 95% and 105% rejected with "Goal weights must total 100%" client+server (AC-3, TC-PRF-001-02), goal count <1 or >10 (BR-2, TC-PRF-001-03), weight not in 5% increments (BR-3, TC-PRF-001-04); boundary (TC-PRF-001-05) exactly 100% / exactly 1 and exactly 10 goals / title 200 + description 2000 max length (FR-2, BR-2/3); team dashboard with per-member status + progress (AC-4, TC-PRF-001-06); authz -- only direct reporting manager or HR with Performance.SetGoal.All can set goals; non-managing manager/employee/unauth blocked (BR-4, TC-PRF-001-07); closed/not-yet-open goal-setting window read-only + "The goal-setting window for this cycle has closed" + server-side block on create/edit/delete (AC-5/BR-1, TC-PRF-001-08); input validation + XSS/SQLi sanitization + category enum + weight 1-100 + valid date (FR-2, TC-PRF-001-09); optimistic concurrency -- two sessions editing the same employee's goals, stale save -> 409, no lost update (NFR-4, TC-PRF-001-10); performance -- team goal list of <=50 members <=400ms P95 (NFR-1, TC-PRF-001-11); accessibility -- goal form + stacked weight bar + drag-reorder + team dashboard WCAG 2.1 AA across 360px-4K with keyboard-accessible reorder (NFR-3, TC-PRF-001-12).
>
> Tenant isolation (NFR-2): TC-PRF-ISO-001 cross-tenant read (Tenant B sees zero of Tenant A's goals, incl. by direct ID), ISO-002 no/invalid/mismatched tenant-context rejection + cross-tenant IDOR block, ISO-003 cross-tenant write block + server-derived tenant_id (no body injection) + foreign employee_id/cycle_id rejected, ISO-004 tenant-scoped goal-list/dashboard caches + goal-assignment notification scoping.
>
> CONDITIONAL/DEFERRED (written as conditional, not gaps): (1) NFR-2 specifies PostgreSQL RLS (`tenant_id = current_setting('app.current_tenant_id')`) on the Goals table; this platform enforces isolation via EF Core global query filters + TenantInterceptor -- TC-PRF-ISO-001/003 describe the EF mechanism and note RLS as an extension point (same caveat as Attendance/Leave/Recruitment/Payroll). (2) US-PRF-001 depends on US-PRF-004 (HR creates/manages appraisal cycles) for the active cycle + goal-setting window dates -- the cycle/window are assumed seeded; the window-state branches (open/closed/not-yet-open) are asserted against those dates. (3) FR-7 employee notification DELIVERY (in-app + email) is CONDITIONAL on the Notification System (S25) -- the enqueue/in-app push is asserted (TC-PRF-001-01, TC-PRF-ISO-004). (4) The team goal-list cache (NFR-1, TC-PRF-001-11/TC-PRF-ISO-004) is CONDITIONAL on a cache layer existing (S10) -- if computed on demand today it asserts tenant-filtered queries with no shared/global key. (5) FR-4 goal cascading (link to a departmental/org objective), FR-5 clone-from-previous-cycle / template library, FR-6 audit logging, and BR-5 acknowledged-goals-require-HR-approval-to-modify touch surfaces owned by later Performance stories / the Audit module (S24) -- they are NOT asserted in US-PRF-001's set and are deferred to those stories. (6) NFR-1 50-member <=400ms P95 SLA requires a seeded performance environment.

> US-PRF-002 (Employee Self-Rates Against Goals) is the SECOND Performance story. It adds 19 test cases: 15 functional/security/performance/accessibility (TC-PRF-002-01..15) + 4 dedicated multi-tenant isolation on the new `self_assessment` table + attachments + auto-save drafts + notifications (TC-PRF-ISO-005..008, continuing the running ISO counter from 004). All 5 acceptance criteria of US-PRF-002 are covered.
>
> KEY notes: happy path (TC-PRF-002-01) open-window My Review form shows every goal with self-rating/achievement/comment inputs -> rate all goals with valid (>=20-char) comments -> Submit -> status "Self-Assessment Submitted", weighted self-score computed, manager notified, further edits locked (AC-1/2, FR-1/2/3/4); negatives -- submit with one goal unrated rejected, partial saved as draft only (BR-2, TC-PRF-002-02), comment <20 chars / rating outside the configured scale / achievement % outside 0-100 rejected client+server (FR-2/3, TC-PRF-002-03); boundary (TC-PRF-002-04) comment exactly 20 chars / achievement 0 and 100 / rating at scale min(1) and max(5), one past each boundary rejected; save-as-draft + resume across sessions (AC-3/FR-6, TC-PRF-002-05); auto-save every 60s + crash recovery (NFR-3, TC-PRF-002-06); closed-window read-only + exact "The self-assessment period for this cycle has ended" + server block (AC-4/BR-1, TC-PRF-002-07); Hangfire deadline reminder in-app + email to non-submitters only (AC-5/FR-7, TC-PRF-002-08); authz -- employee sees only their OWN assessment via `Performance.Read.Self`, Employee A cannot view/edit Employee B's incl. IDOR by id/employeeId, unauth 401 (NFR-2, TC-PRF-002-09); file attachment limits exactly 5 files / 10MB inclusive, 6th + 10MB+1 rejected (FR-5, TC-PRF-002-10); file upload security virus-scan-before-accept + tenant-scoped storage path (NFR-4, TC-PRF-002-11); weighted self-score computation from ratings*weights with BR-4 self:manager ratio applied only at final score (FR-4, TC-PRF-002-12); submitted assessment locked unless manager/HR reopens, employee cannot self-reopen (BR-3, TC-PRF-002-13); performance form load <=400ms P95 incl. all goal data (NFR-1, TC-PRF-002-14); accessibility self-assessment UI WCAG 2.1 AA + responsive 360px, rating inputs usable via touch + keyboard (NFR-5, TC-PRF-002-15).
>
> Tenant isolation (NFR-2) for US-PRF-002: TC-PRF-ISO-005 cross-tenant read of self_assessment (Tenant B sees zero of Tenant A's records, incl. by direct ID), ISO-006 no/invalid/mismatched tenant-context rejection + cross-tenant IDOR block on the self-assessment APIs, ISO-007 cross-tenant write block + server-derived tenant_id (no body injection) + foreign goal_id/cycle_id/employee_id rejected, ISO-008 tenant-scoped attachment storage paths + auto-save drafts + submission/reminder notifications.
>
> CONDITIONAL/DEFERRED for US-PRF-002 (written as conditional, not gaps): (1) same NFR-2 RLS caveat -- the self_assessment table spec names PostgreSQL RLS; isolation is enforced via EF Core global query filters + TenantInterceptor with RLS noted as an extension point (ISO-005/007). (2) DEPENDS ON US-PRF-001 (goals assigned/acknowledged) and US-PRF-004 (appraisal cycle + self-assessment window dates) -- goals/cycle/window assumed seeded; window-state branches asserted against those dates. (3) FR-7 notification DELIVERY (manager submission notice + Hangfire deadline reminder, in-app + email) CONDITIONAL on the Notification System (S25) -- the in-app push + email enqueue are asserted (TC-PRF-002-01/-08, TC-PRF-ISO-008). (4) NFR-4 virus scanning -- if no scanner is wired today, TC-PRF-002-11 asserts the upload routes through the scan SEAM and documents it as an extension point; tenant-scoped storage paths are asserted regardless. (5) Any draft/notification cache or queue key (S10) is asserted tenant-scoped, CONDITIONAL on a cache layer -- if computed on demand it asserts tenant-filtered access with no shared/global key (TC-PRF-ISO-008). (6) NFR-1 400ms P95 requires a seeded performance environment (TC-PRF-002-14). (7) BR-4 self:manager final-score ratio is checked only insofar as it is NOT baked into the raw weighted self-score (TC-PRF-002-12); the final composite score is owned by a later Performance story (manager assessment / final calibration). (8) BR-5 (self-assessment optional when disabled in tenant config) is a precondition (self-assessment enabled) for US-PRF-002's set, not separately asserted here.

> US-PRF-003 (Manager Rates Employee Performance) is the THIRD Performance story -- it is the manager-side counterpart to US-PRF-002 and where the FINAL combined score is first computed. It adds 17 test cases: 13 functional/security/performance/accessibility (TC-PRF-003-01..13) + 4 dedicated multi-tenant isolation on the new `review` (manager-assessment) table + dashboard caches + notifications + audit (TC-PRF-ISO-009..012, continuing the running ISO counter from 008). All 5 acceptance criteria of US-PRF-003 are covered.
>
> KEY notes: happy path (TC-PRF-003-01) manager opens a direct report with submitted self-assessment -> side-by-side self vs manager view (AC-1, FR-1) -> rates all goals with >=20-char comments (FR-3) + summary <=5000 chars (FR-5) -> Submit -> weighted manager score AND final combined score computed (FR-4), status "Manager Review Submitted", employee notified, review locked (AC-2); negatives -- submit with unrated goal(s) -> error LISTING the unrated goals, blocked client+server (AC-3, TC-PRF-003-02), comment <20 chars / rating outside the configured scale / summary > 5000 rejected (FR-2/3/5, TC-PRF-003-03); FINAL-score calculation `(self*self_w)+(manager*manager_w)` data-driven across ratios 50:50, 30:70, 0:100 with server authoritative (FR-4/BR-4, TC-PRF-003-04); boundary (TC-PRF-003-05) rating at scale min(1)+max(5), comment exactly 20, summary exactly 5000, one past each rejected; Team Reviews dashboard status workflow pending self-assessment -> self-assessment submitted -> manager review pending -> completed, color-coded (AC-4, TC-PRF-003-06); scope authz -- manager can ONLY review DIRECT REPORTS, non-report -> 403 incl. IDOR, org tree authoritative, unauth 401 (BR-2/NFR-2, TC-PRF-003-07); HR `Performance.Review.All` reviews anyone + reopens a submitted review, a `.Team`-only manager cannot reopen (AC-5/BR-3, TC-PRF-003-08); submitted review locked/read-only + manager-review window enforced before/after (AC-5/BR-1, TC-PRF-003-09); optimistic concurrency -- HR + manager editing the same review, stale save -> 409, no lost update (NFR-3, TC-PRF-003-10); audit -- every rating/reopen/re-submit logged with user id + timestamp, tenant-scoped (FR-7, TC-PRF-003-11); performance -- single-employee review form (incl. self-assessment data) <=400ms P95, no N+1 (NFR-1, TC-PRF-003-12); accessibility -- WCAG 2.1 AA + keyboard-operable rating inputs + 360px stacked layout replacing side-by-side (NFR-4, TC-PRF-003-13).
>
> Tenant isolation (NFR-2) for US-PRF-003: TC-PRF-ISO-009 cross-tenant read of the review table (Tenant B sees zero of Tenant A's reviews, incl. by direct id; HR `.All` is tenant-bounded so it cannot reopen another tenant's review), ISO-010 no/invalid/mismatched tenant-context rejection + cross-tenant IDOR block on the review APIs, ISO-011 cross-tenant write block + server-derived tenant_id (no body injection) + foreign employee_id/cycle_id/goal_id rejected, ISO-012 tenant-scoped dashboard caches + submission notifications + audit entries.
>
> CONDITIONAL/DEFERRED for US-PRF-003 (written as conditional, not gaps): (1) same NFR-2 RLS caveat -- S7 names PostgreSQL RLS on the Review table; isolation is enforced via EF Core global query filters + TenantInterceptor with RLS noted as an extension point (ISO-009/011). (2) DEPENDS ON US-PRF-001 (goals), US-PRF-002 (submitted self-assessment), US-PRF-004 (cycle + manager-review window dates), and Core HR org tree (manager-report relationships) -- all assumed seeded; window-state branches asserted against those dates and scope against the org tree. (3) FR-7 notification DELIVERY (employee submission notice) CONDITIONAL on the Notification System (S25) -- in-app push asserted, email enqueue asserted, delivery conditional (TC-PRF-003-01, TC-PRF-ISO-012). (4) The dashboard/review-form cache (NFR-1, TC-PRF-003-12 / TC-PRF-ISO-012) is CONDITIONAL on a cache layer (S10) -- if computed on demand it asserts tenant-filtered queries with no shared/global key. (5) FR-7 audit logging surfaces the Audit module (S24); TC-PRF-003-11 / TC-PRF-ISO-012 assert the audit entry shape + tenant scoping against the AuditInterceptor seam. (6) NFR-1 400ms P95 requires a seeded performance environment (TC-PRF-003-12). (7) FR-6 (flag for recognition / promotion / PIP) and BR-5 (360-degree peer/report ratings folded into the final score) are NOT asserted in US-PRF-003's set -- FR-6 is a lightweight flag persisted alongside the review (touched in the happy-path summary but no dedicated TC) and BR-5 is explicitly owned by US-PRF-005 (360-degree feedback); both DEFERRED. (8) The tenant self:manager weight ratio (BR-4) is assumed settable via an HR/admin config seam (US-PRF-004-adjacent); TC-PRF-003-04 reconfigures it between runs.

> US-PRF-004 (HR Creates and Manages Appraisal Cycles) is the FOURTH Performance story and the upstream owner of the appraisal CYCLE that US-PRF-001/002/003 depend on (active-cycle + phase-window dates). It adds 19 test cases: 15 functional/security/performance/accessibility (TC-PRF-004-01..15) + 4 dedicated multi-tenant isolation on the new `cycles` / `cycle_phases` / `cycle_participants` tables + Hangfire jobs + dashboard caches + notifications (TC-PRF-ISO-013..016, continuing the running ISO counter from 012). All 5 acceptance criteria of US-PRF-004 are covered.
>
> KEY notes: happy path (TC-PRF-004-01) HR opens "Create New Cycle" form with all required fields (AC-1) -> fills valid name/period + >=3 sequential non-overlapping phases within the window + department scope + rating scale + 360/calibration/weight config -> Create -> cycle+phases+participants persisted tenant-scoped, Hangfire phase-transition/reminder jobs scheduled, confirmation shown (AC-2, FR-1/2/3/5/6); negatives -- overlapping/non-sequential/reversed/zero-duration phases rejected client+server (FR-2, TC-PRF-004-02), phase dates outside the cycle window rejected incl. shrinking the window under a phase (BR-3, TC-PRF-004-03), create/edit/clone/transition/cancel by a non-authorized user (manager `.Team`/employee/unauth) blocked 403/401 (BR-1, TC-PRF-004-04); cycle dashboard timeline + per-phase completion % (goal-setting/self-assessment/manager-review) + overdue counts over the tenant participant set (AC-3, TC-PRF-004-05); phase extension re-validates sequencing/non-overlap/window, reschedules Hangfire jobs, notifies only affected (non-completed) participants (AC-5/FR-2/FR-5, TC-PRF-004-06); Hangfire deadline reminder fires to current-phase non-completers only, runs in tenant context, retries with Polly exponential backoff, idempotent (AC-4/FR-5/NFR-3, TC-PRF-004-07); status transitions Draft->Active->Paused->Active->Completed + Draft->Cancelled valid, invalid transitions (Completed->Active, Cancelled->Active, Draft->Completed) rejected, paused cycle suspends reminders (FR-7, TC-PRF-004-08); BR-2 cannot delete a cycle with submitted reviews (cancel only; empty Draft still deletable) + BR-6 cancellation requires a reason + notifies ALL participants, review data retained (TC-PRF-004-09); BR-5 rating scale editable in Draft, locked once Active (TC-PRF-004-10); participant scoping -- department scope excludes other departments, manual add of an out-of-scope employee rejected (FR-3), employee cannot be in two ACTIVE cycles of the SAME type but may span an annual + a quarterly (BR-4/FR-4, TC-PRF-004-11); clone a completed cycle copies all config (phases re-anchored, scope, scale, weights, toggles) into a new Draft with fresh dates and NO progress/review data (FR-8, TC-PRF-004-12); performance -- creation of 5,000 participants <=5s + dashboard <=2s P95 with set-based aggregates (NFR-1/NFR-4, TC-PRF-004-13); accessibility -- cycle form + timeline WCAG 2.1 AA, keyboard-operable date pickers + department tree, status-badge contrast not color-only, vertical stepper + swipeable stat cards at 360px (S8, TC-PRF-004-14); boundary -- FR-1 minimum 3 phases enforced (2 rejected, exactly 3 accepted), first-start==cycle-start / last-end==cycle-end inclusive, contiguous adjacent phases valid, same-boundary-day handled per a documented contiguity rule (FR-1/FR-2/BR-3, TC-PRF-004-15).
>
> Tenant isolation (NFR-2) for US-PRF-004: TC-PRF-ISO-013 cross-tenant read of cycles/phases/participants/dashboard (Tenant B sees zero of Tenant A's, incl. by direct id), ISO-014 missing/invalid/mismatched tenant-context rejection + cross-tenant IDOR block on the cycle APIs, ISO-015 cross-tenant write block + server-derived tenant_id (no body injection) + foreign department/employee/rating-scale rejected, ISO-016 tenant-scoped Hangfire cycle jobs + dashboard caches + phase/cancellation notifications.
>
> CONDITIONAL/DEFERRED for US-PRF-004 (written as conditional, not gaps): (1) same NFR-2 RLS caveat -- S7 names PostgreSQL RLS on cycles/cycle_phases/cycle_participants; isolation is enforced via EF Core global query filters + TenantInterceptor with RLS noted as an extension point (ISO-013/015). (2) DEPENDS ON a configured rating scale (precondition) + Core HR employees/departments/grades for participant scoping -- all assumed seeded. (3) FR-5 notification DELIVERY (phase-start/close, deadline reminder, cancellation; in-app + email) CONDITIONAL on the Notification System (S25) -- in-app push + email enqueue asserted, delivery conditional (TC-PRF-004-06/-07/-09, TC-PRF-ISO-016). (4) The dashboard/aggregate cache (NFR-4, TC-PRF-004-05/-13 / TC-PRF-ISO-016) is CONDITIONAL on a cache layer (S10) -- if computed on demand it asserts tenant-filtered set-based aggregates with no shared/global key. (5) NFR-1 5,000-participant <=5s + NFR-4 dashboard <=2s P95 require a seeded performance environment (TC-PRF-004-13). (6) The FR-6 360-degree / calibration / anonymity toggles are persisted + cloned as configuration here (TC-PRF-004-01/-12); their downstream BEHAVIOR (peer feedback collection, calibration sessions) is owned by later Performance stories (US-PRF-005+) and is NOT asserted in US-PRF-004's set. (7) The same-boundary-day phase contiguity rule (TC-PRF-004-15 step 5) asserts whatever the implementation documents (inclusive ranges -> overlap-reject) is applied CONSISTENTLY; the exact rule is an implementation contract, not a gap.

> US-PRF-005 (360-Degree Review: Peers, Reports, Manager, Self) is the FIFTH Performance story -- it folds multi-perspective peer/report feedback into the final score (the BR-5 deferral US-PRF-003 left open). It adds 18 test cases: 14 functional/security/perf/a11y (TC-PRF-005-01..14) + 4 dedicated multi-tenant isolation on the new `feedback_360` table + reminder jobs + results caches + notifications (TC-PRF-ISO-017..020, continuing the running ISO counter from 016). All 5 acceptance criteria of US-PRF-005 are covered.
>
> KEY notes: happy path (TC-PRF-005-01) HR configures a 360-enabled cycle -> Self + Manager auto-assigned, peers (same dept) + reports (org tree) auto-suggested + manually nominated (AC-1, FR-1/2) -> phase enters feedback, reviewers notified (AC-2) -> all submit competency-based feedback, marked Completed + tracker updates (AC-3) -> HR views aggregated report: per-competency averages + self/manager/peer/report radar + weighted composite (FR-6) incorporated into the final performance score (AC-4, BR-6); negatives -- employee nominated as their OWN Peer rejected client+server (BR-2, TC-PRF-005-02), a reviewer submitting twice for the same reviewee/cycle rejected via unique (tenant,cycle,reviewee,reviewer) (BR-3, TC-PRF-005-03), releasing results BELOW the minimum peer threshold warns/blocks HR (BR-4/FR-3, TC-PRF-005-04), unauthorized 360 config/release + cross-reviewer submit (IDOR) blocked 403/401 (NFR-2, TC-PRF-005-05); ANONYMITY -- with anonymity ON the results API payload contains NO reviewer_id/name/email enforced SERVER-SIDE not just UI, even in debug (FR-5/NFR-3, TC-PRF-005-06), and anonymity cannot be retroactively disabled once feedback exists (BR-5, TC-PRF-005-07); composite/final score (FR-6/BR-6, TC-PRF-005-08) data-driven weighted aggregation (self 10/mgr 40/peers 30/reports 20 -> 4.25 for the seeded set) with per-category averaging before weighting + the composite feeding the final score; Hangfire reviewer reminder to non-submitters only with a deep link, tenant-scoped + idempotent + retried (AC-5/FR-8, TC-PRF-005-09); assignment notification in-app + email with a form link to all assigned reviewers (AC-2, TC-PRF-005-10); performance -- feedback form <=400ms P95 + results+radar <=2s for 20 reviewers, no N+1 (NFR-1/NFR-4, TC-PRF-005-11); accessibility -- feedback form single-column + collapsible at 360px + WCAG 2.1 AA, charts not color-only (NFR-5, TC-PRF-005-12); PDF export -- 360 summary report exportable with branding + anonymized comments, export seam + authz/tenant-scoping asserted (FR-7, TC-PRF-005-13); boundary -- min reviewers per category (1 below vs exactly 2 peers) + radar with 3/5/10 competencies + single-vs-zero-reviewer category handling (FR-3/AC-4, TC-PRF-005-14).
>
> Tenant isolation (NFR-2) for US-PRF-005: TC-PRF-ISO-017 cross-tenant read of feedback_360/assignments/results/reports (Tenant B sees zero of Tenant A's, incl. by direct id), ISO-018 missing/invalid/mismatched tenant-context rejection + cross-tenant IDOR (read + write) block on the 360 APIs, ISO-019 cross-tenant write block + server-derived tenant_id (no body injection) + foreign reviewer/reviewee/cycle rejected, ISO-020 tenant-scoped Hangfire reviewer-reminder jobs + results/aggregate caches + assignment/reminder/results notifications.
>
> CONDITIONAL/DEFERRED for US-PRF-005 (written as conditional, not gaps): (1) same NFR-2 RLS caveat -- S7 names PostgreSQL RLS on feedback_360; isolation is enforced via EF Core global query filters + TenantInterceptor with RLS noted as an extension point (ISO-017/019). (2) DEPENDS ON US-PRF-004 (cycle with 360 toggle + feedback window), US-PRF-001 (goals/competencies), US-PRF-002/003 (self + manager perspectives), and Core HR org tree (manager + direct reports) -- all assumed seeded. (3) FR-7 PDF RENDERING is CONDITIONAL on the PDF library (QuestPDF or similar) being wired -- TC-PRF-005-13 asserts the export SEAM + report data model + authz/tenant-scoping + anonymized comments; full visual fidelity is an extension point. (4) AC-2/AC-5/FR-8 notification + reminder DELIVERY (in-app + email) CONDITIONAL on the Notification System (S25) -- in-app push + email enqueue asserted, delivery conditional (TC-PRF-005-09/-10, TC-PRF-ISO-020). (5) The results/aggregate/completion-tracker cache (NFR-4, TC-PRF-005-11 / TC-PRF-ISO-020) is CONDITIONAL on a cache layer (S10) -- if computed on demand it asserts tenant-filtered set-based aggregates with no shared/global key. (6) NFR-1 <=400ms + NFR-4 <=2s/20-reviewers require a seeded performance environment (TC-PRF-005-11). (7) The BR-4 minimum-peer-threshold OVERRIDE path + the BR-6 360-into-final-score BLENDING RULE are exercised against whatever the implementation documents (TC-PRF-005-04/-08) -- the exact override/blend formula is an implementation contract, not a gap. (8) The zero-reviewer-category renormalization rule (TC-PRF-005-08 step 5 / TC-PRF-005-14 step 6) asserts the documented behavior is applied CONSISTENTLY.

> US-PRF-006 (Performance Review Meeting Notes and Sign-Off) is the SIXTH Performance story -- the formal, auditable sign-off layer that locks a completed review with mutual digital acknowledgement. NOTE: this story has FOUR acceptance criteria (AC-1..AC-4), not five. It adds 18 test cases: 14 functional/security/perf/a11y (TC-PRF-006-01..14) + 4 dedicated multi-tenant isolation on the new `review_meeting_notes` + `review_signoffs` (append-only) tables + auto-close Hangfire jobs + notifications + audit + PDF export (TC-PRF-ISO-021..024, continuing the running ISO counter from 020). All 4 acceptance criteria of US-PRF-006 are covered.
>
> KEY notes: happy path (TC-PRF-006-01) manager (review already submitted, BR-1) clicks "Add Meeting Notes" -> templated rich-text editor with the four sections strengths/areas-for-improvement/agreed-actions-with-deadlines/summary + goal-titles+ratings reference (AC-1, FR-1/2) -> "Request Employee Sign-Off" saves notes + records the manager sign-off (name+timestamp+IP) + status "Pending Employee Sign-Off" + notifies the employee (AC-2, FR-3/7) -> employee reads the notes (BR-2 read-tracking) + "Acknowledge & Sign" -> employee signature (name+timestamp+IP) recorded, status "Signed Off", review LOCKED (AC-3, BR-5) -> HR/manager views the full record + Export PDF option (AC-4); negatives -- notes/sign-off rejected BEFORE the manager review is submitted (BR-1, TC-PRF-006-02), a dispute submitted without comments rejected client+server (FR-4, TC-PRF-006-03); DISPUTE flow (TC-PRF-006-04) employee disputes with comments -> manager + HR notified with the comments -> status "Disputed" until HR resolves, not locked + not auto-closed while Disputed (AC-3/FR-4/FR-5/BR-4), and HR resolution (TC-PRF-006-12) by AMEND (edit + re-request sign-off -> Pending Employee Sign-Off) or CONFIRM (uphold), `.Team`-only manager cannot resolve (BR-4/FR-5); AUTO-CLOSE (TC-PRF-006-05) employee does not sign within the tenant-configurable window -> Hangfire closes to "No Response" + notifies HR, idempotent + tenant-scoped, signed/disputed reviews untouched (BR-3); READ-TRACKING (TC-PRF-006-06) system records the notes were opened/read before signing (BR-2); IMMUTABILITY (TC-PRF-006-07) a recorded signature cannot be modified/deleted by anyone incl. HR + a locked review cannot be edited -- only a system-admin compliance correction (audited) may touch it, review_signoffs is append-only (NFR-3/BR-5); AUDIT (TC-PRF-006-08) every sign-off action immutably logged with user id + server timestamp + server-derived IP, client-spoofed IP/timestamp ignored (FR-7); PDF EXPORT (TC-PRF-006-09) complete review (goals/ratings/notes/signatures) exportable with tenant branding <=3s, authz + tenant-scoped, rendering CONDITIONAL on the PDF library (AC-4/FR-6/NFR-4); template + rich-text XSS/HTML sanitization (TC-PRF-006-10, FR-1/2/S10); authz + ordering (TC-PRF-006-11) only the managing manager/HR add notes, only the assigned employee signs (manager cannot sign on the employee's behalf), manager-first ordering server-enforced, IDOR/unauth blocked (FR-3/BR-1); performance notes editor <=400ms P95, no N+1 on the goals/ratings reference (NFR-1, TC-PRF-006-13); accessibility sign-off flow at 360px + touch-friendly confirmation dialogs + WCAG 2.1 AA (NFR-5, TC-PRF-006-14).
>
> Tenant isolation (NFR-2) for US-PRF-006: TC-PRF-ISO-021 cross-tenant read of meeting notes/sign-offs/disputes/signed records (Tenant B sees zero of Tenant A's, incl. by direct id), ISO-022 missing/invalid/mismatched tenant-context rejection + cross-tenant IDOR (sign/dispute/resolve/export another tenant's review), ISO-023 cross-tenant write block + server-derived tenant_id (no body injection) + foreign review/employee rejected, ISO-024 tenant-scoped Hangfire auto-close jobs + sign-off/dispute/auto-close notifications + audit entries + PDF export + caches.
>
> CONDITIONAL/DEFERRED for US-PRF-006 (written as conditional, not gaps): (1) same NFR-2 RLS caveat -- S7 names PostgreSQL RLS on review_meeting_notes/review_signoffs; isolation is enforced via EF Core global query filters + TenantInterceptor with RLS noted as an extension point (ISO-021/023). (2) DEPENDS ON US-PRF-003 (submitted manager review, BR-1 gate), US-PRF-004 (cycle with a sign-off phase), and a tenant-configured meeting-notes template + auto-close window -- all assumed seeded. (3) FR-6 PDF RENDERING is CONDITIONAL on the PDF library being wired -- TC-PRF-006-09 asserts the export SEAM + report data model (goals/ratings/notes/signatures) + tenant branding + authz/tenant-scoping; full visual fidelity is an extension point (consistent with US-PRF-005 FR-7). (4) AC-2/FR-5/BR-3 notification DELIVERY (sign-off request, dispute escalation to manager+HR, auto-close to HR; in-app + email) CONDITIONAL on the Notification System (S25) -- in-app push + email enqueue asserted, delivery conditional (TC-PRF-006-01/-04/-05/-12, TC-PRF-ISO-024). (5) The editor reference cache (NFR-1, TC-PRF-006-13 / TC-PRF-ISO-024) is CONDITIONAL on a cache layer (S10) -- if computed on demand it asserts tenant-filtered queries with no shared/global key. (6) NFR-1 400ms P95 + NFR-4 3s PDF require a seeded performance environment. (7) BR-2 read-before-sign ENFORCEMENT (hard block vs. recorded-flag) is asserted against whatever the implementation documents (TC-PRF-006-06) -- the exact gate is an implementation contract, not a gap; the read state is never silently lost. (8) BR-5 system-admin compliance-correction path (TC-PRF-006-07) is the ONLY route to touch a locked review -- it is asserted as gated to the admin role + audited; the system-admin console itself is owned by the Admin module. (9) FR-7 audit surfaces the Audit module (S24) -- the audit entry shape (user id + timestamp + IP) + tenant scoping are asserted against the AuditInterceptor seam.

> US-PRF-007 (Performance Dashboard and Analytics) is the SEVENTH Performance story -- the HR-facing analytics/reporting layer that aggregates all prior performance data (goals/self/manager/360/cycles) into a tenant-scoped dashboard with charts, filters, drill-down, multi-cycle trend, and export. It adds 19 test cases: 15 functional/security/perf/a11y (TC-PRF-007-01..15) + 4 dedicated multi-tenant isolation on the new `performance_summary` materialized view + aggregate caches + export artifacts + Hangfire refresh jobs (TC-PRF-ISO-025..028, continuing the running ISO counter from 024). All 5 acceptance criteria of US-PRF-007 are covered.
>
> KEY notes: happy path (TC-PRF-007-01) HR opens Performance > Dashboard -> overview renders cycle completion rate + average score + score distribution histogram (FR-1 chart.js) + department-wise average bar (FR-2) + top/bottom-N performers with name/dept/score/trend (FR-3, default N=10) + cycle progress (participants/goal-setting/self/manager/signed-off, FR-6), all tenant-scoped (AC-1); filters (TC-PRF-007-02) by department + grade + cycle (+ location, employment type) update ALL widgets to the filtered population, combined AND-composed server-side (AC-2/FR-4); multi-cycle trend (TC-PRF-007-03) select 3 cycles -> chart.js line of org-wide average-score series in chronological order + per-department overlay, single-cycle boundary (AC-3/FR-7); drill-down (TC-PRF-007-04) click a department bar -> that department's employee list with individual scores + breadcrumb (Dashboard > Dept > Employee), filter context carried (FR-5/S8); export (TC-PRF-007-05) CSV + Excel(XLSX) + PDF reflect visible charts/tables, data accuracy + tenant branding (primary color/logo) on PDF + <=5s for 5,000 employees, rendering asserted at the export SEAM (AC-4/FR-8/NFR-5); SCOPE -- manager dashboard (TC-PRF-007-06) scoped to direct reports only + a "team ranking" INSTEAD of org-wide top/bottom (AC-5/BR-1/BR-3), an employee is redirected to their own review page + dashboard/drill/export endpoints reject employee scope (TC-PRF-007-07/BR-1), and the server rejects a manager pulling org-wide aggregates via scope=org / foreign deptId / top-bottom endpoint / no-scope param -- scope derived from identity not client input (TC-PRF-007-08/AC-5/BR-1/BR-3); BR-2 (TC-PRF-007-09) distribution + aggregates EXCLUDE probation-cycle employees by default, included only via the explicit filter, enforced server-side; top/bottom detail (TC-PRF-007-10) ordering + configurable N + trend indicator + deterministic ties (FR-3); performance (TC-PRF-007-11) overview <=2.5s P95 @ 5,000 employees via the materialized-view / Redis aggregate path with no per-employee N+1 (NFR-1/NFR-3); accessibility (TC-PRF-007-12) charts responsive 360px->4K + WCAG 2.1 AA (keyboard, SR chart summaries, non-color-only) + loading skeletons (NFR-4/S8); BR-4 (TC-PRF-007-13) refresh from performance_summary materialized views on a tenant-configurable interval (default 4h via Hangfire) -- a new review surfaces after a tenant-scoped refresh; combined-filter + empty/single-employee boundary states (TC-PRF-007-14/AC-2/FR-4); filter/query-param injection + type/range validation on dashboard + export endpoints (TC-PRF-007-15).
>
> Tenant isolation (NFR-2) for US-PRF-007: TC-PRF-ISO-025 cross-tenant aggregate read (Tenant B's dashboard shows ZERO Tenant A data incl. by direct cycle/dept id), ISO-026 missing/invalid/mismatched tenant-context rejection + cross-tenant IDOR on overview/trend/drill-down/export, ISO-027 materialized-view aggregates + refresh tenant-DERIVED (server-side tenant_id, no client override, no foreign-id injection into the GROUP BY, refresh writes only the acting tenant's rows), ISO-028 tenant-scoped aggregate caches + export artifacts + Hangfire materialized-view refresh jobs.
>
> CONDITIONAL/DEFERRED for US-PRF-007 (written as conditional, not gaps): (1) same NFR-2 RLS caveat -- S7/NFR-2 name PostgreSQL RLS on performance_summary; isolation is enforced via EF Core global query filters + TenantInterceptor with RLS noted as an extension point (ISO-025/027). (2) DEPENDS ON US-PRF-001/002/003 (goals + submitted self/manager reviews to aggregate), US-PRF-004 (cycles for cycle filtering + trend), and Core HR (department/grade/location for filtering) -- all assumed seeded. (3) NFR-3 Redis aggregate caching is CONDITIONAL on a cache layer being wired -- TC-PRF-007-11 / TC-PRF-ISO-028 assert the materialized-view path with tenant-scoped keys and document Redis as the extension point (consistent with the module's deferred-Redis convention). (4) NFR-3/BR-4 performance_summary materialized-view refresh via Hangfire (default 4h, tenant-configurable) is asserted at the refresh SEAM (TC-PRF-007-13, TC-PRF-ISO-028) -- if the recurring job is not yet scheduled, a manual refresh trigger is asserted and the Hangfire schedule documented as the extension point. (5) FR-8 PDF/XLSX RENDERING is CONDITIONAL on the reporting library (QuestPDF / XLSX writer) being wired -- TC-PRF-007-05 asserts the export data model + tenant branding inputs + <=5s budget at the seam; full visual fidelity is an extension point (consistent with US-PRF-005 FR-7 / US-PRF-006 FR-6). (6) NFR-1 <=2.5s P95 @ 5,000 employees + NFR-5 export <=5s @ 5,000 require a seeded performance environment (TC-PRF-007-11/-05). (7) The FR-3 trend-indicator and tie-break rules (TC-PRF-007-10) + the same-N / configurable-N clamp rule (TC-PRF-007-15) assert whatever the implementation documents is applied CONSISTENTLY; the exact formula/clamp is an implementation contract, not a gap.

> US-PRF-008 (Performance Improvement Plan / PIP) is the EIGHTH Performance story -- the structured, compliance-grade corrective-action workflow for underperforming employees. It adds 19 test cases: 15 functional/security/perf/a11y (TC-PRF-008-01..15) + 4 dedicated multi-tenant isolation on the new `pip` / `pip_objectives` / `pip_checkpoints` tables + Hangfire reminder/ack-timeout jobs + checkpoint attachments + escalation/audit + report artifacts (TC-PRF-ISO-029..032, continuing the running ISO counter from 028). All 5 acceptance criteria of US-PRF-008 are covered.
>
> KEY notes: happy path (TC-PRF-008-01) HR opens "Create PIP" for a flagged employee -> form with employee(pre-filled)/reason/duration/objectives(title+desc+success-criteria+due)/checkpoint-dates/mentor/escalation (AC-1) -> "Initiate PIP" persists pip+objectives+checkpoints tenant-scoped, Draft->Active (FR-2), notifies employee+manager+mentor (in-app+email), schedules Hangfire start/checkpoint-reminder(3 days before)/end/overdue jobs (AC-2, FR-1/3); checkpoint recording (TC-PRF-008-02) manager OR HR records progress/evidence/status(OnTrack/AtRisk/NotMet)/comments/attachment -> employee notified, immutable history entry (AC-3, FR-4, BR-1); lifecycle positive (TC-PRF-008-03) Draft->Active->checkpoint->Extended(new end date + added objectives, FR-6)->Successfully Completed(employee returns to normal) (AC-4, FR-2); lifecycle negative (TC-PRF-008-04) checkpoints Not Met -> outcome Not Met -> HR confirms escalation(reassignment/demotion/non-renewal/termination-recommendation, BR-6) -> stakeholders notified + immutable audit record (AC-4/AC-5, FR-5); negatives -- second active PIP for same employee rejected client+server, released only on terminal state (BR-2, TC-PRF-008-05), duration <30 days rejected + exactly-30 boundary accepted + reversed range rejected (BR-3, TC-PRF-008-06), a MANAGER attempting create/extend/close/escalate rejected 403 (managers checkpoint-only) + employee/unauth blocked, HR positive control (BR-1, TC-PRF-008-07); acknowledgement (TC-PRF-008-08) employee acknowledges (immutable) else 5-BUSINESS-day Hangfire "Not Acknowledged" flag + PIP proceeds (BR-4); visibility (TC-PRF-008-09) ONLY employee/manager/HR/mentor view the PIP, unrelated employee blocked incl. by id, PIP data EXCLUDED from the US-PRF-007 general dashboard/exports (FR-8, BR-5); immutability (TC-PRF-008-10) checkpoint outcomes + status changes + escalation form a complete append-only history, no actor incl. HR edits/deletes, server-derived actor+timestamp, retained per policy (FR-5, NFR-3); encryption (TC-PRF-008-11) reason + escalation notes encrypted at rest via pgcrypto -- asserted at the encryption SEAM, conditional on column-encryption being wired (NFR-4); performance (TC-PRF-008-12) PIP create + checkpoint <=800ms P95, no N+1 (NFR-1); accessibility (TC-PRF-008-13) checkpoint form full-screen single-column at 360px for in-person recording + WCAG 2.1 AA, traffic-light status not color-only (NFR-5); PDF report (TC-PRF-008-14) PIP summary report with objectives/checkpoints/outcomes/signatures + branding + authz(FR-8)/tenant-scoping at the export SEAM, PDF rendering conditional (FR-7); Hangfire (TC-PRF-008-15) start/checkpoint-reminder(3 days prior)/end/overdue jobs fire at the right times to the right recipients, tenant-scoped + idempotent + Polly-retried + rescheduled on extension (FR-3).
>
> Tenant isolation (NFR-2) for US-PRF-008: TC-PRF-ISO-029 cross-tenant read of pip/objectives/checkpoints/escalation/history/report (Tenant B sees zero of Tenant A's, incl. by direct id), ISO-030 missing/invalid/mismatched tenant-context rejection + cross-tenant IDOR (view/checkpoint/extend/outcome/escalation/acknowledge/report), ISO-031 cross-tenant write block + server-derived tenant_id (no body injection) + foreign employee/manager/mentor id rejected, ISO-032 tenant-scoped Hangfire jobs(reminders/end/overdue/ack-timeout) + checkpoint-attachment storage + notifications + audit/history + report artifacts.
>
> CONDITIONAL/DEFERRED for US-PRF-008 (written as conditional, not gaps): (1) same NFR-2 RLS caveat -- S7/NFR-2 name PostgreSQL RLS on pip/pip_objectives/pip_checkpoints; isolation is enforced via EF Core global query filters + TenantInterceptor with RLS noted as an extension point (ISO-029/031). (2) DEPENDS ON US-PRF-003 (manager performance-improvement flag, FR-6) + a completed below-threshold review, US-PRF-007 (general dashboard the PIP must be EXCLUDED from, BR-5), and Core HR org tree (manager/mentor relationships) -- all assumed seeded. (3) NFR-4 pgcrypto column encryption of reason + escalation notes is CONDITIONAL on column-level encryption being wired -- TC-PRF-008-11 asserts the encryption SEAM (`IFieldEncryptor` / pgcrypto) + cleartext-not-at-rest and documents pgcrypto-at-rest as the extension point; tenant-scoping/authz hold regardless. (4) FR-7 PDF RENDERING is CONDITIONAL on the reporting library (QuestPDF or similar) being wired -- TC-PRF-008-14 asserts the export SEAM + report data model (objectives/checkpoints/outcomes/signatures) + branding + authz/tenant-scoping; full visual fidelity is the extension point (consistent with US-PRF-005 FR-7 / US-PRF-006 FR-6 / US-PRF-007 FR-8). (5) AC-2/AC-3/AC-5/BR-4/FR-3 notification + reminder + ack-timeout DELIVERY (in-app + email) CONDITIONAL on the Notification System (S25) -- in-app push + email enqueue asserted, delivery conditional (TC-PRF-008-01/-02/-04/-08/-15, TC-PRF-ISO-032). (6) Any PIP list/aggregate cache (S10) is asserted tenant-scoped, CONDITIONAL on a cache layer -- if computed on demand it asserts tenant-filtered queries with no shared/global key (TC-PRF-ISO-032). (7) NFR-1 <=800ms P95 requires a seeded performance environment (TC-PRF-008-12). (8) NFR-3 7-year retention is asserted at the retention seam (TC-PRF-008-10); the retention purge mechanism is platform-owned. (9) The BR-6 escalation OPTION SET (reassignment/demotion/non-renewal/termination-recommendation) is tenant-configurable; TC-PRF-008-04 exercises the configured options and asserts each records identically -- the exact per-tenant option list is a config seam, not a gap.

> US-PRF-009 (Goal Tracking with Progress Updates) is the NINTH Performance story -- the continuous, employee-driven progress-tracking layer that sits between goal-setting (US-PRF-001) and the formal review (US-PRF-002/003), with an append-only update history, a manager team-progress view, and a Hangfire stale-goal nudge. It adds 19 test cases: 15 functional/security/perf/a11y (TC-PRF-009-01..15) + 4 dedicated multi-tenant isolation on the new `goal_progress_updates` (append-only) + `goal_comments` tables + stale-detection Hangfire job + attachments + caches + notifications (TC-PRF-ISO-033..036, continuing the running ISO counter from 032). All 5 acceptance criteria of US-PRF-009 are covered.
>
> KEY notes: happy path (TC-PRF-009-01) employee opens My Goals -> cards with title/target/current progress %/status/last-update/animated bar (AC-1) -> "Add Update" form (progress slider/status/rich-text notes/attachment) -> Save -> update created tenant-scoped, server-timestamped, append-only logged, manager notified (FR-5), progress bar + last-update refresh (AC-2, FR-1/2/5); update history (TC-PRF-009-02) multiple updates -> expand card -> chronological timeline with date/progress-change/notes/attachments (AC-3, FR-3); manager team progress (TC-PRF-009-03) Team Goals summary table per DIRECT REPORT (overall completion %/# at-risk/last update) + drill-down to employee goals/updates, scope = direct reports only, non-report excluded incl. by id (AC-4); stale-goal nudge (TC-PRF-009-04) Hangfire detects goals with no update > X days (default 14) -> nudge "You haven't updated progress on [Goal] in [X] days" + "Needs Attention" flag on the manager dashboard, completed/within-interval goals skipped, idempotent, interval 0 disables (AC-5/FR-6/BR-4); status rules (TC-PRF-009-05) 100% auto-sets Completed but employee can override (BR-2) + transitions NotStarted->InProgress->Completed/AtRisk/Blocked, each appended (FR-7); Blocked (TC-PRF-009-06) notifies manager + HR, non-Blocked does not over-notify HR (BR-3); weighted overall completion (TC-PRF-009-07) 3 goals (50/30/20% weights x 80/50/10% progress) -> 57.0% weighted (NOT 46.7% mean), server-authoritative, consistent with the team table (FR-4); append-only (TC-PRF-009-08) PUT/PATCH/DELETE on an update rejected for employee AND HR, correction = new appended entry, original byte-for-byte unchanged (NFR-3); comment thread (TC-PRF-009-09) manager comments on an update -> conversation displays in order, <=500 chars (501 rejected), employee replies, participant notified (FR-8); visibility (TC-PRF-009-10) updates visible to employee/manager/HR, peer blocked incl. by id UNLESS the tenant enables shared visibility, toggle tenant-scoped (BR-5); validation/sanitization (TC-PRF-009-11) progress 0-100, status enum, notes <=2000, <=3 files/<=10MB, XSS/SQLi on notes+comments neutralized, server-side enforced (FR-2/S10); boundary (TC-PRF-009-12) progress 0/100, notes exactly 2000, exactly 3 files/exactly 10MB, BR-1 update only during the active cycle window (closed -> blocked); authz (TC-PRF-009-13) employee posts/views only OWN goals (Performance.Read.Self), cross-employee IDOR blocked, manager limited to direct reports, unauth 401 (NFR-2); performance (TC-PRF-009-14) goal list <=400ms P95 (<=10 goals, no N+1) + stale job <=60s @ 5,000 employees (NFR-1/NFR-5); accessibility (TC-PRF-009-15) add-update at 360px bottom-sheet + WCAG 2.1 AA (keyboard slider, SR status not color-only, progressbar aria + reduced-motion) (NFR-4).
>
> Tenant isolation (NFR-2) for US-PRF-009: TC-PRF-ISO-033 cross-tenant read of goal_progress_updates/goal_comments/attachments/aggregates/stale-flags (Tenant B sees zero of Tenant A's, incl. by direct id), ISO-034 missing/invalid/mismatched tenant-context rejection + cross-tenant IDOR (view/add-update/comment/drill-down), ISO-035 cross-tenant write block + server-derived tenant_id (no body injection) + foreign goal_id/employee_id rejected, ISO-036 tenant-scoped stale-detection Hangfire job + nudge/update/Blocked notifications + attachment storage + goal-list/summary caches.
>
> CONDITIONAL/DEFERRED for US-PRF-009 (written as conditional, not gaps): (1) same NFR-2 RLS caveat -- S7/NFR-2 name PostgreSQL RLS on goal_progress_updates; isolation is enforced via EF Core global query filters + TenantInterceptor with RLS noted as an extension point (ISO-033/035). (2) DEPENDS ON US-PRF-001 (assigned+acknowledged goals) and US-PRF-004 (active cycle + tracking window dates, BR-1) -- goals/cycle/window assumed seeded; the window-state branches are asserted against those dates. (3) FR-5/FR-6/BR-3 notification DELIVERY (update notice to manager, Blocked to manager+HR, stale nudge; in-app + email -- SignalR real-time if available else polling) CONDITIONAL on the Notification System (S25) -- the in-app push + email enqueue are asserted, delivery conditional (TC-PRF-009-01/-04/-06/-09, TC-PRF-ISO-036). (4) Any goal-list / overall-completion / team-summary cache (S10) is asserted tenant-scoped, CONDITIONAL on a cache layer -- if computed on demand it asserts tenant-filtered queries with no shared/global key (TC-PRF-ISO-036). (5) The stale-detection Hangfire daily job (default 14d, tenant-configurable, 0 disables, BR-4) is asserted at the job seam (TC-PRF-009-04, TC-PRF-ISO-036); the recurring schedule is documented as the extension point if not yet wired. (6) File attachments (<=3 files, <=10MB) use the platform file-management module with tenant-scoped storage -- virus scanning, if not wired, is documented as a seam (consistent with US-PRF-002 FR-5). (7) NFR-1 <=400ms P95 + NFR-5 <=60s @ 5,000 employees require a seeded performance environment (TC-PRF-009-14). (8) The FR-7 status state machine + the BR-2 100%-auto-complete-overridable rule are asserted against whatever the implementation documents (TC-PRF-009-05) -- the exact transition set is an implementation contract, not a gap. (9) BR-5 shared goal visibility for peers is a tenant config toggle (TC-PRF-009-10); the default-off restriction (employee/manager/HR) holds regardless.

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 9 (US-PRF-001, US-PRF-002, US-PRF-003, US-PRF-004, US-PRF-005, US-PRF-006, US-PRF-007, US-PRF-008, US-PRF-009) |
| Total Test Cases | 164 (128 functional/security/perf/a11y + 36 dedicated multi-tenant isolation) |
| US-PRF-001 Test Cases | 16 (TC-PRF-001-01..12 + TC-PRF-ISO-001..004) |
| US-PRF-002 Test Cases | 19 (TC-PRF-002-01..15 + TC-PRF-ISO-005..008) |
| US-PRF-003 Test Cases | 17 (TC-PRF-003-01..13 + TC-PRF-ISO-009..012) |
| US-PRF-004 Test Cases | 19 (TC-PRF-004-01..15 + TC-PRF-ISO-013..016) |
| US-PRF-005 Test Cases | 18 (TC-PRF-005-01..14 + TC-PRF-ISO-017..020) |
| US-PRF-006 Test Cases | 18 (TC-PRF-006-01..14 + TC-PRF-ISO-021..024) |
| US-PRF-007 Test Cases | 19 (TC-PRF-007-01..15 + TC-PRF-ISO-025..028) |
| US-PRF-008 Test Cases | 19 (TC-PRF-008-01..15 + TC-PRF-ISO-029..032) |
| US-PRF-009 Test Cases | 19 (TC-PRF-009-01..15 + TC-PRF-ISO-033..036) |
| Critical Priority | 56 (US-PRF-001: -01/-02/-07/-08 + ISO-001/-002/-003; US-PRF-002: -01/-07/-09 + ISO-005/-006/-007; US-PRF-003: -01/-02/-07/-09 + ISO-009/-010/-011; US-PRF-004: -01/-02/-04 + ISO-013/-014/-015; US-PRF-005: -01/-05/-06 + ISO-017/-018/-019; US-PRF-006: -01/-07/-11 + ISO-021/-022/-023; US-PRF-007: -01/-06/-08 + ISO-025/-026/-027; US-PRF-008: -01/-07/-09/-10 + ISO-029/-030/-031; US-PRF-009: -01/-08/-10/-13 + ISO-033/-034/-035) |
| High Priority | 108 (remaining functional/perf/a11y of all nine stories + TC-PRF-ISO-004, -008, -012, -016, -020, -024, -028, -032, -036) |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Acceptance Criteria Coverage | US-PRF-001 5/5; US-PRF-002 5/5; US-PRF-003 5/5; US-PRF-004 5/5; US-PRF-005 5/5; US-PRF-006 4/4 (US-PRF-006 has only AC-1..AC-4); US-PRF-007 5/5; US-PRF-008 5/5; US-PRF-009 5/5 |
| Status | All Draft |

> Note: Critical-priority IDs total 56 across the nine stories (US-PRF-001: -01/-02/-07/-08 + ISO-001/-002/-003 = 7; US-PRF-002: -01/-07/-09 + ISO-005/-006/-007 = 6; US-PRF-003: -01/-02/-07/-09 + ISO-009/-010/-011 = 7; US-PRF-004: -01/-02/-04 + ISO-013/-014/-015 = 6; US-PRF-005: -01/-05/-06 + ISO-017/-018/-019 = 6; US-PRF-006: -01/-07/-11 + ISO-021/-022/-023 = 6; US-PRF-007: -01/-06/-08 + ISO-025/-026/-027 = 6; US-PRF-008: -01/-07/-09/-10 + ISO-029/-030/-031 = 7; US-PRF-009: -01/-08/-10/-13 + ISO-033/-034/-035 = 7); High totals 108; summing to 164.

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-PRF-001 | Manager Sets Goals/KPIs for Team Members | TC-PRF-001-01, TC-PRF-001-02, TC-PRF-001-03, TC-PRF-001-04, TC-PRF-001-05, TC-PRF-001-06, TC-PRF-001-07, TC-PRF-001-08, TC-PRF-001-09, TC-PRF-001-10, TC-PRF-001-11, TC-PRF-001-12 | 12 |
| Cross-cutting (PRF-001) | Multi-tenant isolation (goals table + caches + notifications) | TC-PRF-ISO-001, TC-PRF-ISO-002, TC-PRF-ISO-003, TC-PRF-ISO-004 | 4 |
| US-PRF-002 | Employee Self-Rates Against Goals | TC-PRF-002-01, TC-PRF-002-02, TC-PRF-002-03, TC-PRF-002-04, TC-PRF-002-05, TC-PRF-002-06, TC-PRF-002-07, TC-PRF-002-08, TC-PRF-002-09, TC-PRF-002-10, TC-PRF-002-11, TC-PRF-002-12, TC-PRF-002-13, TC-PRF-002-14, TC-PRF-002-15 | 15 |
| Cross-cutting (PRF-002) | Multi-tenant isolation (self_assessment table + attachments + auto-save + notifications) | TC-PRF-ISO-005, TC-PRF-ISO-006, TC-PRF-ISO-007, TC-PRF-ISO-008 | 4 |
| US-PRF-003 | Manager Rates Employee Performance | TC-PRF-003-01, TC-PRF-003-02, TC-PRF-003-03, TC-PRF-003-04, TC-PRF-003-05, TC-PRF-003-06, TC-PRF-003-07, TC-PRF-003-08, TC-PRF-003-09, TC-PRF-003-10, TC-PRF-003-11, TC-PRF-003-12, TC-PRF-003-13 | 13 |
| Cross-cutting (PRF-003) | Multi-tenant isolation (review table + dashboard caches + notifications + audit) | TC-PRF-ISO-009, TC-PRF-ISO-010, TC-PRF-ISO-011, TC-PRF-ISO-012 | 4 |
| US-PRF-004 | HR Creates and Manages Appraisal Cycles | TC-PRF-004-01, TC-PRF-004-02, TC-PRF-004-03, TC-PRF-004-04, TC-PRF-004-05, TC-PRF-004-06, TC-PRF-004-07, TC-PRF-004-08, TC-PRF-004-09, TC-PRF-004-10, TC-PRF-004-11, TC-PRF-004-12, TC-PRF-004-13, TC-PRF-004-14, TC-PRF-004-15 | 15 |
| Cross-cutting (PRF-004) | Multi-tenant isolation (cycles/phases/participants tables + Hangfire jobs + dashboard caches + notifications) | TC-PRF-ISO-013, TC-PRF-ISO-014, TC-PRF-ISO-015, TC-PRF-ISO-016 | 4 |
| US-PRF-005 | 360-Degree Review (Peers, Reports, Manager, Self) | TC-PRF-005-01, TC-PRF-005-02, TC-PRF-005-03, TC-PRF-005-04, TC-PRF-005-05, TC-PRF-005-06, TC-PRF-005-07, TC-PRF-005-08, TC-PRF-005-09, TC-PRF-005-10, TC-PRF-005-11, TC-PRF-005-12, TC-PRF-005-13, TC-PRF-005-14 | 14 |
| Cross-cutting (PRF-005) | Multi-tenant isolation (feedback_360 table + reminder jobs + results caches + notifications) | TC-PRF-ISO-017, TC-PRF-ISO-018, TC-PRF-ISO-019, TC-PRF-ISO-020 | 4 |
| US-PRF-006 | Performance Review Meeting Notes and Sign-Off | TC-PRF-006-01, TC-PRF-006-02, TC-PRF-006-03, TC-PRF-006-04, TC-PRF-006-05, TC-PRF-006-06, TC-PRF-006-07, TC-PRF-006-08, TC-PRF-006-09, TC-PRF-006-10, TC-PRF-006-11, TC-PRF-006-12, TC-PRF-006-13, TC-PRF-006-14 | 14 |
| Cross-cutting (PRF-006) | Multi-tenant isolation (review_meeting_notes + review_signoffs tables + auto-close jobs + notifications + audit + PDF export) | TC-PRF-ISO-021, TC-PRF-ISO-022, TC-PRF-ISO-023, TC-PRF-ISO-024 | 4 |
| US-PRF-007 | Performance Dashboard and Analytics | TC-PRF-007-01, TC-PRF-007-02, TC-PRF-007-03, TC-PRF-007-04, TC-PRF-007-05, TC-PRF-007-06, TC-PRF-007-07, TC-PRF-007-08, TC-PRF-007-09, TC-PRF-007-10, TC-PRF-007-11, TC-PRF-007-12, TC-PRF-007-13, TC-PRF-007-14, TC-PRF-007-15 | 15 |
| Cross-cutting (PRF-007) | Multi-tenant isolation (performance_summary materialized view + aggregate caches + export artifacts + Hangfire refresh jobs) | TC-PRF-ISO-025, TC-PRF-ISO-026, TC-PRF-ISO-027, TC-PRF-ISO-028 | 4 |
| US-PRF-008 | Performance Improvement Plan (PIP) | TC-PRF-008-01, TC-PRF-008-02, TC-PRF-008-03, TC-PRF-008-04, TC-PRF-008-05, TC-PRF-008-06, TC-PRF-008-07, TC-PRF-008-08, TC-PRF-008-09, TC-PRF-008-10, TC-PRF-008-11, TC-PRF-008-12, TC-PRF-008-13, TC-PRF-008-14, TC-PRF-008-15 | 15 |
| Cross-cutting (PRF-008) | Multi-tenant isolation (pip/pip_objectives/pip_checkpoints tables + Hangfire reminder/ack-timeout jobs + checkpoint attachments + escalation/audit + report artifacts) | TC-PRF-ISO-029, TC-PRF-ISO-030, TC-PRF-ISO-031, TC-PRF-ISO-032 | 4 |
| US-PRF-009 | Goal Tracking with Progress Updates | TC-PRF-009-01, TC-PRF-009-02, TC-PRF-009-03, TC-PRF-009-04, TC-PRF-009-05, TC-PRF-009-06, TC-PRF-009-07, TC-PRF-009-08, TC-PRF-009-09, TC-PRF-009-10, TC-PRF-009-11, TC-PRF-009-12, TC-PRF-009-13, TC-PRF-009-14, TC-PRF-009-15 | 15 |
| Cross-cutting (PRF-009) | Multi-tenant isolation (goal_progress_updates + goal_comments tables + stale-detection Hangfire job + attachments + caches + notifications) | TC-PRF-ISO-033, TC-PRF-ISO-034, TC-PRF-ISO-035, TC-PRF-ISO-036 | 4 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-001)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Open-window goal-setting form with all required fields | TC-PRF-001-01, TC-PRF-001-12 |
| AC-2 | Save valid goals (100%) -> persisted tenant-scoped + linked + employee notified | TC-PRF-001-01 |
| AC-3 | Weights not summing to 100% -> "Goal weights must total 100%", submission prevented | TC-PRF-001-02 |
| AC-4 | Team goals dashboard with status (draft/submitted/acknowledged) + progress | TC-PRF-001-06, TC-PRF-001-11, TC-PRF-001-12 |
| AC-5 | Closed goal-setting window -> read-only + closed message, no modification | TC-PRF-001-08 |

## Requirement -> Test Case Coverage (US-PRF-001) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 create/edit/delete during window | TC-PRF-001-01, -06, -07, -08, -10 |
| FR-2 goal fields + max lengths + enum | TC-PRF-001-05, -09 |
| FR-3 weights sum to exactly 100% | TC-PRF-001-01, -02 |
| FR-4 goal cascading | DEFERRED (later Performance story) |
| FR-5 clone / template library | DEFERRED (later Performance story) |
| FR-6 audit logging | DEFERRED (Audit module S24) |
| FR-7 notify employee on assign/modify | TC-PRF-001-01, TC-PRF-ISO-004 |
| BR-1 goals only during goal-setting phase | TC-PRF-001-08 |
| BR-2 min 1 / max 10 goals per employee per cycle | TC-PRF-001-03, -05 |
| BR-3 weights in 5% increments | TC-PRF-001-04, -05 |
| BR-4 only direct manager or HR `.All` | TC-PRF-001-07 |
| BR-5 acknowledged goals need HR approval to modify | DEFERRED (later Performance story) |
| NFR-1 50-member list <=400ms P95 | TC-PRF-001-11 |
| NFR-2 tenant isolation (RLS / EF query filters) | TC-PRF-ISO-001, -002, -003, -004 |
| NFR-3 responsive 360px-4K + WCAG 2.1 AA | TC-PRF-001-12 |
| NFR-4 optimistic concurrency control | TC-PRF-001-10 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-002)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Open-window My Review form: all goals + self-rating/achievement/comment inputs | TC-PRF-002-01, TC-PRF-002-14, TC-PRF-002-15 |
| AC-2 | Rate all goals -> Submit -> "Self-Assessment Submitted" + manager notified + edits prevented | TC-PRF-002-01, TC-PRF-002-02, TC-PRF-002-13 |
| AC-3 | Save as Draft -> partial progress persisted, resume later | TC-PRF-002-05, TC-PRF-002-02, TC-PRF-002-15 |
| AC-4 | Closed window -> read-only + "The self-assessment period for this cycle has ended" | TC-PRF-002-07 |
| AC-5 | Deadline approaching -> Hangfire reminder (in-app + email) to non-submitters | TC-PRF-002-08 |

## Requirement -> Test Case Coverage (US-PRF-002) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 display each goal (title/desc/weight/target/due) + self-rating inputs | TC-PRF-002-01, -07 |
| FR-2 self-rating uses tenant-configured scale | TC-PRF-002-03, -04 |
| FR-3 self-assessment comment min 20 chars per goal | TC-PRF-002-03, -04, -01 |
| FR-4 weighted self-assessment score from ratings + weights | TC-PRF-002-01, -12 |
| FR-5 file attachments per goal: max 5 files, 10MB each | TC-PRF-002-10, TC-PRF-ISO-008 |
| FR-6 save-as-draft persistence | TC-PRF-002-05, -06, -02 |
| FR-7 Hangfire reminder for non-submitters | TC-PRF-002-08, TC-PRF-ISO-008 |
| BR-1 submit only during the self-assessment phase window | TC-PRF-002-07 |
| BR-2 all goals must be rated before submission; partial = draft only | TC-PRF-002-02 |
| BR-3 submitted assessment locked unless manager/HR reopens | TC-PRF-002-13, TC-PRF-002-01 |
| BR-4 self:manager weight ratio applied at final score (not in raw self-score) | TC-PRF-002-12 |
| BR-5 self-assessment optional when disabled in tenant config | PRECONDITION (enabled assumed; out of US-PRF-002 set) |
| NFR-1 form loads <=400ms P95 incl. all goal data | TC-PRF-002-14 |
| NFR-2 tenant isolation (own-only + RLS / EF query filters) | TC-PRF-002-09, TC-PRF-ISO-005, -006, -007, -008 |
| NFR-3 draft auto-save every 60s | TC-PRF-002-06, TC-PRF-ISO-008 |
| NFR-4 file virus-scan + tenant-scoped storage path | TC-PRF-002-11, TC-PRF-ISO-008 |
| NFR-5 responsive 360px + touch + keyboard for rating inputs | TC-PRF-002-15 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-003)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Side-by-side view: each goal + employee self-rating/comments alongside empty manager fields | TC-PRF-003-01, TC-PRF-003-12, TC-PRF-003-13 |
| AC-2 | Rate all goals -> Submit -> manager score + final score, status "Manager Review Submitted", employee notified | TC-PRF-003-01, TC-PRF-003-03, TC-PRF-003-04 |
| AC-3 | Submit without rating all goals -> validation error LISTING unrated goals, prevented | TC-PRF-003-02 |
| AC-4 | Team Reviews dashboard: per-member status (pending self / submitted / manager pending / completed) color-coded | TC-PRF-003-06 |
| AC-5 | Submitted review read-only; editable only if HR reopens | TC-PRF-003-08, TC-PRF-003-09 |

## Requirement -> Test Case Coverage (US-PRF-003) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 display self-rating/comments alongside each goal for manager reference | TC-PRF-003-01, -06, -09 |
| FR-2 manager rating uses the same tenant-configured scale | TC-PRF-003-03, -05 |
| FR-3 manager comment min 20 chars per goal | TC-PRF-003-01, -03, -05 |
| FR-4 final weighted score via tenant self:manager ratio | TC-PRF-003-01, -04 |
| FR-5 overall summary comment (max 5000 chars) | TC-PRF-003-01, -03, -05 |
| FR-6 flag for recognition / promotion / PIP | DEFERRED (lightweight flag; no dedicated TC) |
| FR-7 rating actions audit-logged with user id + timestamp | TC-PRF-003-11, TC-PRF-ISO-012 |
| BR-1 submit only during the manager-review phase window | TC-PRF-003-09 |
| BR-2 manager can only rate direct reports | TC-PRF-003-06, -07 |
| BR-3 HR `Performance.Review.All` rates anyone + reopens submitted reviews | TC-PRF-003-08, -09 |
| BR-4 final = `(self*self_w)+(manager*manager_w)` | TC-PRF-003-04 |
| BR-5 360-degree peer/report ratings folded into final score | DEFERRED (US-PRF-005) |
| NFR-1 single-employee review form (incl. self-assessment data) <=400ms P95 | TC-PRF-003-12 |
| NFR-2 tenant isolation (own-tenant + direct-report scope; RLS / EF query filters) | TC-PRF-003-07, TC-PRF-ISO-009, -010, -011, -012 |
| NFR-3 optimistic concurrency (HR + manager simultaneous edit) | TC-PRF-003-10 |
| NFR-4 WCAG 2.1 AA + keyboard navigation for rating inputs + 360px stacked | TC-PRF-003-13 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-004)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Create-cycle form with name/period/phases/scope/rating-scale/360 fields | TC-PRF-004-01, TC-PRF-004-14, TC-PRF-004-15 |
| AC-2 | Valid cycle created -> phases+participants persisted tenant-scoped, Hangfire jobs scheduled, confirmation | TC-PRF-004-01, TC-PRF-004-02, TC-PRF-004-08 |
| AC-3 | Cycle dashboard: timeline + per-phase completion stats + overdue counts | TC-PRF-004-05, TC-PRF-004-13, TC-PRF-004-14 |
| AC-4 | Deadline approaching -> Hangfire reminder (in-app + email) to non-completers | TC-PRF-004-07 |
| AC-5 | Edit/extend a phase -> re-validate sequencing/non-overlap, reschedule jobs, notify affected | TC-PRF-004-06, TC-PRF-004-02, TC-PRF-004-03 |

## Requirement -> Test Case Coverage (US-PRF-004) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 cycle with min 3 phases (goal-setting, assessment, publish) | TC-PRF-004-01, -15 |
| FR-2 phases sequential + non-overlapping, configurable dates | TC-PRF-004-01, -02, -06, -15 |
| FR-3 scope to all / departments / grades / custom list | TC-PRF-004-01, -11 |
| FR-4 multiple concurrent cycles (not same-type for one employee) | TC-PRF-004-11 |
| FR-5 Hangfire phase-start/reminder/close/escalation jobs | TC-PRF-004-01, -06, -07, TC-PRF-ISO-016 |
| FR-6 rating scale + weight ratio + 360 + calibration + anonymity config | TC-PRF-004-01, -10, -12 |
| FR-7 statuses Draft/Active/Paused/Completed/Cancelled | TC-PRF-004-08, -09, -10 |
| FR-8 clone an existing cycle as a template | TC-PRF-004-12 |
| BR-1 only Performance.SetGoal.All / .Publish.All create/modify cycles | TC-PRF-004-04, -08, -12 |
| BR-2 cannot delete with submitted reviews (cancel only) | TC-PRF-004-09 |
| BR-3 phase dates within the cycle window | TC-PRF-004-03, -06, -15 |
| BR-4 no employee in two active cycles of the same type | TC-PRF-004-11 |
| BR-5 rating scale locks on Draft->Active | TC-PRF-004-10 |
| BR-6 cancellation requires a reason + notifies all participants | TC-PRF-004-09 |
| NFR-1 cycle creation with 5,000 participants <=5s | TC-PRF-004-13 |
| NFR-2 tenant isolation (RLS / EF query filters) | TC-PRF-ISO-013, -014, -015, -016 |
| NFR-3 Hangfire jobs tenant-scoped + retry/backoff (Polly) | TC-PRF-004-07, TC-PRF-ISO-016 |
| NFR-4 dashboard loads <=2s P95 incl. aggregate stats | TC-PRF-004-05, -13 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-005)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | 360 config shows auto-suggested peers/reports + auto-assigned manager/self + manual add/remove | TC-PRF-005-01, TC-PRF-005-02, TC-PRF-005-05 |
| AC-2 | Assigned reviewers notified (in-app + email) with a link to the competency-based feedback form | TC-PRF-005-01, TC-PRF-005-10, TC-PRF-005-12 |
| AC-3 | Reviewer submits -> saved + status "Completed" + tracker updated; identity hidden if anonymity on | TC-PRF-005-01, TC-PRF-005-03, TC-PRF-005-06 |
| AC-4 | Aggregated report: per-competency averages + self/manager/peer/report radar + anonymized comments | TC-PRF-005-01, TC-PRF-005-08, TC-PRF-005-14 |
| AC-5 | Deadline approaching -> Hangfire reminder to non-submitters with a direct link | TC-PRF-005-09 |

## Requirement -> Test Case Coverage (US-PRF-005) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 four reviewer categories (Self/Manager/Peer/Report) | TC-PRF-005-01, -02, -10 |
| FR-2 nominate peers/reports; self+manager auto-assigned | TC-PRF-005-01, -02 |
| FR-3 configurable minimum reviewers per category | TC-PRF-005-04, -14 |
| FR-4 competency-based form with tenant rating scale + optional comments | TC-PRF-005-01, -10, -12 |
| FR-5 anonymous feedback mode (identity not revealed in results) | TC-PRF-005-06, -07, -13 |
| FR-6 weighted composite score from configurable per-category weights | TC-PRF-005-01, -08 |
| FR-7 360 summary report exportable as PDF | TC-PRF-005-13 |
| FR-8 Hangfire reviewer reminders at configurable intervals | TC-PRF-005-09, TC-PRF-ISO-020 |
| BR-1 360 only when the cycle toggle is enabled | TC-PRF-005-01 (precondition) |
| BR-2 employee cannot review themselves as a Peer | TC-PRF-005-02 |
| BR-3 one feedback per reviewer per employee per cycle | TC-PRF-005-03 |
| BR-4 minimum peer reviewers met before results released (else warn) | TC-PRF-005-04, -14 |
| BR-5 anonymity cannot be retroactively disabled after submission | TC-PRF-005-06, -07 |
| BR-6 360 composite incorporated into the final performance score | TC-PRF-005-01, -08 |
| NFR-1 feedback form loads <=400ms P95 | TC-PRF-005-11 |
| NFR-2 tenant isolation (RLS / EF query filters) | TC-PRF-005-05, TC-PRF-ISO-017, -018, -019, -020 |
| NFR-3 anonymity enforced at DB/API level (no reviewer ids in payload) | TC-PRF-005-06 |
| NFR-4 results + radar render <=2s for up to 20 reviewers | TC-PRF-005-11, -14 |
| NFR-5 feedback form mobile-responsive (any device) | TC-PRF-005-12 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-006)

> NOTE: US-PRF-006 has FOUR acceptance criteria (AC-1..AC-4), not five.

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | "Add Meeting Notes" -> templated rich-text editor with the four sections | TC-PRF-006-01, TC-PRF-006-10, TC-PRF-006-13, TC-PRF-006-14 |
| AC-2 | "Request Employee Sign-Off" -> notes saved, status "Pending Employee Sign-Off", employee notified | TC-PRF-006-01, TC-PRF-006-02, TC-PRF-006-11 |
| AC-3 | Employee Acknowledge & Sign (-> "Signed Off", locked) OR Dispute (-> comments captured, manager+HR notified) | TC-PRF-006-01, TC-PRF-006-03, TC-PRF-006-04, TC-PRF-006-06, TC-PRF-006-07, TC-PRF-006-12 |
| AC-4 | Full signed record (goals/ratings/notes/timestamps/signatures) viewable + PDF export | TC-PRF-006-01, TC-PRF-006-07, TC-PRF-006-09 |

## Requirement -> Test Case Coverage (US-PRF-006) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 rich-text editor with configurable tenant template | TC-PRF-006-01, -10 |
| FR-2 sections: strengths / dev areas / agreed actions+deadlines / summary | TC-PRF-006-01, -10 |
| FR-3 digital sign-off workflow: manager first, then employee | TC-PRF-006-01, -02, -11 |
| FR-4 employee Acknowledge & Sign or Dispute (mandatory comments) | TC-PRF-006-03, -04 |
| FR-5 disputed reviews escalated to HR with comments for resolution | TC-PRF-006-04, -12 |
| FR-6 PDF of complete review with tenant branding | TC-PRF-006-09 |
| FR-7 sign-off actions immutably audit-logged (user id + timestamp + IP) | TC-PRF-006-08, -01, TC-PRF-ISO-024 |
| BR-1 meeting notes only after manager review submitted | TC-PRF-006-02, -11 |
| BR-2 employee must review notes before signing; opened/read tracked | TC-PRF-006-06, -01 |
| BR-3 no sign-off within window -> auto-close "No Response" + notify HR | TC-PRF-006-05, TC-PRF-ISO-024 |
| BR-4 disputed remains "Disputed" until HR amends or confirms | TC-PRF-006-04, -12 |
| BR-5 locked after both sign off; only system-admin compliance correction | TC-PRF-006-01, -07 |
| NFR-1 meeting-notes editor loads <=400ms P95 | TC-PRF-006-13 |
| NFR-2 tenant isolation (RLS / EF query filters) | TC-PRF-ISO-021, -022, -023, -024 |
| NFR-3 sign-off records immutable; no user incl. HR can modify a signature | TC-PRF-006-07, -08 |
| NFR-4 PDF export completes <=3s for a single review | TC-PRF-006-09 |
| NFR-5 mobile-accessible sign-off + touch-friendly confirmation dialogs | TC-PRF-006-14 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-007)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Dashboard overview: completion rate + average score + distribution histogram + department bar + top/bottom performers + cycle progress | TC-PRF-007-01, TC-PRF-007-10, TC-PRF-007-11, TC-PRF-007-12 |
| AC-2 | Filters (department/grade/cycle/location/employment type) update all widgets to the filtered population | TC-PRF-007-02, TC-PRF-007-09, TC-PRF-007-14 |
| AC-3 | Trend view: select multiple cycles -> line chart of average scores + department overlay | TC-PRF-007-03 |
| AC-4 | Export (CSV/Excel/PDF) within 5s incl. visible charts + data tables + tenant branding | TC-PRF-007-05 |
| AC-5 | Manager dashboard scoped to direct reports (team-level view), `Performance.Read.Team` enforced | TC-PRF-007-06, TC-PRF-007-07, TC-PRF-007-08 |

## Requirement -> Test Case Coverage (US-PRF-007) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 score distribution histogram (chart.js) | TC-PRF-007-01, -09, -12 |
| FR-2 department-wise average bar chart | TC-PRF-007-01, -02, -04 |
| FR-3 top N / bottom N performers (configurable, default 10) + trend | TC-PRF-007-01, -10 |
| FR-4 filters: cycle, department, grade/band, employment type, location | TC-PRF-007-02, -09, -14, -15 |
| FR-5 drill-down: department bar -> employee list with individual scores | TC-PRF-007-04 |
| FR-6 cycle progress metrics (participants / goal-setting / self / manager / signed-off) | TC-PRF-007-01 |
| FR-7 multi-cycle trend line chart | TC-PRF-007-03 |
| FR-8 export CSV / Excel (XLSX) / PDF with tenant branding on PDF | TC-PRF-007-05, -15 |
| BR-1 permission scope: HR=all, manager=team (redirect employee to own review) | TC-PRF-007-06, -07, -08 |
| BR-2 distribution excludes probation-cycle employees unless explicitly included | TC-PRF-007-09 |
| BR-3 top/bottom only for `.All`; managers see a team ranking | TC-PRF-007-06, -08, -10 |
| BR-4 refresh from materialized views; tenant-configurable interval (default 4h via Hangfire) | TC-PRF-007-13, TC-PRF-ISO-028 |
| NFR-1 dashboard loads <=2.5s P95 @ 5,000 employees | TC-PRF-007-11 |
| NFR-2 tenant isolation via RLS (EF query filters as the platform mechanism) | TC-PRF-007-15, TC-PRF-ISO-025, -026, -027, -028 |
| NFR-3 aggregates via materialized views / Redis caching | TC-PRF-007-11, -13, TC-PRF-ISO-027, -028 |
| NFR-4 chart.js responsive sizing for all viewports + WCAG 2.1 AA | TC-PRF-007-12 |
| NFR-5 export generation for up to 5,000 employees <=5s | TC-PRF-007-05 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-008)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | "Create PIP" form: employee(pre-filled)/reason/duration/objectives(success criteria)/checkpoint dates/mentor/escalation | TC-PRF-008-01, TC-PRF-008-06, TC-PRF-008-13 |
| AC-2 | "Initiate PIP" -> PIP created, employee+manager+mentor notified (in-app+email), Hangfire checkpoint reminders scheduled | TC-PRF-008-01, TC-PRF-008-07, TC-PRF-008-15 |
| AC-3 | "Record Checkpoint" -> progress/evidence/status(OnTrack/AtRisk/NotMet)/comments/attachment; employee notified | TC-PRF-008-02, TC-PRF-008-04, TC-PRF-008-10 |
| AC-4 | PIP outcome review -> Successfully Completed / Extended (new end date) / Not Met (triggers escalation) | TC-PRF-008-03, TC-PRF-008-04, TC-PRF-008-07 |
| AC-5 | Outcome Not Met + HR confirms escalation -> decision recorded, stakeholders notified, immutable audit record | TC-PRF-008-04, TC-PRF-008-10 |

## Requirement -> Test Case Coverage (US-PRF-008) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 create PIP: reason + duration(30/60/90) + objectives w/ success criteria + checkpoints + mentor + escalation | TC-PRF-008-01, TC-PRF-008-06 |
| FR-2 status lifecycle Draft/Active/Extended/Successfully Completed/Not Met/Cancelled | TC-PRF-008-03, TC-PRF-008-04, TC-PRF-008-05 |
| FR-3 Hangfire jobs: start / checkpoint reminders (3 days prior) / end reminder / overdue alerts | TC-PRF-008-01, TC-PRF-008-15, TC-PRF-008-08, TC-PRF-ISO-032 |
| FR-4 record checkpoint: progress status + evidence notes + file attachments | TC-PRF-008-02, TC-PRF-008-04 |
| FR-5 complete immutable history of actions / status changes / checkpoint outcomes | TC-PRF-008-04, TC-PRF-008-10, TC-PRF-008-08 |
| FR-6 extension with new end date + additional objectives | TC-PRF-008-03, TC-PRF-008-15 |
| FR-7 PIP summary report (PDF): objectives/checkpoints/outcomes/signatures | TC-PRF-008-14 |
| FR-8 visibility restricted to employee/manager/HR/mentor | TC-PRF-008-09, TC-PRF-008-14 |
| BR-1 only HR `.All` create/extend/close; managers record checkpoints only | TC-PRF-008-07, TC-PRF-008-02 |
| BR-2 only one active PIP per employee at a time | TC-PRF-008-05 |
| BR-3 PIP duration minimum 30 days | TC-PRF-008-06 |
| BR-4 employee acknowledgement; non-ack within 5 business days -> "Not Acknowledged" flag (Hangfire) | TC-PRF-008-08, TC-PRF-ISO-032 |
| BR-5 PIP data excluded from general dashboards/reports (US-PRF-007) | TC-PRF-008-09 |
| BR-6 configurable escalation: reassignment / demotion / contract non-renewal / termination recommendation | TC-PRF-008-04 |
| NFR-1 PIP creation + checkpoint recording <=800ms P95 | TC-PRF-008-12 |
| NFR-2 tenant isolation via RLS (EF query filters as the platform mechanism) | TC-PRF-008-07, TC-PRF-008-09, TC-PRF-ISO-029, -030, -031, -032 |
| NFR-3 7-year retention of PIP records (retention seam) | TC-PRF-008-10 |
| NFR-4 sensitive fields (reason, escalation notes) encrypted at rest via pgcrypto | TC-PRF-008-11 |
| NFR-5 PIP UI mobile-accessible (checkpoint recording at 360px) + WCAG 2.1 AA | TC-PRF-008-13 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-009)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | My Goals: cards with title/target/current progress %/status/last-update/progress bar | TC-PRF-009-01, TC-PRF-009-15 |
| AC-2 | "Add Update" (progress %/status/notes/attachment) -> timestamped + logged + manager notified + progress bar updates | TC-PRF-009-01, TC-PRF-009-05, TC-PRF-009-12 |
| AC-3 | Multiple updates -> chronological timeline with date/progress change/notes/attachments | TC-PRF-009-02 |
| AC-4 | Team Goals summary table (overall completion %/# at-risk/last update) per direct report + drill-down | TC-PRF-009-03, TC-PRF-009-07, TC-PRF-009-09 |
| AC-5 | Stale goal (no update > X days) -> Hangfire nudge + "Needs Attention" flag on manager dashboard | TC-PRF-009-04 |

## Requirement -> Test Case Coverage (US-PRF-009) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 update goal progress anytime during the active cycle | TC-PRF-009-01, -12 |
| FR-2 update fields: progress 0-100 / status / notes <=2000 / <=3 files <=10MB | TC-PRF-009-01, -11, -12 |
| FR-3 full update history per goal as a timeline | TC-PRF-009-02, -08 |
| FR-4 overall completion = weighted average of goal progress | TC-PRF-009-07 |
| FR-5 manager notified (SignalR/polling) on a progress update | TC-PRF-009-01, -06 |
| FR-6 Hangfire daily stale-goal detection + nudge (default 14d) | TC-PRF-009-04, TC-PRF-ISO-036 |
| FR-7 status transitions NotStarted -> InProgress -> Completed / AtRisk / Blocked | TC-PRF-009-05, -11 |
| FR-8 manager comment thread per goal/update | TC-PRF-009-09 |
| BR-1 updates only during the active cycle window | TC-PRF-009-12 |
| BR-2 100% auto-sets Completed (employee can override) | TC-PRF-009-05 |
| BR-3 Blocked notifies manager + HR | TC-PRF-009-06 |
| BR-4 stale interval tenant-configurable (default 14d; 0 disables) | TC-PRF-009-04 |
| BR-5 updates visible to employee/manager/HR, not peers (unless shared visibility enabled) | TC-PRF-009-10, -13 |
| NFR-1 goal list <=400ms P95 (<=10 goals) | TC-PRF-009-14 |
| NFR-2 tenant isolation via RLS (EF query filters as the platform mechanism) | TC-PRF-009-13, TC-PRF-ISO-033, -034, -035, -036 |
| NFR-3 progress update history append-only (no edit/delete) | TC-PRF-009-08 |
| NFR-4 goal tracking UI mobile-optimized + WCAG 2.1 AA | TC-PRF-009-15 |
| NFR-5 stale-detection job processes 5,000-employee tenant <=60s | TC-PRF-009-14 |
