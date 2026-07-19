using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Late-arrival / early-departure policy + reporting service (US-ATT-008). Detection itself is inline
/// on clock-in/out (AttendanceService) and on regularization approval (RegularizationApprovalService);
/// this service owns the tenant POLICY (FR-4), the team/HR REPORT (AC-5/FR-6) and the self-SCORE (§8).
///
/// SINGLE SOURCE OF TRUTH (DRY): the report and score read the PERSISTED is_late / late_minutes /
/// is_early_departure / early_departure_minutes columns on attendance_log — they never recompute from
/// the shift. Tenant isolation is the EF global query filter + TenantInterceptor (no PostgreSQL RLS,
/// same deferral as the rest of the module — NFR-2 not satisfied via RLS, documented).
///
/// DEFERRED (do NOT block): FR-5 (per-late notification) and FR-7 (chronic HR escalation NOTIFICATION)
/// have no notification infrastructure (TODO US-NTF). The chronic FLAG (isChronic) and the deduction
/// FLAG (LOP in the monthly summary) ARE implemented — only the notify half is deferred.
/// </summary>
public sealed class LateEarlyService : ILateEarlyService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<LateEarlyService> _logger;

    public LateEarlyService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<LateEarlyService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Policy (FR-4)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LatePolicyDto>> GetPolicyAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LatePolicyDto>.Failure("Tenant context is not resolved.", 400);

        var policy = await _dbContext.LatePolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return Result<LatePolicyDto>.Success(policy is null ? new LatePolicyDto() : MapToDto(policy));
    }

    public async Task<Result<LatePolicyDto>> UpsertPolicyAsync(
        LatePolicyDto policy, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LatePolicyDto>.Failure("Tenant context is not resolved.", 400);

        var period = (policy.Period ?? string.Empty).Trim().ToUpperInvariant();
        if (!LatePolicyPeriod.IsValid(period))
            return Result<LatePolicyDto>.Failure(
                "Period must be MONTHLY or QUARTERLY.", 400, "invalid_period");
        if (policy.ThresholdCount < 1)
            return Result<LatePolicyDto>.Failure("Threshold count must be at least 1.", 400, "invalid_threshold");
        if (policy.DeductionDays < 0m || policy.DeductionDays > 31m)
            return Result<LatePolicyDto>.Failure("Deduction days must be between 0 and 31.", 400, "invalid_deduction");
        if (policy.ChronicThreshold < 1)
            return Result<LatePolicyDto>.Failure("Chronic threshold must be at least 1.", 400, "invalid_chronic_threshold");

        var entity = await _dbContext.LatePolicies.FirstOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            entity = new LatePolicy
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
            };
            _dbContext.LatePolicies.Add(entity);
        }

        entity.ThresholdCount = policy.ThresholdCount;
        entity.DeductionDays = policy.DeductionDays;
        entity.Period = period;
        entity.NotificationOnLate = policy.NotificationOnLate;
        entity.ChronicThreshold = policy.ChronicThreshold;
        entity.IsActive = policy.IsActive;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Late policy upserted for tenant {TenantId}: threshold={Threshold} deduction={Deduction} " +
            "period={Period} chronic={Chronic} active={Active}.",
            _tenantContext.TenantId, entity.ThresholdCount, entity.DeductionDays, entity.Period,
            entity.ChronicThreshold, entity.IsActive);

        return Result<LatePolicyDto>.Success(MapToDto(entity));
    }

    // ══════════════════════════════════════════════════════════════
    //  Report (AC-5 / FR-6)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LateEarlyReportResult>> GetReportAsync(
        DateOnly from, DateOnly to, Guid? departmentId, Guid? employeeId, string scope,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LateEarlyReportResult>.Failure("Tenant context is not resolved.", 400);

        if (to < from)
            return Result<LateEarlyReportResult>.Failure("The 'to' date must be on or after the 'from' date.", 400, "invalid_range");

        var normalizedScope = (scope ?? "team").Trim().ToLowerInvariant();
        if (normalizedScope is not ("team" or "all"))
            return Result<LateEarlyReportResult>.Failure("Scope must be 'team' or 'all'.", 400, "invalid_scope");

        // AC-5 / FR-6: scope=all is HR-only (Attendance.View.All); scope=team is the acting manager's
        // direct reports. The controller gate (Attendance.Approve.Team) admits both managers and HR;
        // here we enforce that "all" additionally requires the HR read permission.
        if (normalizedScope == "all"
            && !_currentUser.Permissions.Contains(PermissionCatalog.Attendance.ViewAll))
            return Result<LateEarlyReportResult>.Failure(
                "You are not authorized to view the all-employees late/early report.", 403, "scope_not_allowed");

        // Determine the candidate employee set per scope (both tenant-scoped by the global filter).
        List<Guid> employeeIds;
        if (normalizedScope == "team")
        {
            var manager = await _dbContext.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
            if (manager is null)
                return Result<LateEarlyReportResult>.Success(EmptyResult(from, to));

            employeeIds = await _dbContext.Employees.AsNoTracking()
                .Where(e => e.ReportsToEmployeeId == manager.Id)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);
        }
        else
        {
            employeeIds = await _dbContext.Employees.AsNoTracking()
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);
        }

        if (employeeId is { } emp)
            employeeIds = employeeIds.Where(id => id == emp).ToList();

        if (employeeIds.Count == 0)
            return Result<LateEarlyReportResult>.Success(EmptyResult(from, to));

        var empIdSet = employeeIds.ToHashSet();

        // Employee metadata (name, department) for the rows, with the optional department filter.
        var empQuery = _dbContext.Employees.AsNoTracking().Where(e => empIdSet.Contains(e.Id));
        if (departmentId is { } dept)
            empQuery = empQuery.Where(e => e.DepartmentId == dept);
        var employees = await empQuery.ToListAsync(cancellationToken);

        var deptNames = await DepartmentNamesAsync(employees, cancellationToken);
        var filteredIds = employees.Select(e => e.Id).ToHashSet();

        // Read the PERSISTED late/early fields from attendance_log over the range (DRY — no recompute).
        var rangeStart = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rangeEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var logs = await _dbContext.AttendanceLogs.AsNoTracking()
            .Where(a => filteredIds.Contains(a.EmployeeId)
                && a.ClockIn >= rangeStart && a.ClockIn < rangeEnd)
            .Select(a => new
            {
                a.EmployeeId, a.ClockIn, a.IsLate, a.LateMinutes, a.IsEarlyDeparture, a.EarlyDepartureMinutes,
            })
            .ToListAsync(cancellationToken);

        // ISSUE-084: derive the counts from the single shared CountLateEarly definition (distinct days)
        // so this report reconciles with the self-score and the monthly summary for the same employee.
        var byEmp = logs
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => CountLateEarly(g.Select(x => new LateEarlyLogRow(
                    x.ClockIn, x.IsLate, x.LateMinutes, x.IsEarlyDeparture, x.EarlyDepartureMinutes))));

        // Chronic threshold (FR-7) from the tenant policy; 0 / no-policy → never chronic.
        var policy = await _dbContext.LatePolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        int chronicThreshold = policy?.ChronicThreshold ?? 0;

        var rows = employees
            .Select(e =>
            {
                var agg = byEmp.GetValueOrDefault(e.Id);
                return new LateEarlyRowDto
                {
                    EmployeeId = e.Id,
                    EmployeeName = $"{e.FirstName} {e.LastName}".Trim(),
                    DepartmentName = deptNames.GetValueOrDefault(e.DepartmentId),
                    LateCount = agg.LateCount,
                    TotalLateMinutes = agg.LateMinutes,
                    EarlyDepartureCount = agg.EarlyCount,
                    TotalEarlyMinutes = agg.EarlyMinutes,
                    IsChronic = chronicThreshold > 0 && agg.LateCount > chronicThreshold,
                };
            })
            .OrderByDescending(r => r.LateCount)
            .ThenBy(r => r.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<LateEarlyReportResult>.Success(new LateEarlyReportResult
        {
            From = from.ToString("yyyy-MM-dd"),
            To = to.ToString("yyyy-MM-dd"),
            Rows = rows,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Self score (§8)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LatenessScoreDto>> GetMyScoreAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LatenessScoreDto>.Failure("Tenant context is not resolved.", 400);
        if (month is < 1 or > 12)
            return Result<LatenessScoreDto>.Failure("Month must be between 1 and 12.", 400, "invalid_month");

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<LatenessScoreDto>.Failure(
                "No employee record is linked to the current user.", 403);

        var monthStart = new DateOnly(year, month, 1);
        var rangeStart = monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rangeEnd = monthStart.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var logs = await _dbContext.AttendanceLogs.AsNoTracking()
            .Where(a => a.EmployeeId == employee.Id && a.ClockIn >= rangeStart && a.ClockIn < rangeEnd)
            .Select(a => new { a.ClockIn, a.IsLate, a.LateMinutes, a.IsEarlyDeparture, a.EarlyDepartureMinutes })
            .ToListAsync(cancellationToken);

        var policy = await _dbContext.LatePolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        // ISSUE-084: same shared CountLateEarly definition (distinct days) as GetReportAsync, so the
        // self-score and the manager/HR report never disagree for the same employee/period.
        var counts = CountLateEarly(logs.Select(l => new LateEarlyLogRow(
            l.ClockIn, l.IsLate, l.LateMinutes, l.IsEarlyDeparture, l.EarlyDepartureMinutes)));

        return Result<LatenessScoreDto>.Success(new LatenessScoreDto
        {
            YearMonth = $"{year:D4}-{month:D2}",
            LateCount = counts.LateCount,
            AllowedLates = policy?.ChronicThreshold ?? 0,
            EarlyDepartureCount = counts.EarlyCount,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Chronic-lateness count (FR-7)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// FR-7: distinct late-day count for <paramref name="employeeId"/> in the calendar month containing
    /// <paramref name="asOfLocalDate"/>. Mirrors the month-range construction in <see cref="GetMyScoreAsync"/>
    /// and reuses the shared <see cref="CountLateEarly"/> distinct-day definition (DRY) — so the chronic-lateness
    /// escalation, the report and the self-score never disagree for the same employee/period.
    /// </summary>
    public async Task<int> CountLateDaysInMonthAsync(
        Guid employeeId, DateOnly asOfLocalDate, CancellationToken cancellationToken = default)
    {
        var monthStart = new DateOnly(asOfLocalDate.Year, asOfLocalDate.Month, 1);
        var rangeStart = monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rangeEnd = monthStart.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var logs = await _dbContext.AttendanceLogs.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.ClockIn >= rangeStart && a.ClockIn < rangeEnd)
            .Select(a => new { a.ClockIn, a.IsLate, a.LateMinutes, a.IsEarlyDeparture, a.EarlyDepartureMinutes })
            .ToListAsync(cancellationToken);

        return CountLateEarly(logs.Select(l => new LateEarlyLogRow(
            l.ClockIn, l.IsLate, l.LateMinutes, l.IsEarlyDeparture, l.EarlyDepartureMinutes))).LateCount;
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// ISSUE-084: the SINGLE canonical late/early COUNT definition shared by the manager/HR report
    /// (AC-5/FR-6) and the employee self-score (§8). Counts the number of DISTINCT DAYS on which the
    /// employee was late / left early — matching the monthly summary's visible "Late Count" /
    /// "Early Departure Count" (US-ATT-007, one per calendar day) so the same employee shows identical
    /// counts on every US-ATT-008 surface. Counting distinct days (rather than raw logs) prevents a
    /// second same-day punch from double-counting. The day basis is the UTC calendar day of clock-in —
    /// the same basis the monthly summary buckets by. Late/early MINUTES stay additive across all logs.
    /// </summary>
    private static (int LateCount, int LateMinutes, int EarlyCount, int EarlyMinutes) CountLateEarly(
        IEnumerable<LateEarlyLogRow> logs)
    {
        var lateDays = new HashSet<DateOnly>();
        var earlyDays = new HashSet<DateOnly>();
        int lateMinutes = 0, earlyMinutes = 0;

        foreach (var log in logs)
        {
            var day = DateOnly.FromDateTime(log.ClockIn);
            if (log.IsLate)
            {
                lateDays.Add(day);
                lateMinutes += log.LateMinutes;
            }
            if (log.IsEarlyDeparture)
            {
                earlyDays.Add(day);
                earlyMinutes += log.EarlyDepartureMinutes;
            }
        }

        return (lateDays.Count, lateMinutes, earlyDays.Count, earlyMinutes);
    }

    private readonly record struct LateEarlyLogRow(
        DateTime ClockIn, bool IsLate, int LateMinutes, bool IsEarlyDeparture, int EarlyDepartureMinutes);

    private async Task<Dictionary<Guid, string>> DepartmentNamesAsync(
        IReadOnlyList<Employee> employees, CancellationToken ct)
    {
        var deptIds = employees.Select(e => e.DepartmentId).Distinct().ToList();
        if (deptIds.Count == 0) return new Dictionary<Guid, string>();
        return (await _dbContext.Departments.AsNoTracking()
                .Where(d => deptIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);
    }

    private static LateEarlyReportResult EmptyResult(DateOnly from, DateOnly to) => new()
    {
        From = from.ToString("yyyy-MM-dd"),
        To = to.ToString("yyyy-MM-dd"),
        Rows = [],
    };

    private static LatePolicyDto MapToDto(LatePolicy p) => new()
    {
        ThresholdCount = p.ThresholdCount,
        DeductionDays = p.DeductionDays,
        Period = p.Period,
        NotificationOnLate = p.NotificationOnLate,
        ChronicThreshold = p.ChronicThreshold,
        IsActive = p.IsActive,
    };
}
