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
    private readonly IRecommendationIntegrationService _integration;
    private readonly IPayrollAuditLogger _auditLogger;
    private readonly ILogger<RecommendationService> _logger;

    private const int MaxPageSize = 200;

    // BUG-083: the compensation fields GetAsync decrypts + returns (P3-4 AES-at-rest columns). NAMES ONLY —
    // recorded in the sensitive-reveal audit's After payload so no monetary values reach the audit trail.
    private static readonly string[] CompensationRevealFields =
        ["currentCompensation", "incrementAmount", "incrementPercent", "bonusAmount", "bonusPercent"];

    public RecommendationService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IRecommendationIntegrationService integration,
        IPayrollAuditLogger auditLogger,
        ILogger<RecommendationService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _integration = integration;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    private bool IsHr => _currentUser.Permissions.Contains(PermissionCatalog.Performance.PublishAll);
    private bool IsManager => _currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewTeam);

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
                CurrentCompensation = null, // NFR-3 seam: compensation lives in Payroll; not joined here.
                TenureMonths = TenureMonths(e.DateOfJoining, now),
                FinalScore = review?.FinalScore,
                ManagerFlag = review?.Flag ?? ReviewFlag.None,
                ManagerFlagName = (review?.Flag ?? ReviewFlag.None).ToString(),
                ManagerReviewId = review?.Id,
                Recommendation = rec is null ? null : BuildDto(rec, e, review, nameLookup),
            };
        }).ToList();

        var budget = await _dbContext.RecommendationBudgets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CycleId == cycle.Id, cancellationToken);

        return Result<RecommendationWorkspaceDto>.Success(new RecommendationWorkspaceDto
        {
            CycleId = cycle.Id,
            CycleName = cycle.Name,
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

    public async Task<Result<AutoGenerateResultDto>> AutoGenerateAsync(Guid cycleId, CancellationToken cancellationToken = default)
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
            AppendEvent(rec, RecommendationEventType.Created, actor, actorName, null,
                $"Auto-generated: {rec.AutoGenerationRationale}");
            _dbContext.Recommendations.Add(rec);
            created.Add(rec);
            existing.Add(review.EmployeeId);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recommendations auto-generated. CycleId={CycleId}, Evaluated={Evaluated}, Created={Created}, " +
            "Skipped={Skipped}, TenantId={TenantId}, By={User}",
            cycleId, reviews.Count, created.Count, skipped, _tenantContext.TenantId, _currentUser.Email);

        var nameLookup = await EmployeeNameLookupAsync(created.Select(c => c.EmployeeId), cancellationToken);
        var dtos = created.Select(c => BuildDto(c, nameLookup.GetValueOrDefault(c.EmployeeId), null, nameLookup)).ToList();

        return Result<AutoGenerateResultDto>.Success(new AutoGenerateResultDto
        {
            CycleId = cycleId,
            EmployeesEvaluated = reviews.Count,
            SuggestionsCreated = created.Count,
            SuggestionsSkipped = skipped,
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
            ApplyDetails(rec, input.Type, d, employee);
            rec.Justification = string.IsNullOrWhiteSpace(input.Justification) ? null : input.Justification.Trim();
            AppendEvent(rec, RecommendationEventType.Created, actor, actorName, input.ClientIpAddress, null);
            _dbContext.Recommendations.Add(rec);
        }
        else
        {
            // FR-3: overriding an existing recommendation REQUIRES a justification.
            if (string.IsNullOrWhiteSpace(input.Justification))
                return Result<RecommendationDto>.Failure(
                    "A justification is required when overriding an existing recommendation.", 422, "justification_required");
            if (existing.Status is RecommendationStatus.Approved or RecommendationStatus.Rejected)
                return Result<RecommendationDto>.Failure(
                    "A decided recommendation can no longer be changed.", 409, "recommendation_decided");

            rec = existing;
            ApplyDetails(rec, input.Type, d, employee);
            rec.Justification = input.Justification.Trim();
            AppendEvent(rec, RecommendationEventType.Overridden, actor, actorName, input.ClientIpAddress,
                $"Type set to {input.Type}.");
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

        // BR-2: when calibration is enabled, the employee's manager review must be submitted (calibration complete).
        if (cycle.IsCalibrationEnabled)
        {
            var reviewSubmitted = await _dbContext.ManagerReviews.AsNoTracking().AnyAsync(
                r => r.CycleId == rec.CycleId && r.EmployeeId == rec.EmployeeId && r.SubmittedAt != null,
                cancellationToken);
            if (!reviewSubmitted)
                return Result<RecommendationDto>.Failure(
                    "Calibration is enabled for this cycle; recommendations can only proceed after calibration is complete.",
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
        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
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
        if (normalized is not ("csv" or "xlsx"))
            return Result<RecommendationExportResult>.Failure(
                "Export format must be one of csv, xlsx.", 400, "invalid_format");

        var cycle = await ResolveCycleAsync(cycleId, cancellationToken);
        if (cycle is null)
            return Result<RecommendationExportResult>.Failure("No appraisal cycle is available.", 404, "no_cycle");

        var summary = await BuildSummaryAsync(cycle, cancellationToken);
        var (content, fileName, contentType) = normalized == "xlsx"
            ? (RenderXlsx(summary), $"recommendations-{cycle.Name}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            : (RenderCsv(summary), $"recommendations-{cycle.Name}.csv", "text/csv");

        return Result<RecommendationExportResult>.Success(new RecommendationExportResult
        {
            FileContent = content,
            FileName = fileName,
            ContentType = contentType,
        });
    }

    private async Task<RecommendationSummaryDto> BuildSummaryAsync(AppraisalCycle cycle, CancellationToken ct)
    {
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

    private void ApplyDetails(Recommendation rec, RecommendationType type, RecommendationDetailsInput d, Employee employee)
    {
        rec.Type = type;
        rec.TargetGrade = Trim(d.TargetGrade);
        rec.TargetTitle = Trim(d.TargetTitle);
        rec.EffectiveDate = d.EffectiveDate;
        rec.BonusAmount = d.BonusAmount;
        rec.BonusPercent = d.BonusPercent;
        rec.IncrementAmount = d.IncrementAmount;
        rec.IncrementPercent = d.IncrementPercent;
        rec.TrainingCourse = Trim(d.TrainingCourse);
        rec.CustomTypeLabel = Trim(d.CustomTypeLabel);
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
        return BuildDto(rec, employee, review, nameLookup);
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

    private RecommendationDto BuildDto(
        Recommendation rec, Employee? employee, ManagerReview? review, Dictionary<Guid, Employee> nameLookup)
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
            CurrentCompensation = rec.CurrentCompensation,
            BonusAmount = rec.BonusAmount,
            BonusPercent = rec.BonusPercent,
            IncrementAmount = rec.IncrementAmount,
            IncrementPercent = rec.IncrementPercent,
            TrainingCourse = rec.TrainingCourse,
            CustomTypeLabel = rec.CustomTypeLabel,
            Justification = rec.Justification,
            AutoGenerationRationale = rec.AutoGenerationRationale,
            BudgetId = rec.BudgetId,
            BudgetCharge = rec.BudgetCharge,
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
