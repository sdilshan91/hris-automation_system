using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Overtime tracking and approval (US-ATT-006). All queries are tenant-scoped via ITenantContext + the
/// EF global query filters (NFR-2 asks for PostgreSQL RLS, which this codebase does NOT use). Mirrors
/// the single-level direct-report approval pattern of <see cref="RegularizationApprovalService"/>
/// (US-ATT-004): self-approval prevention (BR-8), direct-report authorization, immutable decision
/// history.
///
/// The clock-out auto-detection path (AC-1/FR-1/NFR-1) is exposed as <see cref="BuildAutoDetectedAsync"/>
/// so <see cref="AttendanceService"/> can create the overtime record inside the SAME clock-out
/// transaction (no extra SaveChanges, no extra API call).
///
/// Deferred (flagged, not blocked): Approval Workflow Engine (FR-5, US-ADM-007) — workflow_instance_id
/// stays null; Notifications (FR-8 HR alert / manager / employee, US-NTF) — TODO, not stubbed; Payroll
/// (FR-7, US-ATT-009) — approval sets IsPayrollReady only.
/// </summary>
public sealed class OvertimeService : IOvertimeService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<OvertimeService> _logger;

    public OvertimeService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<OvertimeService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-1 / FR-1 / NFR-1 — auto-detection on clock-out (called by AttendanceService)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds (but does NOT save) an <see cref="OvertimeRecord"/> for a just-closed attendance session
    /// when net work exceeds the shift standard by at least the threshold (BR-1/BR-2). Returns null when
    /// no overtime is recognized. The caller (clock-out) adds it to the same change-tracker and commits
    /// it atomically with the attendance_log (NFR-1).
    ///
    /// - Standard hours come from the employee's resolved shift (US-ATT-005), falling back to the tenant
    ///   <see cref="AttendanceSettings.StandardWorkMinutes"/> when there is no shift (AC-1 baseline).
    /// - Multiplier from weekday/weekend/holiday (BR-3/BR-7) using the tenant holiday calendar + day-of-week.
    /// - Capped at <see cref="AttendanceSettings.MaxDailyOvertimeMinutes"/> (BR-4/FR-8 — flagged if capped).
    /// - Weekly cap (BR-5/FR-8): if this record pushes the employee's week over the max, the alert flag is set.
    /// - Status PENDING, or UNAPPROVED when the tenant requires pre-approval and no matching pre-approval
    ///   exists for the date (BR-6) — UNAPPROVED is never payroll-ready.
    /// </summary>
    public async Task<OvertimeRecord?> BuildAutoDetectedAsync(
        AttendanceLog log,
        Employee employee,
        AttendanceSettings settings,
        int netWorkMinutes,
        CancellationToken cancellationToken)
    {
        // BR-1 baseline: the standard from the resolved shift, else the tenant setting.
        var date = DateOnly.FromDateTime(log.ClockIn);
        var standardMinutes = await ResolveStandardMinutesAsync(employee.Id, date, settings, cancellationToken);

        // BR-3/BR-7: multiplier basis — public holiday > weekend > weekday.
        var isHoliday = await IsPublicHolidayAsync(date, cancellationToken);
        var (multiplier, multiplierBasis) = OvertimeMultiplierResolver.Resolve(
            date, isHoliday,
            settings.WeekdayOvertimeMultiplier,
            settings.WeekendOvertimeMultiplier,
            settings.HolidayOvertimeMultiplier);

        // FR-1/BR-2/BR-4: detect + cap.
        var computation = AttendanceCalculator.CalculateOvertime(
            netWorkMinutes,
            standardMinutes,
            settings.OvertimeMinimumThresholdMinutes,
            settings.MaxDailyOvertimeMinutes,
            multiplier,
            multiplierBasis);

        if (computation.OvertimeMinutes <= 0)
            return null;   // below threshold or no excess — no overtime record (test hint: 8h20m case).

        // BR-6: when pre-approval is required, look for a matching pre-approval for the date.
        var requirePreApproval = settings.RequireOvertimePreApproval;
        var hasPreApproval = requirePreApproval && await _dbContext.OvertimeRecords
            .AnyAsync(o => o.EmployeeId == employee.Id
                        && o.Date == date
                        && o.Type == OvertimeType.PreApproved,
                      cancellationToken);

        var status = requirePreApproval && !hasPreApproval
            ? OvertimeStatus.Unapproved
            : OvertimeStatus.Pending;

        // BR-5/FR-8: weekly-cap alert — sum the employee's existing approved+pending overtime in the
        // ISO week of this date and flag if adding this block crosses the max.
        var weeklyExceeded = await EvaluateWeeklyCapAsync(
            employee.Id, date, computation.OvertimeMinutes, settings.MaxWeeklyOvertimeMinutes, cancellationToken);

        var record = new OvertimeRecord
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employee.Id,
            AttendanceLogId = log.Id,
            Date = date,
            OvertimeMinutes = computation.OvertimeMinutes,
            ApprovedMinutes = null,
            Multiplier = computation.Multiplier,
            Type = OvertimeType.AutoDetected,
            Status = status,
            Reason = "Auto-detected overtime on clock-out.",
            IsPayrollReady = false,
            DailyCapApplied = computation.CapApplied,
            WeeklyCapExceeded = weeklyExceeded,
            CalculationBasis = computation.Basis,
            WorkflowInstanceId = null,   // TODO(US-ADM-007): set when the Approval Workflow Engine lands.
        };

        if (computation.CapApplied || weeklyExceeded)
        {
            // FR-8: alert HR when daily/weekly maxima are exceeded. DEFERRED — no notification infra.
            // TODO(US-NTF): emit an HR alert. The flags above persist the condition for now.
            _logger.LogWarning(
                "Overtime cap exceeded for employee {EmployeeId} on {Date} (tenant {TenantId}): " +
                "dailyCapApplied={DailyCap}, weeklyCapExceeded={WeeklyCap}.",
                employee.Id, date, _tenantContext.TenantId, computation.CapApplied, weeklyExceeded);
        }

        return record;
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-2 / FR-4 — pre-approval submit
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<OvertimeDto>> SubmitPreApprovalAsync(
        OvertimePreApprovalRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OvertimeDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<OvertimeDto>.Failure(
                "No employee record is linked to the current user.", 403);

        if (employee.Status is EmployeeStatus.Terminated or EmployeeStatus.Inactive)
            return Result<OvertimeDto>.Failure(
                "Overtime pre-approval is not allowed for your current employment status.", 403);

        if (request.ExpectedHours <= 0)
            return Result<OvertimeDto>.Failure(
                "Expected overtime hours must be greater than zero.", 400, "invalid_hours");

        // No past dates for a pre-approval (the intent is to work overtime). "Today" is UTC, consistent
        // with the rest of the module (tenant-timezone deferred).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.Date < today)
            return Result<OvertimeDto>.Failure(
                "Overtime pre-approval cannot be submitted for a past date.", 400, "past_date");

        var expectedMinutes = (int)Math.Round(request.ExpectedHours * 60m, MidpointRounding.AwayFromZero);

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        var isHoliday = await IsPublicHolidayAsync(request.Date, cancellationToken);
        var (multiplier, multiplierBasis) = OvertimeMultiplierResolver.Resolve(
            request.Date, isHoliday,
            settings.WeekdayOvertimeMultiplier,
            settings.WeekendOvertimeMultiplier,
            settings.HolidayOvertimeMultiplier);

        var record = new OvertimeRecord
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employee.Id,
            AttendanceLogId = null,        // pre-approval precedes any clock-out (AC-2).
            Date = request.Date,
            OvertimeMinutes = expectedMinutes,
            ApprovedMinutes = null,
            Multiplier = multiplier,
            Type = OvertimeType.PreApproved,
            Status = OvertimeStatus.Pending,
            Reason = request.Reason.Trim(),
            IsPayrollReady = false,
            CalculationBasis = $"preApproval;expectedMinutes={expectedMinutes};" +
                               $"multiplier={multiplier};multiplierBasis={multiplierBasis}",
            WorkflowInstanceId = null,     // TODO(US-ADM-007): no workflow engine yet.
        };

        _dbContext.OvertimeRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-8 (notify manager of pending pre-approval): DEFERRED — no notification infra (TODO US-NTF).

        _logger.LogInformation(
            "Employee {EmployeeId} submitted overtime pre-approval {Id} for {Date} (tenant {TenantId}).",
            employee.Id, record.Id, record.Date, _tenantContext.TenantId);

        return Result<OvertimeDto>.Success(ToDto(record));
    }

    // ══════════════════════════════════════════════════════════════
    //  My overtime (employee self-list)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<OvertimeDto>>> GetMyOvertimeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<OvertimeDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken, asNoTracking: true);
        if (employee is null)
            return Result<IReadOnlyList<OvertimeDto>>.Failure(
                "No employee record is linked to the current user.", 403);

        var rows = await _dbContext.OvertimeRecords
            .AsNoTracking()
            .Where(o => o.EmployeeId == employee.Id)
            .OrderByDescending(o => o.Date)
            .ThenByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<OvertimeDto> dtos = rows.Select(ToDto).ToList();
        return Result<IReadOnlyList<OvertimeDto>>.Success(dtos);
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-3 / FR-5 — manager pending queue
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<OvertimeQueueResult>> GetPendingForManagerAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OvertimeQueueResult>.Failure("Tenant context is not resolved.", 400);

        var manager = await GetCurrentEmployeeAsync(cancellationToken, asNoTracking: true);
        if (manager is null)
            return Result<OvertimeQueueResult>.Success(new OvertimeQueueResult { Items = [], TotalCount = 0 });

        // FR-5/AC-3: direct reports only (Phase 1 — mirrors US-ATT-004 direct-report scope).
        var teamIds = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == manager.Id)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        if (teamIds.Count == 0)
            return Result<OvertimeQueueResult>.Success(new OvertimeQueueResult { Items = [], TotalCount = 0 });

        var teamSet = teamIds.ToHashSet();

        var query =
            from o in _dbContext.OvertimeRecords.AsNoTracking()
            join e in _dbContext.Employees.AsNoTracking() on o.EmployeeId equals e.Id
            where o.Status == OvertimeStatus.Pending && teamSet.Contains(o.EmployeeId)
            select new { o, e };

        var rows = await query
            .OrderBy(x => x.o.Date)
            .ThenBy(x => x.o.CreatedAt)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new OvertimeQueueItemDto
        {
            Id = x.o.Id,
            EmployeeId = x.o.EmployeeId,
            AttendanceLogId = x.o.AttendanceLogId,
            Date = x.o.Date,
            OvertimeMinutes = x.o.OvertimeMinutes,
            ApprovedMinutes = x.o.ApprovedMinutes,
            Multiplier = x.o.Multiplier,
            Type = x.o.Type,
            Status = x.o.Status,
            Reason = x.o.Reason,
            ManagerComment = x.o.ManagerComment,
            CreatedAt = x.o.CreatedAt,
            EmployeeName = $"{x.e.FirstName} {x.e.LastName}".Trim(),
            EmployeePhoto = x.e.ProfilePhotoUrl,
            SubmittedOn = x.o.CreatedAt,
        }).ToList();

        return Result<OvertimeQueueResult>.Success(
            new OvertimeQueueResult { Items = items, TotalCount = items.Count });
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-4 / FR-6 / FR-7 — approve (with adjust) / reject
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<OvertimeDecisionDto>> ApproveAsync(
        Guid overtimeId, int? approvedMinutes, string? comment,
        CancellationToken cancellationToken = default)
    {
        var ctx = await LoadForDecisionAsync(overtimeId, cancellationToken);
        if (ctx.Failure is not null)
            return ctx.Failure;

        var record = ctx.Record!;
        var manager = ctx.Manager!;

        // FR-6/AC-4: approved minutes default to the recorded overtime; a manager adjustment must be
        // between 0 and the recorded overtime (cannot approve MORE than was worked).
        var finalApproved = approvedMinutes ?? record.OvertimeMinutes;
        if (finalApproved < 0 || finalApproved > record.OvertimeMinutes)
            return Result<OvertimeDecisionDto>.Failure(
                $"Approved minutes must be between 0 and the recorded overtime ({record.OvertimeMinutes}).",
                400, "invalid_approved_minutes");

        record.Status = OvertimeStatus.Approved;
        record.ApprovedMinutes = finalApproved;
        record.ManagerComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        record.IsPayrollReady = true;   // FR-7: approved overtime is payroll-ready.

        var history = NewHistory(
            record.Id, manager.Id, OvertimeApprovalAction.Approved,
            finalApproved, record.ManagerComment, record.CalculationBasis);
        _dbContext.OvertimeApprovalHistories.Add(history);

        // NFR-3: single atomic SaveChanges (status + approved minutes + payroll flag + history).
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-8 (notify employee of approval): DEFERRED — no notification infra (TODO US-NTF).

        _logger.LogInformation(
            "Overtime {Id} APPROVED by manager {ManagerId} for employee {EmployeeId}: " +
            "{ApprovedMinutes} min @ {Multiplier}x (tenant {TenantId}).",
            record.Id, manager.Id, record.EmployeeId, finalApproved, record.Multiplier, _tenantContext.TenantId);

        return Result<OvertimeDecisionDto>.Success(new OvertimeDecisionDto
        {
            Id = record.Id,
            Status = record.Status,
            ApprovedMinutes = record.ApprovedMinutes,
            Multiplier = record.Multiplier,
            ManagerComment = record.ManagerComment,
            ActionedAt = history.ActionedAt,
        });
    }

    public async Task<Result<OvertimeDecisionDto>> RejectAsync(
        Guid overtimeId, string reason, CancellationToken cancellationToken = default)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length < 10)
            return Result<OvertimeDecisionDto>.Failure(
                "A rejection reason of at least 10 characters is required.", 400);

        var ctx = await LoadForDecisionAsync(overtimeId, cancellationToken);
        if (ctx.Failure is not null)
            return ctx.Failure;

        var record = ctx.Record!;
        var manager = ctx.Manager!;

        record.Status = OvertimeStatus.Rejected;
        record.ApprovedMinutes = null;
        record.ManagerComment = trimmed;
        record.IsPayrollReady = false;

        var history = NewHistory(
            record.Id, manager.Id, OvertimeApprovalAction.Rejected,
            null, trimmed, record.CalculationBasis);
        _dbContext.OvertimeApprovalHistories.Add(history);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-8 (notify employee of rejection with reason): DEFERRED — no notification infra (TODO US-NTF).

        _logger.LogInformation(
            "Overtime {Id} REJECTED by manager {ManagerId} for employee {EmployeeId} (tenant {TenantId}).",
            record.Id, manager.Id, record.EmployeeId, _tenantContext.TenantId);

        return Result<OvertimeDecisionDto>.Success(new OvertimeDecisionDto
        {
            Id = record.Id,
            Status = record.Status,
            ApprovedMinutes = null,
            Multiplier = record.Multiplier,
            ManagerComment = trimmed,
            ActionedAt = history.ActionedAt,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-5 — monthly HR report
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<OvertimeReportResult>> GetMonthlyReportAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OvertimeReportResult>.Failure("Tenant context is not resolved.", 400);

        if (month is < 1 or > 12)
            return Result<OvertimeReportResult>.Failure("Month must be between 1 and 12.", 400, "invalid_month");

        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var query =
            from o in _dbContext.OvertimeRecords.AsNoTracking()
            join e in _dbContext.Employees.AsNoTracking() on o.EmployeeId equals e.Id
            where o.Date >= monthStart && o.Date <= monthEnd
            select new { o.EmployeeId, EmployeeName = e.FirstName + " " + e.LastName, o.Status, o.OvertimeMinutes, o.ApprovedMinutes };

        var rows = await query.ToListAsync(cancellationToken);

        var items = rows
            .GroupBy(r => new { r.EmployeeId, r.EmployeeName })
            .Select(g => new OvertimeReportRowDto
            {
                EmployeeId = g.Key.EmployeeId,
                EmployeeName = (g.Key.EmployeeName ?? string.Empty).Trim(),
                ApprovedMinutes = g.Where(x => x.Status == OvertimeStatus.Approved)
                                   .Sum(x => x.ApprovedMinutes ?? 0),
                PendingMinutes = g.Where(x => x.Status == OvertimeStatus.Pending)
                                  .Sum(x => x.OvertimeMinutes),
                RejectedMinutes = g.Where(x => x.Status == OvertimeStatus.Rejected)
                                   .Sum(x => x.OvertimeMinutes),
                RecordCount = g.Count(),
            })
            .OrderBy(r => r.EmployeeName)
            .ToList();

        var totals = new OvertimeReportTotals
        {
            ApprovedMinutes = items.Sum(i => i.ApprovedMinutes),
            PendingMinutes = items.Sum(i => i.PendingMinutes),
            RejectedMinutes = items.Sum(i => i.RejectedMinutes),
            RecordCount = items.Sum(i => i.RecordCount),
        };

        return Result<OvertimeReportResult>.Success(new OvertimeReportResult
        {
            Month = $"{year:D4}-{month:D2}",
            Items = items,
            Totals = totals,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Shared load + authorization (BR-8 self-approval, direct-report, immutability)
    // ══════════════════════════════════════════════════════════════

    private async Task<DecisionContext> LoadForDecisionAsync(
        Guid overtimeId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return DecisionContext.Fail("Tenant context is not resolved.", 400);

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
            return DecisionContext.Fail("No employee record is linked to the current user.", 403);

        var record = await _dbContext.OvertimeRecords
            .FirstOrDefaultAsync(o => o.Id == overtimeId, cancellationToken);
        if (record is null)
            return DecisionContext.Fail("Overtime record not found.", 404);

        // BR-8: a manager cannot approve their OWN overtime — checked before the team check.
        if (record.EmployeeId == manager.Id)
            return DecisionContext.Fail(
                "You cannot approve your own overtime. It must be approved by your manager or HR.",
                403, "self_approval");

        // FR-5/AC-3: direct reports only.
        var requester = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == record.EmployeeId, cancellationToken);
        if (requester is null || requester.ReportsToEmployeeId != manager.Id)
            return DecisionContext.Fail(
                "You are not authorized to approve overtime for this employee.", 403, "not_team_member");

        // Immutability: only PENDING records can be actioned (APPROVED/REJECTED/UNAPPROVED are terminal
        // for the manager — UNAPPROVED is routed to HR, not the manager).
        if (record.Status != OvertimeStatus.Pending)
            return DecisionContext.Fail(
                $"This overtime record has already been actioned (status: {record.Status}).",
                409, "already_actioned");

        return new DecisionContext { Record = record, Manager = manager };
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private Task<Employee?> GetCurrentEmployeeAsync(
        CancellationToken cancellationToken, bool asNoTracking = false)
    {
        var q = _dbContext.Employees.AsQueryable();
        if (asNoTracking) q = q.AsNoTracking();
        return q.FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
    }

    /// <summary>
    /// Resolves the employee's standard work minutes for a date: the assigned shift (US-ATT-005)
    /// converted from its start/end span minus break, else the tenant
    /// <see cref="AttendanceSettings.StandardWorkMinutes"/>. FLEXIBLE shifts use their MinimumHours.
    /// </summary>
    private async Task<int> ResolveStandardMinutesAsync(
        Guid employeeId, DateOnly date, AttendanceSettings settings, CancellationToken cancellationToken)
    {
        var assignment = await _dbContext.EmployeeShifts
            .AsNoTracking()
            .Where(es => es.EmployeeId == employeeId
                && es.EffectiveFrom <= date
                && (es.EffectiveTo == null || es.EffectiveTo >= date))
            .OrderByDescending(es => es.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        Shift? shift = null;
        if (assignment is not null)
        {
            shift = await _dbContext.Shifts
                .AsNoTracking()
                .Include(s => s.RotationSteps)
                .FirstOrDefaultAsync(s => s.Id == assignment.ShiftId, cancellationToken);
        }

        shift ??= await _dbContext.Shifts
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsDefault, cancellationToken);

        if (shift is null)
            return settings.StandardWorkMinutes;   // AC-1 fallback: no shift -> tenant setting.

        var standard = ShiftStandardMinutes(shift);
        return standard ?? settings.StandardWorkMinutes;
    }

    /// <summary>
    /// Standard work minutes a shift represents: (end - start) less break for SINGLE/ROTATING headers
    /// (handles overnight where end &lt; start), or MinimumHours*60 for FLEXIBLE. Null when undeterminable
    /// (caller falls back to the tenant setting).
    /// </summary>
    private static int? ShiftStandardMinutes(Shift shift)
    {
        if (shift.Type == ShiftType.Flexible)
            return shift.MinimumHours.HasValue
                ? (int)Math.Round(shift.MinimumHours.Value * 60m, MidpointRounding.AwayFromZero)
                : (int?)null;

        if (shift.StartTime is null || shift.EndTime is null)
            return null;

        var startMin = shift.StartTime.Value.ToTimeSpan().TotalMinutes;
        var endMin = shift.EndTime.Value.ToTimeSpan().TotalMinutes;
        var span = endMin - startMin;
        if (span <= 0) span += 24 * 60;   // overnight shift (§10 night shift allowed).

        var net = (int)Math.Round(span, MidpointRounding.AwayFromZero) - shift.BreakDurationMinutes;
        return net > 0 ? net : (int?)null;
    }

    /// <summary>BR-3/BR-7: whether the date is an active public holiday in the tenant calendar.</summary>
    private Task<bool> IsPublicHolidayAsync(DateOnly date, CancellationToken cancellationToken)
        => _dbContext.Holidays
            .AsNoTracking()
            .AnyAsync(h => h.Date == date && h.IsActive && h.Type == HolidayType.Public, cancellationToken);

    /// <summary>
    /// BR-5/FR-8: returns true if adding <paramref name="newMinutes"/> pushes the employee's
    /// approved+pending overtime in the Monday-anchored week of <paramref name="date"/> over the max.
    /// </summary>
    private async Task<bool> EvaluateWeeklyCapAsync(
        Guid employeeId, DateOnly date, int newMinutes, int maxWeeklyMinutes, CancellationToken cancellationToken)
    {
        if (maxWeeklyMinutes <= 0)
            return false;

        // Monday-anchored ISO week containing the date.
        var dow = (int)date.DayOfWeek;            // Sun=0..Sat=6
        var daysSinceMonday = (dow + 6) % 7;      // Mon=0
        var weekStart = date.AddDays(-daysSinceMonday);
        var weekEnd = weekStart.AddDays(6);

        var existing = await _dbContext.OvertimeRecords
            .AsNoTracking()
            .Where(o => o.EmployeeId == employeeId
                     && o.Date >= weekStart && o.Date <= weekEnd
                     && (o.Status == OvertimeStatus.Approved || o.Status == OvertimeStatus.Pending))
            .Select(o => o.Status == OvertimeStatus.Approved ? (o.ApprovedMinutes ?? 0) : o.OvertimeMinutes)
            .ToListAsync(cancellationToken);

        return existing.Sum() + newMinutes > maxWeeklyMinutes;
    }

    private async Task<AttendanceSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.AttendanceSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
            return settings;

        settings = new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
        };
        _dbContext.AttendanceSettings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private OvertimeApprovalHistory NewHistory(
        Guid overtimeRecordId, Guid approverEmployeeId, string action,
        int? approvedMinutes, string? comment, string? calculationBasis) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantContext.TenantId,
        OvertimeRecordId = overtimeRecordId,
        ApproverEmployeeId = approverEmployeeId,
        ApprovalLevel = 1,
        Action = action,
        ApprovedMinutes = approvedMinutes,
        Comment = comment,
        CalculationBasis = calculationBasis,
        ActionedAt = DateTime.UtcNow,
    };

    private static OvertimeDto ToDto(OvertimeRecord o) => new()
    {
        Id = o.Id,
        EmployeeId = o.EmployeeId,
        AttendanceLogId = o.AttendanceLogId,
        Date = o.Date,
        OvertimeMinutes = o.OvertimeMinutes,
        ApprovedMinutes = o.ApprovedMinutes,
        Multiplier = o.Multiplier,
        Type = o.Type,
        Status = o.Status,
        Reason = o.Reason,
        ManagerComment = o.ManagerComment,
        CreatedAt = o.CreatedAt,
    };

    private sealed class DecisionContext
    {
        public OvertimeRecord? Record { get; init; }
        public Employee? Manager { get; init; }
        public Result<OvertimeDecisionDto>? Failure { get; init; }

        public static DecisionContext Fail(string error, int statusCode, string? errorCode = null) => new()
        {
            Failure = Result<OvertimeDecisionDto>.Failure(error, statusCode, errorCode),
        };
    }
}
