using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Benefits.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Benefits;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Benefit eligibility-rule + enrollment service (US-TRN-003). All queries are tenant-scoped via
/// <see cref="ITenantContext"/> and the EF global query filters (AC-9). Eligibility itself is delegated to the pure
/// <see cref="BenefitEligibilityEvaluator"/>. Enrollment enforces, in order: plan Active (409 plan_not_active) →
/// effective + open-enrollment window (422 enrollment_window_closed) → eligibility (422 not_eligible + failing-rule
/// reason) → no duplicate active enrollment (409 already_enrolled), the last also backstopped by a partial unique
/// index (BR-3). Every mutation is audited (FR-10) and a post-persist notification is dispatched (FR-9, best-effort).
/// </summary>
public sealed class BenefitEnrollmentService : IBenefitEnrollmentService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<BenefitEnrollmentService> _logger;

    // US-NTF-006: notification intents accumulated during a mutation and flushed only AFTER the state is committed.
    // Optional dispatcher so the unit/Postgres harnesses that construct the service directly need no wiring.
    private readonly INotificationDispatcher? _dispatcher;
    private readonly List<PendingNotification> _pending = new();

    private sealed record PendingNotification(
        Guid? UserId, string EventKey, string PlanName, string EmployeeFirst, string EmployeeLast, string Status);

    public BenefitEnrollmentService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<BenefitEnrollmentService> logger,
        INotificationDispatcher? dispatcher = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
        _dispatcher = dispatcher;
    }

    private bool HasManage => _currentUser.Permissions.Contains(PermissionCatalog.Benefits.Manage);

    private bool HasViewAll =>
        _currentUser.Permissions.Contains(PermissionCatalog.Benefits.ViewAll) || HasManage;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ── Eligibility rules (Manage) ──────────────────────────────────────────────

    public async Task<Result<EligibilityRuleDto>> AddRuleAsync(
        Guid planId, CreateEligibilityRuleRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EligibilityRuleDto>.Failure("Tenant context is not resolved.", 400);

        var plan = await _db.BenefitPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
            return Result<EligibilityRuleDto>.Failure("Benefit plan not found.", 404);

        if (!Enum.TryParse<EligibilityAttribute>(request.Attribute?.Trim(), true, out var attribute))
            return Result<EligibilityRuleDto>.Failure("Invalid eligibility attribute.", 400, "invalid_rule");

        var op = (request.Operator ?? string.Empty).Trim();
        var value = (request.Value ?? string.Empty).Trim();

        var ruleError = BenefitEligibilityEvaluator.ValidateRule(attribute, op, value);
        if (ruleError is not null)
            return Result<EligibilityRuleDto>.Failure(ruleError, 400, "invalid_rule");

        var rule = new BenefitEligibilityRule
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            BenefitPlanId = planId,
            Attribute = attribute,
            Operator = op,
            Value = value,
            IsDeleted = false,
        };

        _db.BenefitEligibilityRules.Add(rule);
        AddAudit("Benefits.EligibilityRuleAdded", rule.Id,
            $"Eligibility rule '{attribute} {op} {value}' added to plan '{plan.Name}' ({planId}).");
        await _db.SaveChangesAsync(cancellationToken);

        return Result<EligibilityRuleDto>.Success(ToRuleDto(rule));
    }

    public async Task<Result<IReadOnlyList<EligibilityRuleDto>>> GetRulesAsync(
        Guid planId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<EligibilityRuleDto>>.Failure("Tenant context is not resolved.", 400);

        var planExists = await _db.BenefitPlans.AsNoTracking().AnyAsync(p => p.Id == planId, cancellationToken);
        if (!planExists)
            return Result<IReadOnlyList<EligibilityRuleDto>>.Failure("Benefit plan not found.", 404);

        var rules = await _db.BenefitEligibilityRules.AsNoTracking()
            .Where(r => r.BenefitPlanId == planId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EligibilityRuleDto>>.Success(rules.Select(ToRuleDto).ToList());
    }

    public async Task<Result<bool>> DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<bool>.Failure("Tenant context is not resolved.", 400);

        var rule = await _db.BenefitEligibilityRules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
            return Result<bool>.Failure("Eligibility rule not found.", 404);

        rule.IsDeleted = true;
        AddAudit("Benefits.EligibilityRuleRemoved", rule.Id,
            $"Eligibility rule '{rule.Attribute} {rule.Operator} {rule.Value}' removed from plan {rule.BenefitPlanId}.");
        await _db.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }

    // ── Eligible plans ──────────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<EligiblePlanDto>>> GetMyEligiblePlansAsync(CancellationToken cancellationToken = default)
    {
        var self = await GetSelfEmployeeAsync(cancellationToken);
        if (self is null)
            return Result<IReadOnlyList<EligiblePlanDto>>.Failure("No employee record is linked to the current user.", 404, "no_employee");
        return await EligiblePlansForAsync(self, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<EligiblePlanDto>>> GetEligiblePlansAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<EligiblePlanDto>>.Failure("Tenant context is not resolved.", 400);

        var self = await GetSelfEmployeeAsync(cancellationToken);
        if (self?.Id != employeeId && !HasViewAll)
            return Result<IReadOnlyList<EligiblePlanDto>>.Failure("You are not allowed to view this employee's eligible plans.", 403, "forbidden");

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<EligiblePlanDto>>.Failure("Employee not found.", 404);

        return await EligiblePlansForAsync(employee, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<EligiblePlanDto>>> EligiblePlansForAsync(Employee employee, CancellationToken cancellationToken)
    {
        // GAP-026 / C4: an employee who cannot enrol must not be shown plans they would be refused. Listing
        // them and then rejecting the click is the same defect one screen later — and it is how HR ends up
        // believing a terminated employee is still covered.
        if (!employee.Status.CanStartNewActivity())
            return Result<IReadOnlyList<EligiblePlanDto>>.Success(Array.Empty<EligiblePlanDto>());

        var attrs = ToAttributes(employee);
        var today = Today;

        // AC-2: only Active + currently-effective + in-window plans are candidates.
        var candidatePlans = await _db.BenefitPlans.AsNoTracking()
            .Where(p => p.Status == BenefitPlanStatus.Active)
            .ToListAsync(cancellationToken);

        candidatePlans = candidatePlans
            .Where(p => IsEffective(p, today) && IsInWindow(p, today))
            .ToList();

        var planIds = candidatePlans.Select(p => p.Id).ToList();
        var rulesByPlan = (await _db.BenefitEligibilityRules.AsNoTracking()
                .Where(r => planIds.Contains(r.BenefitPlanId))
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.BenefitPlanId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var eligible = new List<EligiblePlanDto>();
        foreach (var plan in candidatePlans)
        {
            var rules = rulesByPlan.TryGetValue(plan.Id, out var r) ? r : new List<BenefitEligibilityRule>();
            var (ok, _) = BenefitEligibilityEvaluator.Evaluate(rules, attrs);
            if (ok)
                eligible.Add(ToEligiblePlanDto(plan, IsInWindow(plan, today)));
        }

        return Result<IReadOnlyList<EligiblePlanDto>>.Success(eligible);
    }

    // ── Enrollment ──────────────────────────────────────────────────────────────

    public async Task<Result<BenefitEnrollmentDto>> EnrollAsync(EnrollRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BenefitEnrollmentDto>.Failure("Tenant context is not resolved.", 400);

        if (!Enum.TryParse<CoverageLevel>(request.CoverageLevel?.Trim(), true, out var coverage))
            return Result<BenefitEnrollmentDto>.Failure("Invalid coverage level.", 400, "validation_error");

        // 1. Resolve the target employee (self, or another under Manage).
        var self = await GetSelfEmployeeAsync(cancellationToken);
        Employee? target;
        if (request.EmployeeId is { } employeeId)
        {
            if (self?.Id != employeeId && !HasManage)
                return Result<BenefitEnrollmentDto>.Failure("You are not allowed to enroll this employee.", 403, "forbidden");
            target = self?.Id == employeeId
                ? self
                : await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        }
        else
        {
            target = self;
        }

        if (target is null)
            return Result<BenefitEnrollmentDto>.Failure("Employee not found.", 404, "no_employee");

        // GAP-026 / C4: status was never consulted here, so a TERMINATED employee could be enrolled into any
        // rules-free plan — silently, no error, no log, on a benefits-cost path. Uses the shared predicate
        // rather than a fourth hand-written copy of `Terminated or Inactive`; see EmployeeStatusExtensions.
        //
        // NEW enrollments only. Existing ones are deliberately untouched: AC-7 makes ending an enrollment an
        // explicit manual act, and a validation guard must not silently terminate live benefit records as a
        // side effect of a deploy.
        if (!target.Status.CanStartNewActivity())
            return Result<BenefitEnrollmentDto>.Failure(
                "This employee's employment status does not allow new benefit enrollments.",
                422, "employee_not_enrollable");

        // 2. Plan must exist + be Active.
        var plan = await _db.BenefitPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);
        if (plan is null)
            return Result<BenefitEnrollmentDto>.Failure("Benefit plan not found.", 404);
        if (plan.Status != BenefitPlanStatus.Active)
            return Result<BenefitEnrollmentDto>.Failure("This benefit plan is not open for enrollment.", 409, "plan_not_active");

        // 3. Effective + open-enrollment window.
        var today = Today;
        if (!IsEffective(plan, today) || !IsInWindow(plan, today))
            return Result<BenefitEnrollmentDto>.Failure("The enrollment window for this plan is closed.", 422, "enrollment_window_closed");

        // 4. Eligibility.
        var rules = await _db.BenefitEligibilityRules.AsNoTracking()
            .Where(r => r.BenefitPlanId == plan.Id)
            .ToListAsync(cancellationToken);
        var (eligible, reason) = BenefitEligibilityEvaluator.Evaluate(rules, ToAttributes(target));
        if (!eligible)
            return Result<BenefitEnrollmentDto>.Failure(reason ?? "You are not eligible for this plan.", 422, "not_eligible");

        // 5. No existing active enrollment.
        var duplicate = await _db.BenefitEnrollments.AsNoTracking().AnyAsync(
            e => e.BenefitPlanId == plan.Id && e.EmployeeId == target.Id && e.Status == BenefitEnrollmentStatus.Active,
            cancellationToken);
        if (duplicate)
            return Result<BenefitEnrollmentDto>.Failure("You are already enrolled in this plan.", 409, "already_enrolled");

        // 6. Create (effective today, or the plan's future effective-from).
        var effectiveDate = plan.EffectiveFrom > today ? plan.EffectiveFrom : today;
        var enrollment = new BenefitEnrollment
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            BenefitPlanId = plan.Id,
            EmployeeId = target.Id,
            Status = BenefitEnrollmentStatus.Active,
            CoverageLevel = coverage,
            EffectiveDate = effectiveDate,
            ElectedAt = DateTime.UtcNow,
            ElectedBy = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty,
            IsDeleted = false,
        };

        _db.BenefitEnrollments.Add(enrollment);
        AddAudit("Benefits.Enrolled", enrollment.Id,
            $"Employee {target.Id} enrolled in plan '{plan.Name}' ({plan.Id}), coverage {coverage}, effective {effectiveDate}.");

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // The partial unique index backstops a concurrent duplicate insert (BR-3/NFR-3).
            _logger.LogWarning(ex, "Concurrent duplicate benefit enrollment for plan {PlanId}, employee {EmployeeId}.",
                plan.Id, target.Id);
            return Result<BenefitEnrollmentDto>.Failure("You are already enrolled in this plan.", 409, "already_enrolled");
        }

        QueueNotification("benefit_enrolled", target, plan.Name, enrollment.Status);
        await FlushNotificationsAsync(cancellationToken);

        _logger.LogInformation("Benefit enrollment created. Id={EnrollmentId}, Plan={PlanId}, Employee={EmployeeId}, By={User}",
            enrollment.Id, plan.Id, target.Id, _currentUser.Email);

        return Result<BenefitEnrollmentDto>.Success(ToEnrollmentDto(enrollment, plan.Name, target));
    }

    public async Task<Result<BenefitEnrollmentDto>> TerminateAsync(
        Guid enrollmentId, TerminateEnrollmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BenefitEnrollmentDto>.Failure("Tenant context is not resolved.", 400);

        var enrollment = await _db.BenefitEnrollments.FirstOrDefaultAsync(e => e.Id == enrollmentId, cancellationToken);
        if (enrollment is null)
            return Result<BenefitEnrollmentDto>.Failure("Enrollment not found.", 404);

        // Own or Manage (AC-7).
        var self = await GetSelfEmployeeAsync(cancellationToken);
        if (self?.Id != enrollment.EmployeeId && !HasManage)
            return Result<BenefitEnrollmentDto>.Failure("You are not allowed to terminate this enrollment.", 403, "forbidden");

        if (enrollment.Status == BenefitEnrollmentStatus.Terminated)
            return Result<BenefitEnrollmentDto>.Failure("This enrollment is already terminated.", 409, "already_terminated");

        var endDate = request.EndDate ?? Today;
        enrollment.Status = BenefitEnrollmentStatus.Terminated;
        enrollment.EndDate = endDate;

        var plan = await _db.BenefitPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == enrollment.BenefitPlanId, cancellationToken);
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == enrollment.EmployeeId, cancellationToken);

        AddAudit("Benefits.EnrollmentTerminated", enrollment.Id,
            $"Enrollment {enrollment.Id} (plan {enrollment.BenefitPlanId}, employee {enrollment.EmployeeId}) terminated, end date {endDate}.");
        await _db.SaveChangesAsync(cancellationToken);

        if (employee is not null)
        {
            QueueNotification("benefit_terminated", employee, plan?.Name ?? "your plan", enrollment.Status);
            await FlushNotificationsAsync(cancellationToken);
        }

        return Result<BenefitEnrollmentDto>.Success(ToEnrollmentDto(enrollment, plan?.Name ?? string.Empty, employee));
    }

    // ── Enrollment reads ────────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<BenefitEnrollmentDto>>> GetMyEnrollmentsAsync(CancellationToken cancellationToken = default)
    {
        var self = await GetSelfEmployeeAsync(cancellationToken);
        if (self is null)
            return Result<IReadOnlyList<BenefitEnrollmentDto>>.Failure("No employee record is linked to the current user.", 404, "no_employee");
        return await EnrollmentsForAsync(self.Id, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<BenefitEnrollmentDto>>> GetEmployeeEnrollmentsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<BenefitEnrollmentDto>>.Failure("Tenant context is not resolved.", 400);

        var self = await GetSelfEmployeeAsync(cancellationToken);
        if (self?.Id != employeeId && !HasViewAll)
            return Result<IReadOnlyList<BenefitEnrollmentDto>>.Failure("You are not allowed to view this employee's enrollments.", 403, "forbidden");

        return await EnrollmentsForAsync(employeeId, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<BenefitEnrollmentDto>>> EnrollmentsForAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var rows = await (
            from e in _db.BenefitEnrollments.AsNoTracking().Where(e => e.EmployeeId == employeeId)
            join p in _db.BenefitPlans.AsNoTracking() on e.BenefitPlanId equals p.Id into pj
            from p in pj.DefaultIfEmpty()
            join emp in _db.Employees.AsNoTracking() on e.EmployeeId equals emp.Id into ej
            from emp in ej.DefaultIfEmpty()
            orderby e.CreatedAt descending
            select new { e, PlanName = p != null ? p.Name : string.Empty, First = emp != null ? emp.FirstName : "", Last = emp != null ? emp.LastName : "" })
            .ToListAsync(cancellationToken);

        var dtos = rows.Select(x => ToEnrollmentDto(x.e, x.PlanName, x.First, x.Last)).ToList();
        return Result<IReadOnlyList<BenefitEnrollmentDto>>.Success(dtos);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private Task<Employee?> GetSelfEmployeeAsync(CancellationToken cancellationToken) =>
        _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    private static EmployeeAttributes ToAttributes(Employee e)
    {
        var joined = DateOnly.FromDateTime(e.DateOfJoining);
        var tenureDays = Today.DayNumber - joined.DayNumber;
        return new EmployeeAttributes(e.EmploymentType, tenureDays, e.DepartmentId, e.JobTitleId);
    }

    private static bool IsEffective(BenefitPlan plan, DateOnly today) =>
        plan.EffectiveFrom <= today && (plan.EffectiveTo is not { } to || today <= to);

    private static bool IsInWindow(BenefitPlan plan, DateOnly today) =>
        (plan.EnrollmentOpensAt is not { } opens || opens <= today)
        && (plan.EnrollmentClosesAt is not { } closes || today <= closes);

    private static EligibilityRuleDto ToRuleDto(BenefitEligibilityRule r) => new()
    {
        Id = r.Id,
        BenefitPlanId = r.BenefitPlanId,
        Attribute = r.Attribute.ToString(),
        Operator = r.Operator,
        Value = r.Value,
    };

    private static EligiblePlanDto ToEligiblePlanDto(BenefitPlan p, bool enrollmentOpen) => new()
    {
        PlanId = p.Id,
        Name = p.Name,
        Type = p.Type.ToString(),
        Currency = p.Currency,
        EmployeeCost = p.EmployeeCost,
        EmployerCost = p.EmployerCost,
        EffectiveFrom = p.EffectiveFrom,
        EffectiveTo = p.EffectiveTo,
        EnrollmentOpen = enrollmentOpen,
    };

    private static BenefitEnrollmentDto ToEnrollmentDto(BenefitEnrollment e, string planName, Employee? emp) =>
        ToEnrollmentDto(e, planName, emp?.FirstName ?? string.Empty, emp?.LastName ?? string.Empty);

    private static BenefitEnrollmentDto ToEnrollmentDto(BenefitEnrollment e, string planName, string first, string last) => new()
    {
        Id = e.Id,
        PlanId = e.BenefitPlanId,
        PlanName = planName,
        EmployeeId = e.EmployeeId,
        EmployeeName = $"{first} {last}".Trim(),
        Status = e.Status.ToString(),
        CoverageLevel = e.CoverageLevel.ToString(),
        EffectiveDate = e.EffectiveDate,
        EndDate = e.EndDate,
        ElectedAt = e.ElectedAt,
    };

    // ── Audit (FR-10) ───────────────────────────────────────────────────────────

    private void AddAudit(string action, Guid resourceId, string detail)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "BenefitEnrollment",
            ResourceId = resourceId.ToString(),
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    // ── Notifications (US-NTF-006, FR-9) ─────────────────────────────────────────

    private void QueueNotification(string eventKey, Employee employee, string planName, BenefitEnrollmentStatus status)
    {
        // Skip employees with no linked user (nothing to notify — email address is unknown too).
        if (employee.UserId is not { } userId || userId == Guid.Empty)
            return;
        _pending.Add(new PendingNotification(
            userId, eventKey, planName, employee.FirstName, employee.LastName, status.ToString()));
    }

    private async Task FlushNotificationsAsync(CancellationToken cancellationToken)
    {
        if (_dispatcher is null || _pending.Count == 0)
        {
            _pending.Clear();
            return;
        }

        var intents = _pending.ToList();
        _pending.Clear();

        foreach (var intent in intents)
        {
            try
            {
                var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["plan"] = new Dictionary<string, object?> { ["name"] = intent.PlanName },
                    ["employee"] = new Dictionary<string, object?>
                    {
                        ["firstName"] = intent.EmployeeFirst,
                        ["lastName"] = intent.EmployeeLast,
                    },
                    ["enrollment"] = new Dictionary<string, object?> { ["status"] = intent.Status },
                });
                var request = new NotificationRequest(
                    TenantId: _tenantContext.TenantId,
                    EventKey: intent.EventKey,
                    PayloadJson: payloadJson,
                    RecipientUserId: intent.UserId,
                    RecipientEmail: null,
                    NotificationType: null);

                await _dispatcher.SendInAppAsync(request, cancellationToken);
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Benefit notification {EventKey} for user {UserId} failed (non-fatal).",
                    intent.EventKey, intent.UserId);
            }
        }
    }
}
