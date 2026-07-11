using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Benefits.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Benefit-plan administration service (US-TRN-002). All queries are tenant-scoped via <see cref="ITenantContext"/>
/// and the EF global query filters. This is a single-entity CRUD slice (no concurrency): plan create/edit/list/get,
/// the Draft/Active/Inactive/Archived state machine (AC-2/AC-3/AC-6), tenant-default-currency resolution (AC-1,
/// US-ADM-006) and before/after audit (AC-4/FR-7) live here. Only <c>Active</c> plans within their effective
/// window are enrollable — that eligibility check is enforced downstream in US-TRN-003; here we only own the state.
/// </summary>
public sealed class BenefitPlanService : IBenefitPlanService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<BenefitPlanService> _logger;

    public BenefitPlanService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<BenefitPlanService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ── Reads (AC-5) ────────────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<BenefitPlanDto>>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<BenefitPlanDto>>.Failure("Tenant context is not resolved.", 400);

        var plans = await _db.BenefitPlans.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<BenefitPlanDto>>.Success(plans.Select(ToDto).ToList());
    }

    public async Task<Result<BenefitPlanDto>> GetPlanByIdAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BenefitPlanDto>.Failure("Tenant context is not resolved.", 400);

        var plan = await _db.BenefitPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
            return Result<BenefitPlanDto>.Failure("Benefit plan not found.", 404);

        return Result<BenefitPlanDto>.Success(ToDto(plan));
    }

    // ── Writes (Manage-gated by the controller) ─────────────────────────────────

    public async Task<Result<BenefitPlanDto>> CreatePlanAsync(CreateBenefitPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BenefitPlanDto>.Failure("Tenant context is not resolved.", 400);

        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<BenefitPlanDto>.Failure("Plan name is required.", 400, "validation_error");

        if (!Enum.TryParse<BenefitType>(request.Type, true, out var type))
            return Result<BenefitPlanDto>.Failure("Invalid benefit type.", 400, "validation_error");

        var costError = ValidateCostsAndDates(
            request.EmployerCost, request.EmployeeCost, request.EffectiveFrom, request.EffectiveTo,
            request.EnrollmentOpensAt, request.EnrollmentClosesAt);
        if (costError is not null)
            return Result<BenefitPlanDto>.Failure(costError, 400, "validation_error");

        var currency = string.IsNullOrWhiteSpace(request.Currency)
            ? await ResolveTenantDefaultCurrencyAsync(cancellationToken)
            : request.Currency.Trim().ToUpperInvariant();

        var plan = new BenefitPlan
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Name = request.Name.Trim(),
            Type = type,
            Description = request.Description?.Trim(),
            CoverageDetails = request.CoverageDetails?.Trim(),
            EmployerCost = request.EmployerCost,
            EmployeeCost = request.EmployeeCost,
            Currency = currency,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            EnrollmentOpensAt = request.EnrollmentOpensAt,
            EnrollmentClosesAt = request.EnrollmentClosesAt,
            Status = BenefitPlanStatus.Draft,
            IsDeleted = false,
        };

        _db.BenefitPlans.Add(plan);
        AddAudit("Benefits.PlanCreated", plan.Id, $"Benefit plan '{plan.Name}' ({plan.Type}) created (Draft), currency {plan.Currency}.");
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Benefit plan created. Id={PlanId}, TenantId={TenantId}, By={User}",
            plan.Id, _tenantContext.TenantId, _currentUser.Email);

        return Result<BenefitPlanDto>.Success(ToDto(plan));
    }

    public async Task<Result<BenefitPlanDto>> UpdatePlanAsync(Guid planId, UpdateBenefitPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BenefitPlanDto>.Failure("Tenant context is not resolved.", 400);

        var plan = await _db.BenefitPlans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
            return Result<BenefitPlanDto>.Failure("Benefit plan not found.", 404);

        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<BenefitPlanDto>.Failure("Plan name is required.", 400, "validation_error");

        if (!Enum.TryParse<BenefitType>(request.Type, true, out var type))
            return Result<BenefitPlanDto>.Failure("Invalid benefit type.", 400, "validation_error");

        var costError = ValidateCostsAndDates(
            request.EmployerCost, request.EmployeeCost, request.EffectiveFrom, request.EffectiveTo,
            request.EnrollmentOpensAt, request.EnrollmentClosesAt);
        if (costError is not null)
            return Result<BenefitPlanDto>.Failure(costError, 400, "validation_error");

        // AC-4/FR-7: audit the before/after state of the mutated fields.
        var before =
            $"name='{plan.Name}', type={plan.Type}, employerCost={plan.EmployerCost}, employeeCost={plan.EmployeeCost}, " +
            $"currency={plan.Currency}, effectiveFrom={plan.EffectiveFrom}, effectiveTo={plan.EffectiveTo}, " +
            $"coverage='{plan.CoverageDetails}'";

        plan.Name = request.Name.Trim();
        plan.Type = type;
        plan.Description = request.Description?.Trim();
        plan.CoverageDetails = request.CoverageDetails?.Trim();
        plan.EmployerCost = request.EmployerCost;
        plan.EmployeeCost = request.EmployeeCost;
        if (!string.IsNullOrWhiteSpace(request.Currency))
            plan.Currency = request.Currency.Trim().ToUpperInvariant();
        plan.EffectiveFrom = request.EffectiveFrom;
        plan.EffectiveTo = request.EffectiveTo;
        plan.EnrollmentOpensAt = request.EnrollmentOpensAt;
        plan.EnrollmentClosesAt = request.EnrollmentClosesAt;

        var after =
            $"name='{plan.Name}', type={plan.Type}, employerCost={plan.EmployerCost}, employeeCost={plan.EmployeeCost}, " +
            $"currency={plan.Currency}, effectiveFrom={plan.EffectiveFrom}, effectiveTo={plan.EffectiveTo}, " +
            $"coverage='{plan.CoverageDetails}'";

        AddAudit("Benefits.PlanUpdated", plan.Id, $"Benefit plan '{plan.Name}' updated. Before: {{{before}}}. After: {{{after}}}.");
        await _db.SaveChangesAsync(cancellationToken);

        return Result<BenefitPlanDto>.Success(ToDto(plan));
    }

    public async Task<Result<BenefitPlanDto>> ChangeStatusAsync(Guid planId, string targetStatus, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BenefitPlanDto>.Failure("Tenant context is not resolved.", 400);

        if (!Enum.TryParse<BenefitPlanStatus>(targetStatus, true, out var target))
            return Result<BenefitPlanDto>.Failure("Invalid benefit plan status.", 400, "validation_error");

        var plan = await _db.BenefitPlans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
            return Result<BenefitPlanDto>.Failure("Benefit plan not found.", 404);

        if (!IsLegalTransition(plan.Status, target))
            return Result<BenefitPlanDto>.Failure(
                $"Cannot change benefit plan status from {plan.Status} to {target}.", 409, "invalid_status_transition");

        // AC-3/BR-3: deactivating (→Inactive) or archiving never cascades to existing enrollments (none exist
        // until US-TRN-003; when enrollments are added there this transition must remain non-mutating on them).
        var from = plan.Status;
        plan.Status = target;
        AddAudit("Benefits.PlanStatusChanged", plan.Id, $"Benefit plan '{plan.Name}' status {from} → {target}.");
        await _db.SaveChangesAsync(cancellationToken);

        return Result<BenefitPlanDto>.Success(ToDto(plan));
    }

    /// <summary>
    /// Legal benefit-plan status transitions (AC-2/AC-3/AC-6): Draft → Active/Archived; Active → Inactive/Archived;
    /// Inactive → Active/Archived; Archived is terminal.
    /// </summary>
    internal static bool IsLegalTransition(BenefitPlanStatus from, BenefitPlanStatus to) => from switch
    {
        BenefitPlanStatus.Draft => to is BenefitPlanStatus.Active or BenefitPlanStatus.Archived,
        BenefitPlanStatus.Active => to is BenefitPlanStatus.Inactive or BenefitPlanStatus.Archived,
        BenefitPlanStatus.Inactive => to is BenefitPlanStatus.Active or BenefitPlanStatus.Archived,
        _ => false, // Archived is terminal.
    };

    // NOTE (US-TRN-003 seam): a hard-delete endpoint is intentionally OMITTED in TRN-002 — the story exposes only
    // GET/POST/PUT/status, and BR-4/AC-6 require "archive, don't delete" once enrollments exist. Archival is the
    // status transition (→ Archived) above. When US-TRN-003 adds BenefitEnrollment, wire the enrollment-count guard
    // there: if it later introduces a delete path, block it when >=1 enrollment references the plan.

    // ── Currency (AC-1, US-ADM-006) ─────────────────────────────────────────────

    /// <summary>
    /// Resolves the tenant's default currency for a plan whose create request left <c>Currency</c> blank.
    /// The tenant default currency is stored on <see cref="Tenant.Currency"/> and maintained via US-ADM-006
    /// (TenantSettingsService company settings). Falls back to "USD" (with a warning) only if the tenant row is
    /// missing or its currency is blank — which should not happen since Tenant.Currency defaults to "USD".
    /// </summary>
    private async Task<string> ResolveTenantDefaultCurrencyAsync(CancellationToken cancellationToken)
    {
        var currency = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(currency))
            return currency.Trim().ToUpperInvariant();

        _logger.LogWarning(
            "Tenant {TenantId} has no default currency; falling back to USD for a new benefit plan.",
            _tenantContext.TenantId);
        return "USD";
    }

    // ── Validation helper ───────────────────────────────────────────────────────

    private static string? ValidateCostsAndDates(
        decimal? employerCost, decimal? employeeCost, DateOnly effectiveFrom, DateOnly? effectiveTo,
        DateOnly? enrollmentOpensAt, DateOnly? enrollmentClosesAt)
    {
        if (employerCost is < 0)
            return "Employer cost must be zero or a positive amount.";
        if (employeeCost is < 0)
            return "Employee cost must be zero or a positive amount.";
        if (effectiveTo is { } to && to < effectiveFrom)
            return "Effective-to date must be on or after the effective-from date.";
        if (enrollmentOpensAt is { } opens && enrollmentClosesAt is { } closes && closes < opens)
            return "Enrollment-closes date must be on or after the enrollment-opens date.";
        return null;
    }

    // ── Mapping ─────────────────────────────────────────────────────────────────

    private static BenefitPlanDto ToDto(BenefitPlan p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Type = p.Type.ToString(),
        Description = p.Description,
        CoverageDetails = p.CoverageDetails,
        EmployerCost = p.EmployerCost,
        EmployeeCost = p.EmployeeCost,
        Currency = p.Currency,
        EffectiveFrom = p.EffectiveFrom,
        EffectiveTo = p.EffectiveTo,
        EnrollmentOpensAt = p.EnrollmentOpensAt,
        EnrollmentClosesAt = p.EnrollmentClosesAt,
        Status = p.Status.ToString(),
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };

    // ── Audit (FR-7) ────────────────────────────────────────────────────────────

    private void AddAudit(string action, Guid resourceId, string detail)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "BenefitPlan",
            ResourceId = resourceId.ToString(),
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }
}
