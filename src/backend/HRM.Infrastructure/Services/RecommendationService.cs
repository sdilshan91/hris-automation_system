using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Performance-based recommendation service (US-PRF-010). See <see cref="IRecommendationService"/> for the full
/// responsibility/scope/seam list. Every read/write is tenant-scoped via ITenantContext + the EF global query
/// filter (NFR-2). Enforces: HR-only (Publish.All) workspace/generate/save/submit/budget/rule + manager scope to
/// direct reports (AC-5); mandatory justification on override (FR-3); promotion needs grade+effective-date (BR-5);
/// submit gated by published final ratings (BR-1) + completed calibration if enabled (BR-2); the approval workflow
/// as an ordered approver chain (FR-4); budget soft-warning (FR-8/BR-4); and the BR-6 downstream-integration seam
/// raised on final approval. The immutable approval audit lives in append-only RecommendationEvent rows (FR-7).
/// </summary>
public sealed class RecommendationService : IRecommendationService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IHtmlSanitizer _sanitizer;
    private readonly IRecommendationIntegrationService _integration;
    private readonly IPayrollAuditLogger _auditLogger;
    private readonly ILogger<RecommendationService> _logger;

    private const int MaxPageSize = 200;

    // BUG-083: the compensation fields GetAsync decrypts + returns (P3-4 AES-at-rest columns). NAMES ONLY —
    // recorded in the sensitive-reveal audit's After payload so no monetary values reach the audit trail.
    /// <summary>
    /// GAP-012 / ISSUE-373: the export formats the workspace advertises. Kept beside the validator's accepted
    /// set on purpose — the validator takes csv/xlsx/pdf, but PDF rendering is deferred, so this is the subset
    /// that actually works today. Widen it when PDF ships.
    /// </summary>
    private static readonly string[] SupportedExportFormats = ["csv", "xlsx"];

    private static readonly string[] CompensationRevealFields =
        ["currentCompensation", "incrementAmount", "incrementPercent", "bonusAmount", "bonusPercent"];

    private readonly ISalaryAssignmentService? _salaryAssignment;

    public RecommendationService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IHtmlSanitizer sanitizer,
        IRecommendationIntegrationService integration,
        IPayrollAuditLogger auditLogger,
        ILogger<RecommendationService> logger,
        ISalaryAssignmentService? salaryAssignment = null)
    {
        // ISSUE-150: optional so the many existing test constructions keep compiling. When absent the
        // workspace degrades to the previous behaviour (null compensation) rather than throwing — a
        // missing payroll seam must not take down the whole recommendation screen.
        _salaryAssignment = salaryAssignment;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _sanitizer = sanitizer;
        _integration = integration;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    private bool IsHr => _currentUser.Permissions.Contains(PermissionCatalog.Performance.PublishAll);
    private bool IsManager => _currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewTeam);

    /// <summary>
    /// GAP-012 / ISSUE-373: may this caller see compensation figures? Scoped like <c>Payroll.ViewSensitive</c>
    /// — Owner / Admin / HR Manager, never HR Officer — because compiling recommendations and reading a named
    /// person's salary are different jobs.
    /// </summary>
    private bool CanSeeCompensation =>
        _currentUser.Permissions.Contains(PermissionCatalog.Payroll.ViewCompensation);

    // ════════════════════════════════════════════════════════════════
    //  Workspace (AC-1, NFR-4) + scope (AC-5/NFR-5)
    // ════════════════════════════════════════════════════════════════

    public async Task<Result<RecommendationWorkspaceDto>> GetWorkspaceAsync(
        RecommendationWorkspaceQueryInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecommendationWorkspaceDto>.Failure("Tenant context is not resolved.", 400);

        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope.IsFailure)
            return Result<RecommendationWorkspaceDto>.Failure(scope.Error!, scope.StatusCode ?? 403, scope.ErrorCode);

        var cycle = await ResolveCycleAsync(input.CycleId, cancellationToken);
        if (cycle is null)
            return Result<RecommendationWorkspaceDto>.Failure("No appraisal cycle is available for this tenant.", 404, "no_cycle");

        var page = input.Page < 1 ? 1 : input.Page;
        var pageSize = input.PageSize is < 1 or > MaxPageSize ? 25 : input.PageSize;

        // In-scope employees for the cycle (enrolled participants if any, else all tenant employees).
        var employeeIds = await ResolveCycleEmployeeIdsAsync(cycle.Id, scope.Value!, cancellationToken);
        var total = employeeIds.Count;

        var pagedIds = employeeIds.OrderBy(id => id).Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => pagedIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        var deptNames = await DepartmentNamesAsync(employees.Select(e => e.DepartmentId), cancellationToken);
        var titleNames = await JobTitleNamesAsync(employees.Select(e => e.JobTitleId), cancellationToken);
        var gradeByTitle = await GradeByTitleAsync(employees.Select(e => e.JobTitleId), cancellationToken);

        // ISSUE-150: the CURRENT side of FR-5's comparison, resolved LIVE from Payroll rather than read
        // back from a snapshot. TC-PRF-010-06 step 4 requires the current figure to be current, not a
        // value frozen when the recommendation was drafted. One batched query for the whole page — the
        // per-employee overload here would be a 200-query N+1 on a screen HR waits on.
        var currentCtc = _salaryAssignment is null
            ? new Dictionary<Guid, decimal>()
            : await _salaryAssignment.GetCurrentAnnualCtcAsync(pagedIds, cancellationToken);

        // The submitted manager reviews (final score + flag) for the cycle.
        var reviews = await _dbContext.ManagerReviews.AsNoTracking()
            .Where(r => r.CycleId == cycle.Id && pagedIds.Contains(r.EmployeeId))
            .ToListAsync(cancellationToken);
        var reviewByEmp = reviews.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        // Existing recommendations for the cycle.
        var recs = await LoadRecommendationsQuery()
            .Where(r => r.CycleId == cycle.Id && pagedIds.Contains(r.EmployeeId))
            .ToListAsync(cancellationToken);
        var recByEmp = recs.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        var nameLookup = await EmployeeNameLookupAsync(
            recs.SelectMany(ApproverEmployeeIds), cancellationToken);

        var now = DateTime.UtcNow;
        var rows = employees.Select(e =>
        {
            reviewByEmp.TryGetValue(e.Id, out var review);
            recByEmp.TryGetValue(e.Id, out var rec);
            return new RecommendationWorkspaceRowDto
            {
                EmployeeId = e.Id,
                EmployeeName = FullName(e),
                EmployeeNo = e.EmployeeNo,
                DepartmentId = e.DepartmentId,
                DepartmentName = deptNames.GetValueOrDefault(e.DepartmentId, string.Empty),
                CurrentGrade = gradeByTitle.GetValueOrDefault(e.JobTitleId),
                CurrentTitle = titleNames.GetValueOrDefault(e.JobTitleId),
                // ISSUE-150: live from Payroll. Absent = the employee has no active salary assignment,
                // which is a real state (unassigned staff) and stays null rather than rendering 0.
                CurrentCompensation = currentCtc.TryGetValue(e.Id, out var ctc) ? ctc : null,
                TenureMonths = TenureMonths(e.DateOfJoining, now),
                FinalScore = review?.FinalScore,
                ManagerFlag = review?.Flag ?? ReviewFlag.None,
                ManagerFlagName = (review?.Flag ?? ReviewFlag.None).ToString(),
                ManagerReviewId = review?.Id,
                // BUG-533: the workspace admits Performance.Publish.All | Performance.Review.Team, neither of
                // which implies Payroll.ViewCompensation. Mask the nested recommendation's comp figures here,
                // where the permission is already evaluated, so the row agrees with CompensationVisible below.
                Recommendation = rec is null ? null : BuildDto(rec, e, review, nameLookup, CanSeeCompensation),
            };
        }).ToList();

        var budget = await _dbContext.RecommendationBudgets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CycleId == cycle.Id, cancellationToken);

        return Result<RecommendationWorkspaceDto>.Success(new RecommendationWorkspaceDto
        {
            CycleId = cycle.Id,
            CycleName = cycle.Name,
            // GAP-012 / ISSUE-373: PDF is validated but its rendering is deferred, so it is deliberately NOT
            // advertised — offering a button that 500s is worse than not offering it.
            AvailableExportFormats = SupportedExportFormats,
            // GAP-012 / ISSUE-373: a real permission check, telling the UI whether to offer the reveal at all
            // rather than rendering a control that will 403. BUG-533: this flag is now the truth rather than a
            // claim — when it is false the rows carry NO compensation figures, on the row itself or on its
            // nested recommendation. Until BUG-533 it read false while the payload shipped the numbers anyway.
            CompensationVisible = CanSeeCompensation,
            RatingScaleMax = cycle.RatingScaleMax,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            Rows = rows,
            Budget = budget is null ? null : BuildBudgetDto(budget),
        });
    }

    public async Task<Result<RecommendationDto>> GetAsync(Guid recommendationId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecommendationDto>.Failure("Tenant context is not resolved.", 400);

        var rec = await LoadRecommendationsQuery()
            .FirstOrDefaultAsync(r => r.Id == recommendationId, cancellationToken);
        if (rec is null)
            return Result<RecommendationDto>.Failure("Recommendation not found.", 404, "recommendation_not_found");

        var authz = await AuthorizeViewAsync(rec, cancellationToken);
        if (authz.IsFailure)
            return Result<RecommendationDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        // GAP-012 / ISSUE-373: this path DECRYPTS compensation, so it needs its own gate on top of the
        // view-authorization above. AuthorizeViewAsync answers "may you see this recommendation" — a manager
        // may, for their own report — which is a different question from "may you see their salary".
        // Refused BEFORE the read and before the audit row, so an unauthorized attempt cannot log a reveal
        // that never happened.
        if (!CanSeeCompensation)
        {
            return Result<RecommendationDto>.Failure(
                "You do not have permission to view compensation figures.", 403, "compensation_not_permitted");
        }

        var dto = await BuildDtoWithLookupsAsync(rec, cancellationToken);

        // BUG-083: this read returns the AES-decrypted compensation fields (P3-4), so it is a deliberate
        // sensitive reveal. Audit AFTER the authorized read — name the exposed comp fields, store NO values.
        // (GetWorkspaceAsync is NOT audited: it nulls CurrentCompensation and does not decrypt comp.)
        await _auditLogger.LogAndSaveAsync(
            action: PayrollAuditAction.RecommendationViewSensitive,
            resourceType: PayrollAuditAction.ResourceType.Recommendation,
            resourceId: rec.Id.ToString(),
            before: null,
            after: new { fields = CompensationRevealFields, recommendationId = rec.Id, employeeId = rec.EmployeeId },
            cancellationToken: cancellationToken);

        return Result<RecommendationDto>.Success(dto);
    }

    // ════════════════════════════════════════════════════════════════
    //  Auto-generation (AC-2/FR-2/BR-3)
    // ════════════════════════════════════════════════════════════════

    public async Task<Result<AutoGenerateResultDto>> AutoGenerateAsync(
        Guid cycleId, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AutoGenerateResultDto>.Failure("Tenant context is not resolved.", 400);
        if (!IsHr)
            return Result<AutoGenerateResultDto>.Failure(
                "Only HR can auto-generate recommendations.", 403, "forbidden");

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<AutoGenerateResultDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        // BR-3: rules are tenant config; the highest-threshold matching ACTIVE rule wins per employee.
        var rules = await _dbContext.RecommendationRules.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.MinFinalScore)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
            return Result<AutoGenerateResultDto>.Failure(
                "No active auto-generation rules are configured for this tenant.", 422, "no_rules");

        // Employees in the cycle with a SUBMITTED manager review carrying a final score.
        var reviews = await _dbContext.ManagerReviews.AsNoTracking()
            .Where(r => r.CycleId == cycleId && r.FinalScore != null)
            .ToListAsync(cancellationToken);

        // Skip employees who already have a recommendation in this cycle.
        var existingEmpIds = await _dbContext.Recommendations.AsNoTracking()
            .Where(r => r.CycleId == cycleId)
            .Select(r => r.EmployeeId)
            .ToListAsync(cancellationToken);
        var existing = existingEmpIds.ToHashSet();

        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        var actorName = SignerDisplayName(actor) ?? _currentUser.Email;

        var created = new List<Recommendation>();
        var skipped = 0;

        foreach (var review in reviews)
        {
            if (existing.Contains(review.EmployeeId)) { skipped++; continue; }

            var rule = rules.FirstOrDefault(r => review.FinalScore!.Value >= r.MinFinalScore);
            if (rule is null) { skipped++; continue; }

            var rec = new Recommendation
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EmployeeId = review.EmployeeId,
                CycleId = cycleId,
                ManagerReviewId = review.Id,
                Type = rule.RecommendedType,
                Status = RecommendationStatus.Draft,
                IsAutoGenerated = true,
                BonusPercent = rule.RecommendedType == RecommendationType.Bonus ? rule.DefaultBonusPercent : null,
                IncrementPercent = rule.RecommendedType == RecommendationType.Increment ? rule.DefaultIncrementPercent : null,
                AutoGenerationRationale =
                    $"Final score {review.FinalScore!.Value:0.##} ≥ {rule.MinFinalScore:0.##} threshold ({rule.Name}).",
                IsDeleted = false,
            };
            // ENH-015 dry run: skip ALL THREE mutation points. Not just the Add — AppendEvent is the trap. It
            // runs BEFORE `Recommendations.Add(rec)`, so `rec` is still Detached, so its
            // `Entry(rec).State != Added` branch takes `RecommendationEvents.Add(ev)` and puts an Added event
            // row into the change tracker. A "preview" would then leave the DbContext dirty, and the next
            // unrelated SaveChangesAsync in the same scope would try to insert an event for a recommendation
            // that was never created. A preview must leave the context exactly as it found it.
            //
            // MERGE NOTE (ENH-015 x ISSUE-149a): the audit call landed on this method separately. It belongs
            // INSIDE the guard for the same reason and one more — a dry run creates nothing, so an audit row
            // claiming a Created event would be a false entry in the compliance trail. Auditing a preview is
            // worse than not auditing it.
            if (!dryRun)
            {
                AppendEvent(rec, RecommendationEventType.Created, actor, actorName, null,
                    $"Auto-generated: {rec.AutoGenerationRationale}");
                _dbContext.Recommendations.Add(rec);

                // ISSUE-149a: an auto-generated suggestion is still a CREATE — same action and same payload
                // shape as the manual path, with isAutoGenerated distinguishing it. Staged; committed with
                // the batch below.
                _auditLogger.Log(
                    PayrollAuditAction.RecommendationCreated,
                    PayrollAuditAction.ResourceType.Recommendation,
                    rec.Id.ToString(),
                    before: null,
                    after: AuditPayload(AuditState(rec)));
            }

            created.Add(rec);
            existing.Add(review.EmployeeId);
        }

        // Nothing to save on a preview — and nothing to roll back either, because nothing was ever tracked.
        if (!dryRun)
            await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recommendations auto-generated. CycleId={CycleId}, Evaluated={Evaluated}, Created={Created}, " +
            "Skipped={Skipped}, DryRun={DryRun}, TenantId={TenantId}, By={User}",
            cycleId, reviews.Count, created.Count, skipped, dryRun, _tenantContext.TenantId, _currentUser.Email);

        var nameLookup = await EmployeeNameLookupAsync(created.Select(c => c.EmployeeId), cancellationToken);
        // BUG-533: auto-generate is HR-only (Performance.Publish.All), which an HR Officer holds WITHOUT
        // Payroll.ViewCompensation. The generated suggestions carry rule-derived BonusPercent/IncrementPercent,
        // so they are masked on the same rule as every other read.
        var dtos = created
            .Select(c => BuildDto(c, nameLookup.GetValueOrDefault(c.EmployeeId), null, nameLookup, CanSeeCompensation))
            .ToList();

        return Result<AutoGenerateResultDto>.Success(new AutoGenerateResultDto
        {
            CycleId = cycleId,
            EmployeesEvaluated = reviews.Count,
            SuggestionsCreated = created.Count,
            SuggestionsSkipped = skipped,
            DryRun = dryRun,
            Suggestions = dtos,
        });
    }

    // ════════════════════════════════════════════════════════════════
    //  Manual create / override (FR-1/FR-3/BR-5)
    // ════════════════════════════════════════════════════════════════

    public async Task<Result<RecommendationDto>> SaveAsync(SaveRecommendationInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecommendationDto>.Failure("Tenant context is not resolved.", 400);
        if (!IsHr)
            return Result<RecommendationDto>.Failure("Only HR can manage recommendations.", 403, "forbidden");

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == input.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<RecommendationDto>.Failure("Employee not found.", 404, "employee_not_found");

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == input.CycleId, cancellationToken);
        if (cycle is null)
            return Result<RecommendationDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        // ISSUE-351: refuse to create a recommendation that could NEVER be progressed.
        //
        // The dead-end: a Draft recommendation for an employee whose ManagerReview was never submitted, on a
        // cycle that has since reached a TERMINAL status. Both submitting and reopening that review require an
        // open manager-review window; IsPhaseOpen requires Status == Active; and Completed has no outbound edge
        // in IsValidTransition. The row is then permanently stuck and the employee permanently un-recommendable.
        //
        // Deliberately narrow. Drafting a recommendation BEFORE the review is submitted stays allowed — that is
        // legitimate early preparation while the cycle is still open and the review can still land. Only the
        // combination that is already irrecoverable is refused. (Auto-generate never produces this state: it
        // filters FinalScore != null. This closes the same gap on the manual path.)
        //
        // Prevention rather than an HR-privileged reopen path: adding a Completed -> Active transition would let
        // a cycle whose final ratings are already published be un-completed, which is a governance change far
        // larger than the obscure state it would rescue. That option stays open if recoverability is ever
        // wanted for real stuck rows; none are known to exist.
        if (cycle.IsTerminal)
        {
            var reviewSubmitted = await _dbContext.ManagerReviews.AsNoTracking()
                .AnyAsync(r => r.CycleId == input.CycleId
                            && r.EmployeeId == input.EmployeeId
                            && r.SubmittedAt != null, cancellationToken);
            if (!reviewSubmitted)
                return Result<RecommendationDto>.Failure(
                    $"This cycle is {cycle.Status} and the employee's manager review was never submitted, so a "
                    + "recommendation created now could never be progressed. Submit the review before the cycle "
                    + "completes, or record this outside the cycle.",
                    422, "cycle_terminal_review_unsubmitted");
        }

        var d = input.Details;

        // BR-5: a promotion requires a target grade + effective date.
        if (input.Type == RecommendationType.Promotion &&
            (string.IsNullOrWhiteSpace(d.TargetGrade) || d.EffectiveDate is null))
            return Result<RecommendationDto>.Failure(
                "A promotion recommendation requires a target grade and an effective date.", 422, "promotion_details_required");

        // Validate the budget belongs to the cycle (FR-8), when supplied.
        if (d.BudgetId is { } bId)
        {
            var budgetOk = await _dbContext.RecommendationBudgets.AsNoTracking()
                .AnyAsync(b => b.Id == bId && b.CycleId == input.CycleId, cancellationToken);
            if (!budgetOk)
                return Result<RecommendationDto>.Failure("Budget not found for this cycle.", 404, "budget_not_found");
        }

        var existing = await _dbContext.Recommendations
            .Include(r => r.Approvers)
            .Include(r => r.Events)
            .FirstOrDefaultAsync(r => r.CycleId == input.CycleId && r.EmployeeId == input.EmployeeId, cancellationToken);

        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        var actorName = SignerDisplayName(actor) ?? _currentUser.Email;

        var review = await _dbContext.ManagerReviews.AsNoTracking()
            .Where(r => r.CycleId == input.CycleId && r.EmployeeId == input.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        // ISSUE-149(b): the FR-3 justification is operator free text stored verbatim. #666 widened the sink —
        // AuditPayload now copies it into audit_log.before/after on every create and override, and audit rows
        // are IMMUTABLE and retained, so sanitizing at write time will NOT clean anything already written.
        // Every day this waits adds permanently unsanitized history. Exposure today is API-only (no innerHTML
        // sink renders this field), so this is defence-in-depth, not an active XSS fix. Sanitized ONCE here,
        // above the create/override split, so both write paths and the audit projection see the same value.
        // Trim() nulls a blank/whitespace result, which is what makes the FR-3 required-check below correct:
        // a justification consisting only of a payload sanitizes to nothing and must be REFUSED, not accepted
        // as satisfying FR-3 and then stored blank (the ISSUE-121 ordering).
        var justification = Trim(_sanitizer.Sanitize(input.Justification));

        Recommendation rec;
        if (existing is null)
        {
            rec = new Recommendation
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EmployeeId = input.EmployeeId,
                CycleId = input.CycleId,
                ManagerReviewId = review?.Id,
                Status = RecommendationStatus.Draft,
                IsAutoGenerated = false,
                IsDeleted = false,
            };
            ApplyDetails(rec, input.Type, d, employee, await CurrentCtcSnapshotAsync(employee.Id, cancellationToken));
            rec.Justification = justification;
            AppendEvent(rec, RecommendationEventType.Created, actor, actorName, input.ClientIpAddress, null);
            _dbContext.Recommendations.Add(rec);

            // ISSUE-149a: central audit trail for a recommendation CREATE. Staged on the same DbContext so it
            // commits atomically with the row itself — a create that rolls back leaves no orphan audit row.
            _auditLogger.Log(
                PayrollAuditAction.RecommendationCreated,
                PayrollAuditAction.ResourceType.Recommendation,
                rec.Id.ToString(),
                before: null,
                after: AuditPayload(AuditState(rec)));
        }
        else
        {
            // FR-3: overriding an existing recommendation REQUIRES a justification. Checked on the SANITIZED
            // value (ISSUE-149(b)) — a justification that is nothing but markup is no justification at all.
            if (justification is null)
                return Result<RecommendationDto>.Failure(
                    "A justification is required when overriding an existing recommendation.", 422, "justification_required");
            if (existing.Status is RecommendationStatus.Approved or RecommendationStatus.Rejected)
                return Result<RecommendationDto>.Failure(
                    "A decided recommendation can no longer be changed.", 409, "recommendation_decided");

            rec = existing;

            // ISSUE-149a: snapshot BEFORE the mutation — `existing` is a TRACKED entity, so the old values are
            // gone the instant ApplyDetails runs and there is nothing left to put in the audit `before`.
            var beforeState = AuditState(rec);

            ApplyDetails(rec, input.Type, d, employee, await CurrentCtcSnapshotAsync(employee.Id, cancellationToken));
            rec.Justification = justification;
            AppendEvent(rec, RecommendationEventType.Overridden, actor, actorName, input.ClientIpAddress,
                $"Type set to {input.Type}.");

            // The highest-value audit row in this file: who changed WHICH field on an existing recommendation,
            // carrying the FR-3 justification. Compensation moves are named, never valued (see AuditPayload).
            var afterState = AuditState(rec);
            _auditLogger.Log(
                PayrollAuditAction.RecommendationOverridden,
                PayrollAuditAction.ResourceType.Recommendation,
                rec.Id.ToString(),
                before: AuditPayload(beforeState),
                after: AuditPayload(afterState, ChangedFields(beforeState, afterState)));
        }

        var fail = await SaveAsync(cancellationToken);
        if (fail is not null) return fail;

        _logger.LogInformation(
            "Recommendation saved. RecommendationId={RecommendationId}, EmployeeId={EmployeeId}, Type={Type}, " +
            "Override={Override}, TenantId={TenantId}, By={User}",
            rec.Id, rec.EmployeeId, rec.Type, existing is not null, _tenantContext.TenantId, _currentUser.Email);

        return await ReloadAsync(rec.Id, cancellationToken);
    }

    // ════════════════════════════════════════════════════════════════
    //  Submit into the approval workflow (AC-3/FR-4, gates BR-1/BR-2)
    // ════════════════════════════════════════════════════════════════

    public async Task<Result<RecommendationDto>> SubmitAsync(SubmitRecommendationInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecommendationDto>.Failure("Tenant context is not resolved.", 400);
        if (!IsHr)
            return Result<RecommendationDto>.Failure("Only HR can submit recommendations.", 403, "forbidden");

        var rec = await _dbContext.Recommendations
            .Include(r => r.Approvers)
            .Include(r => r.Events)
            .FirstOrDefaultAsync(r => r.Id == input.RecommendationId, cancellationToken);
        if (rec is null)
            return Result<RecommendationDto>.Failure("Recommendation not found.", 404, "recommendation_not_found");

        if (rec.Status is not RecommendationStatus.Draft)
            return Result<RecommendationDto>.Failure(
                "Only a draft recommendation can be submitted.", 409, "not_draft");

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == rec.CycleId, cancellationToken);
        if (cycle is null)
            return Result<RecommendationDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        // BR-1: final ratings must be published — i.e. the cycle has reached Completed (publish phase done).
        if (cycle.Status != AppraisalCycleStatus.Completed)
            return Result<RecommendationDto>.Failure(
                "Recommendations can only be submitted after the cycle's final ratings are published.",
                422, "final_ratings_not_published");

        // BR-2: when calibration is enabled, the cycle's Calibration PHASE must be complete.
        //
        // This used to check whether the employee's manager review had been submitted — a PROXY, and a poor
        // one. It passed with zero calibrations applied, so the gate never actually gated, while returning
        // the error code `calibration_incomplete` for a condition it never tested. F3 gives the phase a real
        // completion fact (CyclePhase.CompletedOn), so the gate can read what its own error code claims.
        //
        // Read from the PHASE, never from "does this employee have a RatingCalibration row". The normal
        // committee outcome for most employees is NO adjustment, so per-employee calibration evidence is
        // permanently absent for them — a per-employee gate would never open for the majority and would look
        // like a product bug rather than a modelling one. This is the whole argument for phase-level state.
        if (cycle.IsCalibrationEnabled)
        {
            var calibrationComplete = await _dbContext.CyclePhases.AsNoTracking().AnyAsync(
                ph => ph.CycleId == rec.CycleId
                      && ph.PhaseType == CyclePhaseType.Calibration
                      && ph.CompletedOn != null,
                cancellationToken);
            if (!calibrationComplete)
                return Result<RecommendationDto>.Failure(
                    "Calibration is enabled for this cycle; recommendations can only proceed after the calibration phase is marked complete.",
                    422, "calibration_incomplete");
        }

        // BR-5 re-check on submit (an override could have left a promotion incomplete).
        if (rec.Type == RecommendationType.Promotion &&
            (string.IsNullOrWhiteSpace(rec.TargetGrade) || rec.EffectiveDate is null))
            return Result<RecommendationDto>.Failure(
                "A promotion recommendation requires a target grade and an effective date.", 422, "promotion_details_required");

        // BR-4 (ISSUE-145): the budget is a SOFT cap, not a hard one — but proceeding OVER budget REQUIRES a
        // justification (the justification IS the gate). Checked BEFORE charging, so a rejected submit never
        // consumes the budget. Over budget WITH a justification proceeds (soft warning); within budget is unchanged.
        if (string.IsNullOrWhiteSpace(rec.Justification) && await WouldExceedBudgetAsync(rec, cancellationToken))
            return Result<RecommendationDto>.Failure(
                "This recommendation exceeds the remaining budget; a justification is required to proceed over budget.",
                400, "over_budget_requires_justification");

        // AC-3: link to the employee's performance record.
        if (rec.ManagerReviewId is null)
        {
            var review = await _dbContext.ManagerReviews.AsNoTracking()
                .Where(r => r.CycleId == rec.CycleId && r.EmployeeId == rec.EmployeeId)
                .FirstOrDefaultAsync(cancellationToken);
            rec.ManagerReviewId = review?.Id;
        }

        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        var actorName = SignerDisplayName(actor) ?? _currentUser.Email;

        // FR-4: build the ordered approver chain. No approvers configured ⇒ status Submitted (terminal "submitted").
        var approverIds = input.ApproverEmployeeIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
        var order = 0;
        foreach (var approverId in approverIds)
        {
            var approver = new RecommendationApprover
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                RecommendationId = rec.Id,
                ApproverEmployeeId = approverId,
                StepOrder = order++,
                Decision = RecommendationApproverDecision.Pending,
                IsDeleted = false,
            };
            rec.Approvers.Add(approver);
            _dbContext.RecommendationApprovers.Add(approver);
        }

        rec.Status = approverIds.Count == 0 ? RecommendationStatus.Submitted : RecommendationStatus.PendingApproval;
        rec.SubmittedAt = DateTime.UtcNow;

        // FR-8/BR-4: charge the budget (soft-warning only, never blocks).
        var warning = await ChargeBudgetAsync(rec, cancellationToken);

        AppendEvent(rec, RecommendationEventType.Submitted, actor, actorName, input.ClientIpAddress,
            approverIds.Count == 0 ? "Submitted (no approvers configured)." : $"Submitted to {approverIds.Count} approver(s).");

        // ISSUE-149a: submit is the transition into the approval workflow — who submitted, to which approver
        // chain, and whether it went through OVER budget (BR-4 permits that with a justification; the audit is
        // where "we knowingly went over" is recorded). Staged so it commits with the transition.
        _auditLogger.Log(
            PayrollAuditAction.RecommendationSubmitted,
            PayrollAuditAction.ResourceType.Recommendation,
            rec.Id.ToString(),
            before: new { status = RecommendationStatus.Draft.ToString() },
            after: new
            {
                status = rec.Status.ToString(),
                type = rec.Type.ToString(),
                submittedAt = rec.SubmittedAt,
                approverCount = approverIds.Count,
                approverEmployeeIds = approverIds,
                budgetId = rec.BudgetId,
                overBudget = warning,
            });

        var fail = await SaveAsync(cancellationToken);
        if (fail is not null) return fail;

        _logger.LogInformation(
            "Recommendation submitted. RecommendationId={RecommendationId}, Status={Status}, Approvers={Approvers}, " +
            "BudgetWarning={Warning}, TenantId={TenantId}, By={User}",
            rec.Id, rec.Status, approverIds.Count, warning, _tenantContext.TenantId, _currentUser.Email);

        return await ReloadAsync(rec.Id, cancellationToken);
    }

    // ════════════════════════════════════════════════════════════════
    //  Approve / reject (FR-4)
    // ════════════════════════════════════════════════════════════════

    public async Task<Result<RecommendationDto>> DecideAsync(DecideRecommendationInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecommendationDto>.Failure("Tenant context is not resolved.", 400);

        var rec = await _dbContext.Recommendations
            .Include(r => r.Approvers)
            .Include(r => r.Events)
            .FirstOrDefaultAsync(r => r.Id == input.RecommendationId, cancellationToken);
        if (rec is null)
            return Result<RecommendationDto>.Failure("Recommendation not found.", 404, "recommendation_not_found");

        if (rec.Status != RecommendationStatus.PendingApproval)
            return Result<RecommendationDto>.Failure(
                "This recommendation is not awaiting approval.", 409, "not_pending_approval");

        var me = await GetCurrentEmployeeAsync(cancellationToken);
        if (me is null)
            return Result<RecommendationDto>.Failure("The current user is not linked to an employee record.", 403, "no_employee_record");

        // FR-4: the next pending approver in chain order must act.
        var nextStep = rec.Approvers
            .Where(a => a.Decision == RecommendationApproverDecision.Pending)
            .OrderBy(a => a.StepOrder)
            .FirstOrDefault();
        if (nextStep is null)
            return Result<RecommendationDto>.Failure("There is no pending approval step.", 409, "no_pending_step");

        // Only the current approver (or HR Publish.All) can decide; HR can override but must be in the chain ideally.
        if (nextStep.ApproverEmployeeId != me.Id && !IsHr)
            return Result<RecommendationDto>.Failure(
                "Only the current approver can act on this recommendation.", 403, "not_current_approver");

        var actorName = SignerDisplayName(me) ?? _currentUser.Email;
        // ISSUE-149(b): the approver's decision comment. It lands in TWO permanent places — the approval step
        // (RecommendationApprover.Comment) and the append-only FR-7 RecommendationEvent.Detail — neither of
        // which has an edit path, so this is write-once history exactly like the audit rows. Sanitize BEFORE
        // the blank check so a comment that is only a payload becomes null (an absent comment) rather than an
        // empty string recorded as if the approver had typed something.
        var comment = Trim(_sanitizer.Sanitize(input.Comment));
        nextStep.DecidedAt = DateTime.UtcNow;
        nextStep.Comment = comment;

        if (!input.Approve)
        {
            nextStep.Decision = RecommendationApproverDecision.Rejected;
            rec.Status = RecommendationStatus.Rejected;
            rec.DecidedAt = DateTime.UtcNow;
            await ReverseBudgetAsync(rec, cancellationToken); // release the budget charge.
            AppendEvent(rec, RecommendationEventType.Rejected, me, actorName, input.ClientIpAddress, comment);
        }
        else
        {
            nextStep.Decision = RecommendationApproverDecision.Approved;
            AppendEvent(rec, RecommendationEventType.Approved, me, actorName, input.ClientIpAddress,
                $"Step {nextStep.StepOrder + 1} approved.{(comment is null ? "" : $" {comment}")}");

            var allApproved = rec.Approvers.All(a => a.Decision == RecommendationApproverDecision.Approved);
            if (allApproved)
            {
                rec.Status = RecommendationStatus.Approved;
                rec.DecidedAt = DateTime.UtcNow;
                // BR-6: raise the downstream-integration seam + immutable IntegrationRaised event.
                var target = IntegrationTarget(rec.Type);
                AppendEvent(rec, RecommendationEventType.IntegrationRaised, null, "System", null,
                    $"Downstream integration raised → {target} (seam; not wired).");
            }
        }

        // ISSUE-149a: the approval decision itself. The action mirrors PayrollRun.Approved / PayrollRun.Rejected;
        // the step fields identify WHO in the chain acted and whether this decision was the final one.
        _auditLogger.Log(
            input.Approve ? PayrollAuditAction.RecommendationApproved : PayrollAuditAction.RecommendationRejected,
            PayrollAuditAction.ResourceType.Recommendation,
            rec.Id.ToString(),
            before: new { status = RecommendationStatus.PendingApproval.ToString() },
            after: new
            {
                status = rec.Status.ToString(),
                stepOrder = nextStep.StepOrder,
                approverEmployeeId = nextStep.ApproverEmployeeId,
                decision = nextStep.Decision.ToString(),
                decidedAt = nextStep.DecidedAt,
                isFinalDecision = rec.DecidedAt is not null,
                comment,
            });

        var fail = await SaveAsync(cancellationToken);
        if (fail is not null) return fail;

        if (rec.Status == RecommendationStatus.Approved)
        {
            await _integration.RaiseAsync(
                rec.Id, rec.EmployeeId, rec.CycleId, rec.Type, IntegrationTarget(rec.Type), cancellationToken);
        }

        _logger.LogInformation(
            "Recommendation decision. RecommendationId={RecommendationId}, Approve={Approve}, NewStatus={Status}, " +
            "TenantId={TenantId}, By={User}",
            rec.Id, input.Approve, rec.Status, _tenantContext.TenantId, _currentUser.Email);

        return await ReloadAsync(rec.Id, cancellationToken);
    }

    // ════════════════════════════════════════════════════════════════
    //  Summary stats (AC-4/FR-6) + export
    // ════════════════════════════════════════════════════════════════

    public async Task<Result<RecommendationSummaryDto>> GetSummaryAsync(Guid? cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecommendationSummaryDto>.Failure("Tenant context is not resolved.", 400);
        if (!IsHr)
            return Result<RecommendationSummaryDto>.Failure(
                "Only HR can view the recommendation summary.", 403, "forbidden");

        var cycle = await ResolveCycleAsync(cycleId, cancellationToken);
        if (cycle is null)
            return Result<RecommendationSummaryDto>.Failure("No appraisal cycle is available.", 404, "no_cycle");

        var summary = await BuildSummaryAsync(cycle, cancellationToken);
        return Result<RecommendationSummaryDto>.Success(summary);
    }

    public async Task<Result<RecommendationExportResult>> ExportSummaryAsync(
        Guid? cycleId, string format, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecommendationExportResult>.Failure("Tenant context is not resolved.", 400);
        if (!IsHr)
            return Result<RecommendationExportResult>.Failure(
                "Only HR can export the recommendation summary.", 403, "forbidden");

        var normalized = (format ?? "csv").Trim().ToLowerInvariant();
        if (normalized is not ("csv" or "xlsx" or "pdf"))
            return Result<RecommendationExportResult>.Failure(
                "Export format must be one of csv, xlsx, pdf.", 400, "invalid_format");

        var cycle = await ResolveCycleAsync(cycleId, cancellationToken);
        if (cycle is null)
            return Result<RecommendationExportResult>.Failure("No appraisal cycle is available.", 404, "no_cycle");

        var summary = await BuildSummaryAsync(cycle, cancellationToken);
        var (content, fileName, contentType) = normalized switch
        {
            "xlsx" => (RenderXlsx(summary), $"recommendations-{cycle.Name}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            "pdf" => (Performance.PerformancePdfRenderer.RenderRecommendationSummary(summary, _tenantContext.PrimaryColor),
                $"recommendations-{cycle.Name}.pdf", "application/pdf"),
            _ => (RenderCsv(summary), $"recommendations-{cycle.Name}.csv", "text/csv"),
        };

        return Result<RecommendationExportResult>.Success(new RecommendationExportResult
        {
            FileContent = content,
            FileName = fileName,
            ContentType = contentType,
        });
    }

    private async Task<RecommendationSummaryDto> BuildSummaryAsync(AppraisalCycle cycle, CancellationToken ct)
    {
        // GAP-012 / ISSUE-373: the tenant's display currency for the money figures below. IgnoreQueryFilters is
        // not needed — the tenant row is reachable under the resolved context, and Tenant carries no tenant_id
        // filter of its own. Falls back to empty rather than guessing a currency: a WRONG unit beside a salary
        // figure is worse than none.
        var tenantCurrency = await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.Currency)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var recs = await _dbContext.Recommendations.AsNoTracking()
            .Where(r => r.CycleId == cycle.Id)
            .ToListAsync(ct);

        // Only SUBMITTED+ recommendations count toward the pools (Draft suggestions excluded).
        var counted = recs.Where(r => r.Status != RecommendationStatus.Draft && r.Status != RecommendationStatus.Rejected).ToList();

        var totalPromotions = counted.Count(r => r.Type == RecommendationType.Promotion);
        var bonusPool = counted.Where(r => r.Type == RecommendationType.Bonus).Sum(r => r.BonusAmount ?? 0m);
        var incrementTotal = counted.Where(r => r.Type == RecommendationType.Increment).Sum(r => r.IncrementAmount ?? 0m);
        var trainingCount = counted.Count(r => r.Type == RecommendationType.TrainingNomination);

        var byStatus = recs.GroupBy(r => r.Status)
            .Select(g => new RecommendationCountByStatusDto
            {
                Status = g.Key,
                StatusName = g.Key.ToString(),
                Count = g.Count(),
            })
            .OrderBy(s => s.Status)
            .ToList();

        // Increment distribution by department.
        var incRecs = counted.Where(r => r.Type == RecommendationType.Increment).ToList();
        var empIds = incRecs.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.DepartmentId })
            .ToListAsync(ct);
        var deptByEmp = employees.ToDictionary(e => e.Id, e => e.DepartmentId);
        var deptNames = await DepartmentNamesAsync(employees.Select(e => e.DepartmentId), ct);

        var incrementByDept = incRecs
            .Where(r => deptByEmp.ContainsKey(r.EmployeeId))
            .GroupBy(r => deptByEmp[r.EmployeeId])
            .Select(g => new RecommendationIncrementByDeptDto
            {
                DepartmentId = g.Key,
                DepartmentName = deptNames.GetValueOrDefault(g.Key, string.Empty),
                RecommendationCount = g.Count(),
                TotalIncrementAmount = g.Sum(r => r.IncrementAmount ?? 0m),
            })
            .OrderByDescending(d => d.TotalIncrementAmount)
            .ToList();

        var budget = await _dbContext.RecommendationBudgets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CycleId == cycle.Id, ct);

        // AC-4: comparison vs the previous cycle (the most recently started cycle before this one).
        var prevCycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .Where(c => c.StartDate < cycle.StartDate)
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync(ct);
        RecommendationPreviousCycleDto? prev = null;
        if (prevCycle is not null)
        {
            var prevRecs = await _dbContext.Recommendations.AsNoTracking()
                .Where(r => r.CycleId == prevCycle.Id
                    && r.Status != RecommendationStatus.Draft && r.Status != RecommendationStatus.Rejected)
                .ToListAsync(ct);
            prev = new RecommendationPreviousCycleDto
            {
                CycleId = prevCycle.Id,
                CycleName = prevCycle.Name,
                TotalPromotions = prevRecs.Count(r => r.Type == RecommendationType.Promotion),
                TotalBonusPoolAllocated = prevRecs.Where(r => r.Type == RecommendationType.Bonus).Sum(r => r.BonusAmount ?? 0m),
                TotalIncrementAllocated = prevRecs.Where(r => r.Type == RecommendationType.Increment).Sum(r => r.IncrementAmount ?? 0m),
            };
        }

        return new RecommendationSummaryDto
        {
            CycleId = cycle.Id,
            CycleName = cycle.Name,
            // GAP-012 / ISSUE-373: the UI prints this beside every money figure and never received it.
            Currency = tenantCurrency,
            TotalRecommendations = recs.Count,
            TotalPromotions = totalPromotions,
            TotalBonusPoolAllocated = bonusPool,
            TotalIncrementAllocated = incrementTotal,
            TotalTrainingNominations = trainingCount,
            ByStatus = byStatus,
            IncrementByDepartment = incrementByDept,
            Budget = budget is null ? null : BuildBudgetDto(budget),
            PreviousCycle = prev,
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  Budget config (FR-8)
    // ════════════════════════════════════════════════════════════════

    public async Task<Result<RecommendationBudgetDto>> SaveBudgetAsync(SaveBudgetInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecommendationBudgetDto>.Failure("Tenant context is not resolved.", 400);
        if (!IsHr)
            return Result<RecommendationBudgetDto>.Failure("Only HR can manage the budget.", 403, "forbidden");
        if (input.AllocatedAmount < 0)
            return Result<RecommendationBudgetDto>.Failure("The allocated amount cannot be negative.", 422, "invalid_amount");

        var cycleExists = await _dbContext.AppraisalCycles.AsNoTracking()
            .AnyAsync(c => c.Id == input.CycleId, cancellationToken);
        if (!cycleExists)
            return Result<RecommendationBudgetDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        var budget = await _dbContext.RecommendationBudgets
            .FirstOrDefaultAsync(b => b.CycleId == input.CycleId, cancellationToken);
        if (budget is null)
        {
            budget = new RecommendationBudget
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                CycleId = input.CycleId,
                Name = input.Name.Trim(),
                AllocatedAmount = input.AllocatedAmount,
                ConsumedAmount = 0m,
                Currency = string.IsNullOrWhiteSpace(input.Currency) ? "USD" : input.Currency.Trim().ToUpperInvariant(),
                IsDeleted = false,
            };
            _dbContext.RecommendationBudgets.Add(budget);
        }
        else
        {
            budget.Name = input.Name.Trim();
            budget.AllocatedAmount = input.AllocatedAmount;
            if (!string.IsNullOrWhiteSpace(input.Currency))
                budget.Currency = input.Currency.Trim().ToUpperInvariant();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecommendationBudgetDto>.Success(BuildBudgetDto(budget));
    }

    public async Task<Result<RecommendationBudgetDto?>> GetBudgetAsync(Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecommendationBudgetDto?>.Failure("Tenant context is not resolved.", 400);
        if (!IsHr)
            return Result<RecommendationBudgetDto?>.Failure("Only HR can view the budget.", 403, "forbidden");

        var budget = await _dbContext.RecommendationBudgets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CycleId == cycleId, cancellationToken);
        return Result<RecommendationBudgetDto?>.Success(budget is null ? null : BuildBudgetDto(budget));
    }

    // ════════════════════════════════════════════════════════════════
    //  Rule config (FR-2)
    // ════════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<RecommendationRuleDto>>> ListRulesAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<RecommendationRuleDto>>.Failure("Tenant context is not resolved.", 400);
        if (!IsHr)
            return Result<IReadOnlyList<RecommendationRuleDto>>.Failure("Only HR can view the rules.", 403, "forbidden");

        var rules = await _dbContext.RecommendationRules.AsNoTracking()
            .OrderByDescending(r => r.MinFinalScore)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<RecommendationRuleDto>>.Success(rules.Select(BuildRuleDto).ToList());
    }

    public async Task<Result<RecommendationRuleDto>> SaveRuleAsync(SaveRuleInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecommendationRuleDto>.Failure("Tenant context is not resolved.", 400);
        if (!IsHr)
            return Result<RecommendationRuleDto>.Failure("Only HR can manage the rules.", 403, "forbidden");
        if (input.MinFinalScore < 0)
            return Result<RecommendationRuleDto>.Failure("The threshold score cannot be negative.", 422, "invalid_threshold");

        RecommendationRule rule;
        if (input.RuleId is { } id)
        {
            var existing = await _dbContext.RecommendationRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (existing is null)
                return Result<RecommendationRuleDto>.Failure("Rule not found.", 404, "rule_not_found");
            rule = existing;
            rule.Name = input.Name.Trim();
            rule.MinFinalScore = input.MinFinalScore;
            rule.RecommendedType = input.RecommendedType;
            rule.DefaultBonusPercent = input.DefaultBonusPercent;
            rule.DefaultIncrementPercent = input.DefaultIncrementPercent;
            rule.IsActive = input.IsActive;
        }
        else
        {
            rule = new RecommendationRule
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                Name = input.Name.Trim(),
                MinFinalScore = input.MinFinalScore,
                RecommendedType = input.RecommendedType,
                DefaultBonusPercent = input.DefaultBonusPercent,
                DefaultIncrementPercent = input.DefaultIncrementPercent,
                IsActive = input.IsActive,
                IsDeleted = false,
            };
            _dbContext.RecommendationRules.Add(rule);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecommendationRuleDto>.Success(BuildRuleDto(rule));
    }

    // ════════════════════════════════════════════════════════════════
    //  Authorization / scope (AC-5/NFR-5)
    // ════════════════════════════════════════════════════════════════

    private sealed record RecommendationScope(bool IsOrg, HashSet<Guid> TeamEmployeeIds);

    /// <summary>
    /// AC-5/NFR-5: HR (Publish.All) gets the full org workspace; a manager (Review.Team WITHOUT Publish.All) is
    /// HARD-restricted to their direct reports. Anyone else (general employees) is forbidden.
    /// </summary>
    private async Task<Result<RecommendationScope>> ResolveScopeAsync(CancellationToken cancellationToken)
    {
        if (IsHr)
            return Result<RecommendationScope>.Success(new RecommendationScope(true, []));

        if (!IsManager)
            return Result<RecommendationScope>.Failure(
                "You do not have permission to view recommendations.", 403, "forbidden");

        var me = await GetCurrentEmployeeAsync(cancellationToken);
        if (me is null)
            return Result<RecommendationScope>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        var reportIds = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == me.Id)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        return Result<RecommendationScope>.Success(new RecommendationScope(false, reportIds.ToHashSet()));
    }

    private async Task<Result> AuthorizeViewAsync(Recommendation rec, CancellationToken cancellationToken)
    {
        if (IsHr) return Result.Success();
        if (!IsManager)
            return Result.Failure("You do not have permission to view this recommendation.", 403, "forbidden");

        var me = await GetCurrentEmployeeAsync(cancellationToken);
        if (me is null)
            return Result.Failure("The current user is not linked to an employee record.", 403, "no_employee_record");

        var isReport = await _dbContext.Employees.AsNoTracking()
            .AnyAsync(e => e.Id == rec.EmployeeId && e.ReportsToEmployeeId == me.Id, cancellationToken);
        return isReport
            ? Result.Success()
            : Result.Failure("You can only view recommendations for your direct reports.", 403, "not_direct_report");
    }

    private async Task<List<Guid>> ResolveCycleEmployeeIdsAsync(
        Guid cycleId, RecommendationScope scope, CancellationToken ct)
    {
        var participantIds = await _dbContext.CycleParticipants.AsNoTracking()
            .Where(p => p.CycleId == cycleId)
            .Select(p => p.EmployeeId)
            .ToListAsync(ct);

        List<Guid> ids = participantIds.Count > 0
            ? participantIds
            : await _dbContext.Employees.AsNoTracking().Where(e => e.IsActive).Select(e => e.Id).ToListAsync(ct);

        if (!scope.IsOrg)
            ids = ids.Where(id => scope.TeamEmployeeIds.Contains(id)).ToList();

        return ids.Distinct().ToList();
    }

    // ════════════════════════════════════════════════════════════════
    //  Budget charge / reverse (FR-8/BR-4)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// BR-4 (ISSUE-145): true when charging this recommendation's amount WOULD push its budget's consumed over the
    /// allocation. Read-only (does NOT mutate the budget) — used to gate an over-budget submit that lacks a
    /// justification, mirroring <see cref="RecommendationBudget.IsExceeded"/> (consumed &gt; allocated).
    /// </summary>
    private async Task<bool> WouldExceedBudgetAsync(Recommendation rec, CancellationToken ct)
    {
        if (rec.BudgetId is not { } budgetId || rec.BudgetCharge <= 0m) return false;
        var budget = await _dbContext.RecommendationBudgets.AsNoTracking().FirstOrDefaultAsync(b => b.Id == budgetId, ct);
        if (budget is null) return false;
        return budget.ConsumedAmount + rec.BudgetCharge > budget.AllocatedAmount;
    }

    /// <summary>Charges the recommendation's monetary amount to its budget. Returns true when the budget is now exceeded (BR-4 soft warning).</summary>
    private async Task<bool> ChargeBudgetAsync(Recommendation rec, CancellationToken ct)
    {
        if (rec.BudgetId is not { } budgetId || rec.BudgetCharge <= 0m) return false;
        var budget = await _dbContext.RecommendationBudgets.FirstOrDefaultAsync(b => b.Id == budgetId, ct);
        if (budget is null) return false;
        budget.ConsumedAmount += rec.BudgetCharge;
        return budget.IsExceeded;
    }

    private async Task ReverseBudgetAsync(Recommendation rec, CancellationToken ct)
    {
        if (rec.BudgetId is not { } budgetId || rec.BudgetCharge <= 0m) return;
        var budget = await _dbContext.RecommendationBudgets.FirstOrDefaultAsync(b => b.Id == budgetId, ct);
        if (budget is null) return;
        budget.ConsumedAmount = Math.Max(0m, budget.ConsumedAmount - rec.BudgetCharge);
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    private IQueryable<Recommendation> LoadRecommendationsQuery() => _dbContext.Recommendations.AsNoTracking()
        .Include(r => r.Approvers)
        .Include(r => r.Events);

    /// <summary>
    /// ISSUE-150: <paramref name="currentCompensationSnapshot"/> is the employee's annual CTC AT THE MOMENT
    /// THIS RECOMMENDATION WAS WRITTEN, persisted into the (encrypted) CurrentCompensation column. It is
    /// deliberately a SNAPSHOT and not the live figure: an approver reviewing "raise X to Y" months later
    /// needs the X the proposer actually saw, and the workspace separately renders the LIVE value for the
    /// comparison TC-PRF-010-06 step 4 asks for. The two answer different questions; storing only one of
    /// them loses the other.
    /// </summary>
    /// <summary>ISSUE-150: one employee's current annual CTC, or null when unassigned or the seam is absent.</summary>
    private async Task<decimal?> CurrentCtcSnapshotAsync(Guid employeeId, CancellationToken ct)
    {
        if (_salaryAssignment is null) return null;
        var map = await _salaryAssignment.GetCurrentAnnualCtcAsync(new[] { employeeId }, ct);
        return map.TryGetValue(employeeId, out var v) ? v : null;
    }

    private void ApplyDetails(Recommendation rec, RecommendationType type, RecommendationDetailsInput d, Employee employee,
        decimal? currentCompensationSnapshot = null)
    {
        rec.Type = type;
        if (currentCompensationSnapshot is not null)
            rec.CurrentCompensation = currentCompensationSnapshot;
        rec.TargetGrade = Trim(d.TargetGrade);
        rec.TargetTitle = Trim(d.TargetTitle);
        rec.EffectiveDate = d.EffectiveDate;
        rec.BonusAmount = d.BonusAmount;
        rec.BonusPercent = d.BonusPercent;
        rec.IncrementAmount = d.IncrementAmount;
        rec.IncrementPercent = d.IncrementPercent;
        rec.TrainingCourse = Trim(d.TrainingCourse);
        // ISSUE-149(b): the tenant-defined custom type label (FR-1) — like the justification it is copied
        // into audit_log via AuditPayload, so an unsanitized value becomes immutable history. Sanitize
        // BEFORE Trim() so a label that is only a payload collapses to null rather than being stored as an
        // empty label. NOTE the deliberate scope: TargetGrade/TargetTitle/TrainingCourse above are ALSO
        // free text with the same audit exposure and are NOT sanitized here — they are in this file's
        // follow-up, not silently forgotten.
        rec.CustomTypeLabel = Trim(_sanitizer.Sanitize(d.CustomTypeLabel));
        rec.BudgetId = d.BudgetId;
    }

    private void AppendEvent(
        Recommendation rec, RecommendationEventType type, Employee? actor, string actorName, string? clientIp, string? detail)
    {
        var ev = new RecommendationEvent
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            RecommendationId = rec.Id,
            EventType = type,
            ActorUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            ActorEmployeeId = actor?.Id,
            ActorName = actorName,
            OccurredAt = DateTime.UtcNow,
            ClientIpAddress = clientIp,
            Detail = detail,
            IsDeleted = false,
        };
        rec.Events.Add(ev);
        if (_dbContext.Entry(rec).State != EntityState.Added)
            _dbContext.RecommendationEvents.Add(ev);
    }

    // ════════════════════════════════════════════════════════════════
    //  Write-path audit projection (ISSUE-149a)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// In-memory snapshot of a recommendation's auditable state. The compensation figures are held here ONLY
    /// so that a CHANGE to them can be detected — they are never serialized into the audit row.
    /// </summary>
    private sealed record RecommendationAuditState(
        string Status, string Type, bool IsAutoGenerated, string? TargetGrade, string? TargetTitle,
        DateOnly? EffectiveDate, string? TrainingCourse, string? CustomTypeLabel, Guid? BudgetId,
        string? Justification, decimal? BonusAmount, decimal? BonusPercent,
        decimal? IncrementAmount, decimal? IncrementPercent);

    private static RecommendationAuditState AuditState(Recommendation rec) => new(
        rec.Status.ToString(), rec.Type.ToString(), rec.IsAutoGenerated, rec.TargetGrade, rec.TargetTitle,
        rec.EffectiveDate, rec.TrainingCourse, rec.CustomTypeLabel, rec.BudgetId, rec.Justification,
        rec.BonusAmount, rec.BonusPercent, rec.IncrementAmount, rec.IncrementPercent);

    /// <summary>
    /// The serializable audit projection. It deliberately carries NO compensation FIGURE: those columns are
    /// encrypted at rest (RecommendationConfiguration) and gated behind Payroll.ViewCompensation, and the
    /// reveal audit on <see cref="GetAsync"/> stores field NAMES only. Copying the numbers into
    /// <c>audit_log.after</c> would republish them in plaintext to a different reader set and defeat the
    /// at-rest encryption — so this records WHICH compensation fields carry a value, and (for an override)
    /// which ones MOVED, never what the value is.
    /// </summary>
    private static object AuditPayload(RecommendationAuditState s, IReadOnlyList<string>? changedFields = null) => new
    {
        status = s.Status,
        type = s.Type,
        isAutoGenerated = s.IsAutoGenerated,
        targetGrade = s.TargetGrade,
        targetTitle = s.TargetTitle,
        effectiveDate = s.EffectiveDate,
        trainingCourse = s.TrainingCourse,
        customTypeLabel = s.CustomTypeLabel,
        budgetId = s.BudgetId,
        justification = s.Justification,
        compensationFields = CompensationFieldsSet(s),
        changedFields,
    };

    /// <summary>Names — never values — of the compensation fields that carry a figure.</summary>
    private static string[] CompensationFieldsSet(RecommendationAuditState s)
    {
        var fields = new List<string>(4);
        if (s.BonusAmount is not null) fields.Add("bonusAmount");
        if (s.BonusPercent is not null) fields.Add("bonusPercent");
        if (s.IncrementAmount is not null) fields.Add("incrementAmount");
        if (s.IncrementPercent is not null) fields.Add("incrementPercent");
        return [.. fields];
    }

    /// <summary>
    /// Which fields an override actually changed. A compensation field is reported BY NAME when its figure
    /// moved — "X changed the increment on this recommendation" without leaking "from 5000 to 9000".
    /// </summary>
    private static string[] ChangedFields(RecommendationAuditState b, RecommendationAuditState a)
    {
        var changed = new List<string>();
        if (b.Type != a.Type) changed.Add("type");
        if (b.TargetGrade != a.TargetGrade) changed.Add("targetGrade");
        if (b.TargetTitle != a.TargetTitle) changed.Add("targetTitle");
        if (b.EffectiveDate != a.EffectiveDate) changed.Add("effectiveDate");
        if (b.TrainingCourse != a.TrainingCourse) changed.Add("trainingCourse");
        if (b.CustomTypeLabel != a.CustomTypeLabel) changed.Add("customTypeLabel");
        if (b.BudgetId != a.BudgetId) changed.Add("budgetId");
        if (b.Justification != a.Justification) changed.Add("justification");
        if (b.BonusAmount != a.BonusAmount) changed.Add("bonusAmount");
        if (b.BonusPercent != a.BonusPercent) changed.Add("bonusPercent");
        if (b.IncrementAmount != a.IncrementAmount) changed.Add("incrementAmount");
        if (b.IncrementPercent != a.IncrementPercent) changed.Add("incrementPercent");
        return [.. changed];
    }

    private async Task<Result<RecommendationDto>?> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<RecommendationDto>.Failure(
                "This recommendation was modified by another session. Reload and try again.", 409, "concurrency_conflict");
        }
    }

    private async Task<Result<RecommendationDto>> ReloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var rec = await LoadRecommendationsQuery().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rec is null)
            return Result<RecommendationDto>.Failure("Recommendation not found.", 404, "recommendation_not_found");
        return Result<RecommendationDto>.Success(await BuildDtoWithLookupsAsync(rec, cancellationToken));
    }

    private async Task<RecommendationDto> BuildDtoWithLookupsAsync(Recommendation rec, CancellationToken ct)
    {
        var employee = await _dbContext.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == rec.EmployeeId, ct);
        var review = rec.ManagerReviewId is { } mrId
            ? await _dbContext.ManagerReviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == mrId, ct)
            : null;
        var nameLookup = await EmployeeNameLookupAsync(ApproverEmployeeIds(rec), ct);
        // BUG-533: unchanged behaviour, stated explicitly. Callers are GetAsync — which has ALREADY refused a
        // caller without Payroll.ViewCompensation with a 403, so CanSeeCompensation is provably true here — and
        // ReloadAsync, the echo of a write the caller just performed. Narrowing the write-path echo is a
        // separate decision (it would change what an approver sees after deciding), not part of BUG-533.
        return BuildDto(rec, employee, review, nameLookup, includeCompensation: true);
    }

    private static IEnumerable<Guid> ApproverEmployeeIds(Recommendation rec) =>
        rec.Approvers.Select(a => a.ApproverEmployeeId);

    private async Task<Dictionary<Guid, Employee>> EmployeeNameLookupAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<Guid, Employee>();
        return await _dbContext.Employees.AsNoTracking()
            .Where(e => idList.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);
    }

    /// <summary>
    /// Projects a <see cref="Recommendation"/> onto its DTO.
    ///
    /// BUG-533: <paramref name="includeCompensation"/> is NOT optional on purpose. This projection is reached by
    /// the audited reveal path (<see cref="GetAsync"/>, which 403s without <c>Payroll.ViewCompensation</c>) AND by
    /// the workspace / auto-generate paths, which are gated only on <c>Performance.Publish.All</c> or
    /// <c>Performance.Review.Team</c>. An HR Officer (Publish.All, no ViewCompensation) therefore read the whole
    /// org's bonus and increment figures, and any line manager read their reports', simply by calling the
    /// workspace instead of the detail endpoint. Every caller must now state its answer; there is no fail-open
    /// default to inherit by accident.
    ///
    /// The fields are NULLED, not omitted: the DTO shape stays byte-identical, so no OpenAPI regeneration and no
    /// frontend change is needed, and <c>RecommendationWorkspaceDto.CompensationVisible</c> already carries the
    /// signal the UI needs. <see cref="RecommendationDto.BudgetCharge"/> is masked with them because
    /// <see cref="Recommendation.BudgetCharge"/> is <c>BonusAmount ?? IncrementAmount ?? 0m</c> — leaving it
    /// would re-expose the exact figure the other five just hid.
    /// </summary>
    private RecommendationDto BuildDto(
        Recommendation rec, Employee? employee, ManagerReview? review, Dictionary<Guid, Employee> nameLookup,
        bool includeCompensation)
    {
        return new RecommendationDto
        {
            Id = rec.Id,
            EmployeeId = rec.EmployeeId,
            EmployeeName = employee is null ? string.Empty : FullName(employee),
            EmployeeNo = employee?.EmployeeNo ?? string.Empty,
            CycleId = rec.CycleId,
            ManagerReviewId = rec.ManagerReviewId,
            Type = rec.Type,
            TypeName = rec.Type.ToString(),
            Status = rec.Status,
            StatusName = rec.Status.ToString(),
            IsAutoGenerated = rec.IsAutoGenerated,
            CurrentGrade = rec.CurrentGrade,
            TargetGrade = rec.TargetGrade,
            CurrentTitle = rec.CurrentTitle,
            TargetTitle = rec.TargetTitle,
            EffectiveDate = rec.EffectiveDate,
            // BUG-533: the five NFR-3 sensitive figures. Null unless the caller holds Payroll.ViewCompensation.
            CurrentCompensation = includeCompensation ? rec.CurrentCompensation : null,
            BonusAmount = includeCompensation ? rec.BonusAmount : null,
            BonusPercent = includeCompensation ? rec.BonusPercent : null,
            IncrementAmount = includeCompensation ? rec.IncrementAmount : null,
            IncrementPercent = includeCompensation ? rec.IncrementPercent : null,
            TrainingCourse = rec.TrainingCourse,
            CustomTypeLabel = rec.CustomTypeLabel,
            Justification = rec.Justification,
            AutoGenerationRationale = rec.AutoGenerationRationale,
            BudgetId = rec.BudgetId,
            // BUG-533: Recommendation.BudgetCharge is `BonusAmount ?? IncrementAmount ?? 0m` — an exact copy of a
            // figure masked above, so it has to be masked with them or the mask is cosmetic. Zeroed rather than
            // made nullable: `decimal` keeps the wire shape identical (no OpenAPI regeneration).
            BudgetCharge = includeCompensation ? rec.BudgetCharge : 0m,
            SubmittedAt = rec.SubmittedAt,
            DecidedAt = rec.DecidedAt,
            FinalScore = review?.FinalScore,
            ManagerFlag = review?.Flag ?? ReviewFlag.None,
            ManagerFlagName = (review?.Flag ?? ReviewFlag.None).ToString(),
            Approvers = rec.Approvers.OrderBy(a => a.StepOrder).Select(a => new RecommendationApproverDto
            {
                Id = a.Id,
                ApproverEmployeeId = a.ApproverEmployeeId,
                ApproverName = nameLookup.TryGetValue(a.ApproverEmployeeId, out var emp) ? FullName(emp) : string.Empty,
                StepOrder = a.StepOrder,
                Decision = a.Decision,
                DecisionName = a.Decision.ToString(),
                DecidedAt = a.DecidedAt,
                Comment = a.Comment,
            }).ToList(),
            Events = rec.Events.Where(e => !e.IsDeleted).OrderBy(e => e.OccurredAt).Select(e => new RecommendationEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                EventTypeName = e.EventType.ToString(),
                ActorName = e.ActorName,
                OccurredAt = e.OccurredAt,
                Detail = e.Detail,
            }).ToList(),
        };
    }

    private static RecommendationBudgetDto BuildBudgetDto(RecommendationBudget b) => new()
    {
        Id = b.Id,
        CycleId = b.CycleId,
        Name = b.Name,
        AllocatedAmount = b.AllocatedAmount,
        ConsumedAmount = b.ConsumedAmount,
        RemainingAmount = b.RemainingAmount,
        Currency = b.Currency,
        IsExceeded = b.IsExceeded,
    };

    private static RecommendationRuleDto BuildRuleDto(RecommendationRule r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        MinFinalScore = r.MinFinalScore,
        RecommendedType = r.RecommendedType,
        RecommendedTypeName = r.RecommendedType.ToString(),
        DefaultBonusPercent = r.DefaultBonusPercent,
        DefaultIncrementPercent = r.DefaultIncrementPercent,
        IsActive = r.IsActive,
    };

    private async Task<AppraisalCycle?> ResolveCycleAsync(Guid? cycleId, CancellationToken ct)
    {
        if (cycleId is { } id)
            return await _dbContext.AppraisalCycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        // Default to the most recently started cycle (same convention as the dashboard).
        return await _dbContext.AppraisalCycles.AsNoTracking()
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Dictionary<Guid, string>> DepartmentNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<Guid, string>();
        return await _dbContext.Departments.AsNoTracking()
            .Where(d => idList.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);
    }

    private async Task<Dictionary<Guid, string>> JobTitleNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<Guid, string>();
        return await _dbContext.JobTitles.AsNoTracking()
            .Where(j => idList.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.TitleName, ct);
    }

    private async Task<Dictionary<Guid, string?>> GradeByTitleAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<Guid, string?>();
        var rows = await _dbContext.JobTitles.AsNoTracking()
            .Where(j => idList.Contains(j.Id))
            .Select(j => new { j.Id, j.GradeId })
            .ToListAsync(ct);
        // There is no Grade entity; surface the grade FK as a string for the comparison view (FR-5).
        return rows.ToDictionary(r => r.Id, r => (string?)(r.GradeId?.ToString()));
    }

    private static int TenureMonths(DateTime doj, DateTime now)
    {
        var months = ((now.Year - doj.Year) * 12) + now.Month - doj.Month;
        if (now.Day < doj.Day) months--;
        return Math.Max(0, months);
    }

    private static string IntegrationTarget(RecommendationType type) => type switch
    {
        RecommendationType.Promotion => "core-hr",
        RecommendationType.LateralMove => "core-hr",
        RecommendationType.Increment => "payroll",
        RecommendationType.Bonus => "payroll",
        RecommendationType.TrainingNomination => "training",
        RecommendationType.PipReferral => "performance-pip",
        _ => "custom",
    };

    private async Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    private static string FullName(Employee e) => $"{e.FirstName} {e.LastName}".Trim();
    private static string? SignerDisplayName(Employee? e) => e is null ? null : FullName(e);
    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── Export rendering (FR-6 — reuse the US-PRF-007 ClosedXML/CSV approach; PDF deferred) ──

    private static byte[] RenderCsv(RecommendationSummaryDto s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Recommendation Summary");
        sb.AppendLine($"Cycle,{Csv(s.CycleName)}");
        sb.AppendLine($"Total recommendations,{s.TotalRecommendations}");
        sb.AppendLine($"Total promotions,{s.TotalPromotions}");
        sb.AppendLine($"Total bonus pool allocated,{s.TotalBonusPoolAllocated.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Total increment allocated,{s.TotalIncrementAllocated.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Total training nominations,{s.TotalTrainingNominations}");
        sb.AppendLine();
        sb.AppendLine("Status,Count");
        foreach (var row in s.ByStatus)
            sb.AppendLine($"{Csv(row.StatusName)},{row.Count}");
        sb.AppendLine();
        sb.AppendLine("Department,Increment recommendations,Total increment amount");
        foreach (var row in s.IncrementByDepartment)
            sb.AppendLine($"{Csv(row.DepartmentName)},{row.RecommendationCount},{row.TotalIncrementAmount.ToString(CultureInfo.InvariantCulture)}");
        if (s.PreviousCycle is { } p)
        {
            sb.AppendLine();
            sb.AppendLine("Previous cycle comparison");
            sb.AppendLine($"Cycle,{Csv(p.CycleName)}");
            sb.AppendLine($"Promotions,{p.TotalPromotions}");
            sb.AppendLine($"Bonus pool,{p.TotalBonusPoolAllocated.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Increment,{p.TotalIncrementAllocated.ToString(CultureInfo.InvariantCulture)}");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] RenderXlsx(RecommendationSummaryDto s)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Summary");
        var r = 1;
        ws.Cell(r, 1).Value = "Recommendation Summary"; r += 2;
        ws.Cell(r, 1).Value = "Cycle"; ws.Cell(r, 2).Value = s.CycleName; r++;
        ws.Cell(r, 1).Value = "Total recommendations"; ws.Cell(r, 2).Value = s.TotalRecommendations; r++;
        ws.Cell(r, 1).Value = "Total promotions"; ws.Cell(r, 2).Value = s.TotalPromotions; r++;
        ws.Cell(r, 1).Value = "Total bonus pool allocated"; ws.Cell(r, 2).Value = s.TotalBonusPoolAllocated; r++;
        ws.Cell(r, 1).Value = "Total increment allocated"; ws.Cell(r, 2).Value = s.TotalIncrementAllocated; r++;
        ws.Cell(r, 1).Value = "Total training nominations"; ws.Cell(r, 2).Value = s.TotalTrainingNominations; r += 2;

        ws.Cell(r, 1).Value = "Status"; ws.Cell(r, 2).Value = "Count"; r++;
        foreach (var row in s.ByStatus)
        {
            ws.Cell(r, 1).Value = row.StatusName; ws.Cell(r, 2).Value = row.Count; r++;
        }
        r++;

        var dept = workbook.Worksheets.Add("Increment by department");
        dept.Cell(1, 1).Value = "Department";
        dept.Cell(1, 2).Value = "Increment recommendations";
        dept.Cell(1, 3).Value = "Total increment amount";
        var dr = 2;
        foreach (var row in s.IncrementByDepartment)
        {
            dept.Cell(dr, 1).Value = row.DepartmentName;
            dept.Cell(dr, 2).Value = row.RecommendationCount;
            dept.Cell(dr, 3).Value = row.TotalIncrementAmount;
            dr++;
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
