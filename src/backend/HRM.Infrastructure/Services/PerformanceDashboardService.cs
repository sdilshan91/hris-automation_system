using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Performance dashboard + analytics service (US-PRF-007). See <see cref="IPerformanceDashboardService"/>
/// for the full responsibility / scope / deferral list. Every query runs under the EF global query filter
/// (TenantId == tenant) so the whole dashboard is tenant-isolated (NFR-2/AC-5) — there is no Postgres RLS
/// (deferred). The headline final score is REUSED from <c>ManagerReview.FinalScore</c> (BR-4); this service
/// never recomputes scores. Data is loaded in a handful of bulk tenant-scoped queries and aggregated
/// in-memory (GroupBy), then projected to the pinned DTOs.
///
/// SCOPE (BR-1/BR-3/AC-5): <see cref="ResolveScopeAsync"/> derives Organization vs Team from the caller's
/// permissions. A manager (Performance.View.Team WITHOUT Performance.View.All) is HARD-restricted to their
/// own direct reports — the in-scope employee id set is the manager's reports, so every aggregate, the
/// distribution, the department averages, the trend and the drill-down only ever see that team. A manager
/// gets a team ranking instead of org-wide top/bottom performers (BR-3), and cannot pull another team's or
/// the org's data through any method here.
///
/// EXTENSION POINT (NFR-3/BR-4 — materialized views / Redis): these aggregates are computed LIVE on each
/// request with tenant-scoped EF queries. A future story can introduce a performance_summary materialized
/// view refreshed every 4 hours by Hangfire + a Redis read-through cache keyed by (tenantId, cycleId,
/// filter-hash, scope) WITHOUT changing this service's public contract — the DTOs are the stable seam.
/// </summary>
public sealed class PerformanceDashboardService : IPerformanceDashboardService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<PerformanceDashboardService> _logger;

    public PerformanceDashboardService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<PerformanceDashboardService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Overview (AC-1/AC-2/FR-1..FR-3/FR-6)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PerformanceDashboardDto>> GetOverviewAsync(
        PerformanceDashboardFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PerformanceDashboardDto>.Failure("Tenant context is not resolved.", 400);

        var scopeResult = await ResolveScopeAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return Result<PerformanceDashboardDto>.Failure(
                scopeResult.Error!, scopeResult.StatusCode ?? 403, scopeResult.ErrorCode);
        var scope = scopeResult.Value!;

        var cycle = await ResolveCycleAsync(filter.CycleId, cancellationToken);
        if (cycle is null)
            return Result<PerformanceDashboardDto>.Failure(
                "No appraisal cycle is available for this tenant.", 404, "no_cycle");

        var population = await LoadPopulationAsync(cycle.Id, filter, scope, cancellationToken);

        // Scored employees = those in scope with a SUBMITTED manager review carrying a final score (BR-4).
        var scored = population.Where(e => e.FinalScore is not null).ToList();
        var scoredCount = scored.Count;
        var avg = scoredCount == 0 ? 0m : Round(scored.Average(e => e.FinalScore!.Value));

        var distribution = BuildDistribution(scored, cycle.RatingScaleMax);
        var deptAverages = BuildDepartmentAverages(scored);
        var (top, bottom) = BuildPerformerLists(scored, filter.TopBottomCount, scope);
        var progress = BuildProgress(population);

        var dto = new PerformanceDashboardDto
        {
            CycleId = cycle.Id,
            CycleName = cycle.Name,
            Scope = scope.Kind.ToString(),
            RatingScaleMax = cycle.RatingScaleMax,
            ScoredEmployeeCount = scoredCount,
            AverageScore = avg,
            Progress = progress,
            ScoreDistribution = distribution,
            DepartmentAverages = deptAverages,
            TopPerformers = top,
            BottomPerformers = bottom,
        };

        return Result<PerformanceDashboardDto>.Success(dto);
    }

    // ══════════════════════════════════════════════════════════════
    //  Department drill-down (FR-5/AC-2)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<DepartmentDrilldownDto>> GetDepartmentDrilldownAsync(
        Guid departmentId, PerformanceDashboardFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<DepartmentDrilldownDto>.Failure("Tenant context is not resolved.", 400);

        var scopeResult = await ResolveScopeAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return Result<DepartmentDrilldownDto>.Failure(
                scopeResult.Error!, scopeResult.StatusCode ?? 403, scopeResult.ErrorCode);
        var scope = scopeResult.Value!;

        var cycle = await ResolveCycleAsync(filter.CycleId, cancellationToken);
        if (cycle is null)
            return Result<DepartmentDrilldownDto>.Failure(
                "No appraisal cycle is available for this tenant.", 404, "no_cycle");

        var department = await _dbContext.Departments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken);
        if (department is null)
            return Result<DepartmentDrilldownDto>.Failure("Department not found.", 404, "department_not_found");

        // Drill-down is the dashboard population pinned to one department (FR-5). Manager scope still applies
        // (a manager only ever sees their own reports within the department — BR-1/BR-3).
        var deptFilter = filter with { DepartmentId = departmentId };
        var population = await LoadPopulationAsync(cycle.Id, deptFilter, scope, cancellationToken);

        var employees = population
            .OrderByDescending(e => e.FinalScore ?? decimal.MinValue)
            .ThenBy(e => e.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .Select(e => new DepartmentEmployeeScoreDto
            {
                EmployeeId = e.EmployeeId,
                EmployeeName = e.EmployeeName,
                EmployeeNo = e.EmployeeNo,
                JobTitle = e.JobTitle,
                Score = e.FinalScore,
                Status = e.Status,
            })
            .ToList();

        var scored = population.Where(e => e.FinalScore is not null).ToList();
        var avg = scored.Count == 0 ? 0m : Round(scored.Average(e => e.FinalScore!.Value));

        return Result<DepartmentDrilldownDto>.Success(new DepartmentDrilldownDto
        {
            CycleId = cycle.Id,
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            Headcount = scored.Count,
            AverageScore = avg,
            Employees = employees,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Multi-cycle trend (AC-3/FR-7)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PerformanceTrendDto>> GetTrendAsync(
        IReadOnlyList<Guid> cycleIds, PerformanceDashboardFilter filter, bool includeDepartmentSeries,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PerformanceTrendDto>.Failure("Tenant context is not resolved.", 400);

        var scopeResult = await ResolveScopeAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return Result<PerformanceTrendDto>.Failure(
                scopeResult.Error!, scopeResult.StatusCode ?? 403, scopeResult.ErrorCode);
        var scope = scopeResult.Value!;

        // Resolve the requested cycles (tenant-scoped via the global filter). An empty/absent list trends ALL
        // the tenant's cycles, ordered by start (FR-7).
        var cyclesQuery = _dbContext.AppraisalCycles.AsNoTracking();
        if (cycleIds.Count > 0)
            cyclesQuery = cyclesQuery.Where(c => cycleIds.Contains(c.Id));
        var cycles = await cyclesQuery
            .OrderBy(c => c.StartDate)
            .Select(c => new { c.Id, c.Name, c.StartDate })
            .ToListAsync(cancellationToken);

        var points = new List<CycleTrendPointDto>(cycles.Count);
        var deptSeriesAccum = new Dictionary<Guid, (string Name, List<CycleTrendPointDto> Points)>();

        foreach (var c in cycles)
        {
            var population = await LoadPopulationAsync(c.Id, filter, scope, cancellationToken);
            var scored = population.Where(e => e.FinalScore is not null).ToList();
            var avg = scored.Count == 0 ? 0m : Round(scored.Average(e => e.FinalScore!.Value));

            points.Add(new CycleTrendPointDto
            {
                CycleId = c.Id,
                CycleName = c.Name,
                StartDate = c.StartDate,
                AverageScore = avg,
                ScoredEmployeeCount = scored.Count,
            });

            if (includeDepartmentSeries)
            {
                foreach (var deptGroup in scored.GroupBy(e => e.DepartmentId))
                {
                    var deptScored = deptGroup.ToList();
                    if (!deptSeriesAccum.TryGetValue(deptGroup.Key, out var entry))
                        deptSeriesAccum[deptGroup.Key] = entry = (deptScored[0].DepartmentName, new List<CycleTrendPointDto>());
                    entry.Points.Add(new CycleTrendPointDto
                    {
                        CycleId = c.Id,
                        CycleName = c.Name,
                        StartDate = c.StartDate,
                        AverageScore = Round(deptScored.Average(e => e.FinalScore!.Value)),
                        ScoredEmployeeCount = deptScored.Count,
                    });
                }
            }
        }

        var deptSeries = deptSeriesAccum
            .Select(kvp => new DepartmentTrendSeriesDto
            {
                DepartmentId = kvp.Key,
                DepartmentName = kvp.Value.Name,
                Points = kvp.Value.Points.OrderBy(p => p.StartDate).ToList(),
            })
            .OrderBy(s => s.DepartmentName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<PerformanceTrendDto>.Success(new PerformanceTrendDto
        {
            Scope = scope.Kind.ToString(),
            Points = points,
            DepartmentSeries = deptSeries,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Export (FR-8/AC-4): CSV + XLSX (ClosedXML). PDF deferred (QuestPDF seam).
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PerformanceDashboardExportResult>> ExportOverviewAsync(
        PerformanceDashboardFilter filter, string format, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PerformanceDashboardExportResult>.Failure("Tenant context is not resolved.", 400);

        var normalized = NormalizeFormat(format);
        if (normalized is null)
            return Result<PerformanceDashboardExportResult>.Failure(
                "Export format must be one of csv, xlsx.", 400, "invalid_format");

        var overview = await GetOverviewAsync(filter, cancellationToken);
        if (overview.IsFailure)
            return Result<PerformanceDashboardExportResult>.Failure(
                overview.Error!, overview.StatusCode ?? 400, overview.ErrorCode);

        var (content, fileName, contentType) = RenderExport(normalized, overview.Value!);
        return Result<PerformanceDashboardExportResult>.Success(new PerformanceDashboardExportResult
        {
            FileContent = content,
            FileName = fileName,
            ContentType = contentType,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Scope resolution (BR-1/BR-3/AC-5)
    // ══════════════════════════════════════════════════════════════

    private readonly record struct DashboardScope(
        PerformanceDashboardScope Kind, IReadOnlySet<Guid>? RestrictEmployeeIds);

    /// <summary>
    /// Derives the caller's dashboard scope from their permissions (AC-5). Performance.View.All ⇒ org-wide
    /// (no employee restriction). Otherwise Performance.View.Team ⇒ team scope restricted to the caller's
    /// direct reports (BR-1/BR-3). A caller with neither permission is forbidden (employees have no dashboard
    /// — BR-1, redirected on the FE; enforced server-side here too).
    /// </summary>
    private async Task<Result<DashboardScope>> ResolveScopeAsync(CancellationToken cancellationToken)
    {
        var permissions = _currentUser.Permissions;

        if (permissions.Contains(PermissionCatalog.Performance.ViewAll))
            return Result<DashboardScope>.Success(
                new DashboardScope(PerformanceDashboardScope.Organization, null));

        if (!permissions.Contains(PermissionCatalog.Performance.ViewTeam))
            return Result<DashboardScope>.Failure(
                "You do not have permission to view the performance dashboard.", 403, "forbidden");

        // Manager scope: restrict to the caller's direct reports.
        var manager = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (manager is null)
            return Result<DashboardScope>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        var reportIds = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == manager.Id)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        return Result<DashboardScope>.Success(
            new DashboardScope(PerformanceDashboardScope.Team, reportIds.ToHashSet()));
    }

    // ══════════════════════════════════════════════════════════════
    //  Population loading + filters (FR-4/BR-2)
    // ══════════════════════════════════════════════════════════════

    private sealed record EmployeeScoreRow(
        Guid EmployeeId, string EmployeeName, string EmployeeNo, Guid DepartmentId,
        string DepartmentName, string JobTitle, decimal? FinalScore, bool HasGoals,
        bool SelfSubmitted, bool ManagerSubmitted, bool SignedOff)
    {
        public string Status =>
            ManagerSubmitted ? "Completed"
            : SelfSubmitted ? "ManagerReviewPending"
            : "PendingSelfAssessment";
    }

    /// <summary>
    /// Loads the in-scope employee population for a cycle with their final scores + completion flags, applying
    /// the FR-4 filters (department / grade / employment-type / location), the BR-2 probation exclusion and the
    /// manager-vs-HR scope (BR-1/BR-3). Everything runs under the tenant global filter (NFR-2).
    /// </summary>
    private async Task<List<EmployeeScoreRow>> LoadPopulationAsync(
        Guid cycleId, PerformanceDashboardFilter filter, DashboardScope scope, CancellationToken ct)
    {
        // Employees in scope for THIS cycle: enrolled participants if any exist, else all tenant employees
        // (legacy cycles created before participant snapshotting). Then apply HR/manager scope + filters.
        var participantIds = await _dbContext.CycleParticipants.AsNoTracking()
            .Where(p => p.CycleId == cycleId)
            .Select(p => p.EmployeeId)
            .ToListAsync(ct);
        // Use List (not HashSet) for the EF .Contains predicates below — EF translates List.Contains to a
        // SQL IN reliably (and the InMemory provider only handles List.Contains), whereas a captured
        // HashSet/IReadOnlySet .Contains is not translated and would silently filter out every row.
        var participantList = participantIds.Count > 0 ? participantIds : null;

        // Optional grade filter resolves to the set of job-title ids carrying that grade (tenant-scoped).
        List<Guid>? jobTitleIdsForGrade = null;
        if (filter.GradeId is { } gradeId)
        {
            jobTitleIdsForGrade = await _dbContext.JobTitles.AsNoTracking()
                .Where(jt => jt.GradeId == gradeId)
                .Select(jt => jt.Id)
                .ToListAsync(ct);
        }

        EmploymentType? employmentType = ParseEmploymentType(filter.EmploymentType);

        var employeesQuery = _dbContext.Employees.AsNoTracking().Where(e => e.IsActive);

        if (scope.RestrictEmployeeIds is not null)
        {
            // Manager scope (BR-1/BR-3): hard-restrict to the caller's direct reports.
            var restrict = scope.RestrictEmployeeIds.ToList();
            employeesQuery = employeesQuery.Where(e => restrict.Contains(e.Id));
        }

        if (participantList is not null)
            employeesQuery = employeesQuery.Where(e => participantList.Contains(e.Id));

        if (filter.DepartmentId is { } deptId)
            employeesQuery = employeesQuery.Where(e => e.DepartmentId == deptId);

        if (jobTitleIdsForGrade is not null)
        {
            var jtIds = jobTitleIdsForGrade;
            employeesQuery = employeesQuery.Where(e => jtIds.Contains(e.JobTitleId));
        }

        if (employmentType is { } et)
            employeesQuery = employeesQuery.Where(e => e.EmploymentType == et);

        if (filter.LocationId is { } locId)
            employeesQuery = employeesQuery.Where(e => e.LocationId == locId);

        if (!filter.IncludeProbation)
            employeesQuery = employeesQuery.Where(e => e.Status != EmployeeStatus.Probation);

        var employeeRows = await employeesQuery
            .Select(e => new
            {
                e.Id,
                e.FirstName,
                e.LastName,
                e.EmployeeNo,
                e.DepartmentId,
                e.JobTitleId,
            })
            .ToListAsync(ct);

        if (employeeRows.Count == 0)
            return new List<EmployeeScoreRow>();

        var employeeIds = employeeRows.Select(e => e.Id).ToList();

        // Resolve department + job-title names via separate tenant-scoped lookups (avoids a required-navigation
        // join whose related query filter can interfere with the projection).
        var deptIds = employeeRows.Select(e => e.DepartmentId).Distinct().ToList();
        var deptNames = (await _dbContext.Departments.AsNoTracking()
                .Where(d => deptIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(ct))
            .ToDictionary(d => d.Id, d => d.Name);

        var jobTitleIds = employeeRows.Select(e => e.JobTitleId).Distinct().ToList();
        var jobTitleNames = (await _dbContext.JobTitles.AsNoTracking()
                .Where(jt => jobTitleIds.Contains(jt.Id))
                .Select(jt => new { jt.Id, jt.TitleName })
                .ToListAsync(ct))
            .ToDictionary(jt => jt.Id, jt => jt.TitleName);

        var employees = employeeRows.Select(e => new
        {
            e.Id,
            e.FirstName,
            e.LastName,
            e.EmployeeNo,
            e.DepartmentId,
            DepartmentName = deptNames.GetValueOrDefault(e.DepartmentId, string.Empty),
            JobTitle = jobTitleNames.GetValueOrDefault(e.JobTitleId, string.Empty),
        }).ToList();

        // Manager reviews for the cycle/population (final scores + submitted flag + sign-off).
        var reviews = (await _dbContext.ManagerReviews.AsNoTracking()
                .Where(r => r.CycleId == cycleId && employeeIds.Contains(r.EmployeeId))
                .Select(r => new { r.EmployeeId, r.Status, r.FinalScore, r.SignoffStatus })
                .ToListAsync(ct))
            .ToDictionary(r => r.EmployeeId);

        // Self-assessments submitted.
        var selfSubmitted = (await _dbContext.SelfAssessments.AsNoTracking()
                .Where(s => s.CycleId == cycleId && employeeIds.Contains(s.EmployeeId)
                    && s.Status == SelfAssessmentStatus.Submitted)
                .Select(s => s.EmployeeId)
                .ToListAsync(ct))
            .ToHashSet();

        // Employees with at least one goal (goal-setting complete).
        var withGoals = (await _dbContext.Goals.AsNoTracking()
                .Where(g => g.CycleId == cycleId && employeeIds.Contains(g.EmployeeId))
                .Select(g => g.EmployeeId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        return employees.Select(e =>
        {
            reviews.TryGetValue(e.Id, out var review);
            var managerSubmitted = review is { Status: ManagerReviewStatus.Submitted };
            // BR-4: only a SUBMITTED review's final score counts as a score.
            var finalScore = managerSubmitted ? review!.FinalScore : null;
            var signedOff = review is { SignoffStatus: ReviewSignoffStatus.SignedOff };

            return new EmployeeScoreRow(
                e.Id,
                $"{e.FirstName} {e.LastName}".Trim(),
                e.EmployeeNo,
                e.DepartmentId,
                e.DepartmentName,
                e.JobTitle,
                finalScore,
                withGoals.Contains(e.Id),
                selfSubmitted.Contains(e.Id),
                managerSubmitted,
                signedOff);
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════════
    //  Aggregations
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Histogram buckets across the rating scale (AC-1/FR-1): one unit-wide bucket per integer band
    /// (1–2, 2–3, …) up to the scale max. The top bucket is inclusive of the max (a perfect score lands in it).
    /// </summary>
    private static List<ScoreDistributionBucketDto> BuildDistribution(
        IReadOnlyList<EmployeeScoreRow> scored, int ratingScaleMax)
    {
        var buckets = new List<ScoreDistributionBucketDto>();
        var max = Math.Max(1, ratingScaleMax);
        for (int lo = 1; lo < max; lo++)
        {
            int hi = lo + 1;
            bool isTop = hi == max;
            var count = scored.Count(e =>
            {
                var s = e.FinalScore!.Value;
                return isTop ? s >= lo && s <= hi : s >= lo && s < hi;
            });
            buckets.Add(new ScoreDistributionBucketDto
            {
                RangeStart = lo,
                RangeEnd = hi,
                Label = $"{lo:0.0}–{hi:0.0}",
                Count = count,
            });
        }

        // Scale max == 1 (degenerate): a single 0–1 bucket so the chart still renders.
        if (buckets.Count == 0)
            buckets.Add(new ScoreDistributionBucketDto
            {
                RangeStart = 0, RangeEnd = 1, Label = "0.0–1.0",
                Count = scored.Count(e => e.FinalScore!.Value <= 1),
            });

        return buckets;
    }

    private static List<DepartmentAverageDto> BuildDepartmentAverages(IReadOnlyList<EmployeeScoreRow> scored)
        => scored
            .GroupBy(e => new { e.DepartmentId, e.DepartmentName })
            .Select(g => new DepartmentAverageDto
            {
                DepartmentId = g.Key.DepartmentId,
                DepartmentName = g.Key.DepartmentName,
                Headcount = g.Count(),
                AverageScore = Round(g.Average(e => e.FinalScore!.Value)),
            })
            .OrderByDescending(d => d.AverageScore)
            .ThenBy(d => d.DepartmentName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static (List<PerformerDto> Top, List<PerformerDto> Bottom) BuildPerformerLists(
        IReadOnlyList<EmployeeScoreRow> scored, int requestedCount, DashboardScope scope)
    {
        var n = Math.Clamp(requestedCount <= 0 ? 10 : requestedCount, 1, 100);

        var ordered = scored
            .OrderByDescending(e => e.FinalScore!.Value)
            .ThenBy(e => e.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var top = ordered.Take(n).Select(ToPerformer).ToList();

        // BR-3: bottom performers are an ORG-WIDE concept (HR only). For a manager (Team scope) the "top"
        // list IS the team ranking; bottom is intentionally empty.
        if (scope.Kind == PerformanceDashboardScope.Team)
            return (top, new List<PerformerDto>());

        var bottom = ordered
            .AsEnumerable()
            .Reverse()
            .Take(n)
            .Select(ToPerformer)
            .ToList();

        return (top, bottom);
    }

    private static PerformerDto ToPerformer(EmployeeScoreRow e) => new()
    {
        EmployeeId = e.EmployeeId,
        EmployeeName = e.EmployeeName,
        EmployeeNo = e.EmployeeNo,
        DepartmentId = e.DepartmentId,
        DepartmentName = e.DepartmentName,
        Score = e.FinalScore!.Value,
    };

    private static CycleProgressDto BuildProgress(IReadOnlyList<EmployeeScoreRow> population)
    {
        var total = population.Count;
        var goalSetting = population.Count(e => e.HasGoals);
        var self = population.Count(e => e.SelfSubmitted);
        var manager = population.Count(e => e.ManagerSubmitted);
        var signed = population.Count(e => e.SignedOff);
        var completion = total == 0 ? 0m : Round(manager * 100m / total);

        return new CycleProgressDto
        {
            TotalParticipants = total,
            GoalSettingCompleted = goalSetting,
            SelfAssessmentCompleted = self,
            ManagerReviewCompleted = manager,
            SignedOff = signed,
            CompletionRate = completion,
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Export rendering
    // ══════════════════════════════════════════════════════════════

    private static (byte[] Content, string FileName, string ContentType) RenderExport(
        string format, PerformanceDashboardDto d)
    {
        var baseName = $"performance-dashboard-{d.CycleId:N}";
        return format switch
        {
            "xlsx" => (RenderXlsx(d), $"{baseName}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            _ => (RenderCsv(d), $"{baseName}.csv", "text/csv"),
        };
    }

    private static byte[] RenderCsv(PerformanceDashboardDto d)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Performance Dashboard");
        sb.AppendLine($"Cycle,{Csv(d.CycleName)}");
        sb.AppendLine($"Scope,{d.Scope}");
        sb.AppendLine($"Average Score,{Num(d.AverageScore)}");
        sb.AppendLine($"Scored Employees,{d.ScoredEmployeeCount}");
        sb.AppendLine();

        sb.AppendLine("Cycle Progress");
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"Total Participants,{d.Progress.TotalParticipants}");
        sb.AppendLine($"Goal Setting Completed,{d.Progress.GoalSettingCompleted}");
        sb.AppendLine($"Self Assessment Completed,{d.Progress.SelfAssessmentCompleted}");
        sb.AppendLine($"Manager Review Completed,{d.Progress.ManagerReviewCompleted}");
        sb.AppendLine($"Signed Off,{d.Progress.SignedOff}");
        sb.AppendLine($"Completion Rate (%),{Num(d.Progress.CompletionRate)}");
        sb.AppendLine();

        sb.AppendLine("Score Distribution");
        sb.AppendLine("Range,Count");
        foreach (var b in d.ScoreDistribution)
            sb.AppendLine($"{Csv(b.Label)},{b.Count}");
        sb.AppendLine();

        sb.AppendLine("Department Averages");
        sb.AppendLine("Department,Headcount,Average Score");
        foreach (var dep in d.DepartmentAverages)
            sb.AppendLine($"{Csv(dep.DepartmentName)},{dep.Headcount},{Num(dep.AverageScore)}");
        sb.AppendLine();

        sb.AppendLine("Top Performers");
        sb.AppendLine("Employee,Employee No,Department,Score");
        foreach (var p in d.TopPerformers)
            sb.AppendLine($"{Csv(p.EmployeeName)},{Csv(p.EmployeeNo)},{Csv(p.DepartmentName)},{Num(p.Score)}");

        if (d.BottomPerformers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Bottom Performers");
            sb.AppendLine("Employee,Employee No,Department,Score");
            foreach (var p in d.BottomPerformers)
                sb.AppendLine($"{Csv(p.EmployeeName)},{Csv(p.EmployeeNo)},{Csv(p.DepartmentName)},{Num(p.Score)}");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] RenderXlsx(PerformanceDashboardDto d)
    {
        using var workbook = new XLWorkbook();

        var overview = workbook.Worksheets.Add("Overview");
        overview.Cell(1, 1).Value = "Metric"; overview.Cell(1, 2).Value = "Value";
        overview.Range(1, 1, 1, 2).Style.Font.Bold = true;
        var overviewRows = new (string, string)[]
        {
            ("Cycle", d.CycleName),
            ("Scope", d.Scope),
            ("Average Score", Num(d.AverageScore)),
            ("Scored Employees", d.ScoredEmployeeCount.ToString(CultureInfo.InvariantCulture)),
            ("Total Participants", d.Progress.TotalParticipants.ToString(CultureInfo.InvariantCulture)),
            ("Goal Setting Completed", d.Progress.GoalSettingCompleted.ToString(CultureInfo.InvariantCulture)),
            ("Self Assessment Completed", d.Progress.SelfAssessmentCompleted.ToString(CultureInfo.InvariantCulture)),
            ("Manager Review Completed", d.Progress.ManagerReviewCompleted.ToString(CultureInfo.InvariantCulture)),
            ("Signed Off", d.Progress.SignedOff.ToString(CultureInfo.InvariantCulture)),
            ("Completion Rate (%)", Num(d.Progress.CompletionRate)),
        };
        for (int i = 0; i < overviewRows.Length; i++)
        {
            overview.Cell(i + 2, 1).Value = overviewRows[i].Item1;
            overview.Cell(i + 2, 2).Value = overviewRows[i].Item2;
        }
        overview.Columns().AdjustToContents();

        var dist = workbook.Worksheets.Add("Distribution");
        dist.Cell(1, 1).Value = "Range"; dist.Cell(1, 2).Value = "Count";
        dist.Range(1, 1, 1, 2).Style.Font.Bold = true;
        int dr = 2;
        foreach (var b in d.ScoreDistribution)
        {
            dist.Cell(dr, 1).Value = b.Label;
            dist.Cell(dr, 2).Value = b.Count;
            dr++;
        }
        dist.Columns().AdjustToContents();

        var depts = workbook.Worksheets.Add("Departments");
        depts.Cell(1, 1).Value = "Department"; depts.Cell(1, 2).Value = "Headcount"; depts.Cell(1, 3).Value = "Average Score";
        depts.Range(1, 1, 1, 3).Style.Font.Bold = true;
        int depr = 2;
        foreach (var dep in d.DepartmentAverages)
        {
            depts.Cell(depr, 1).Value = dep.DepartmentName;
            depts.Cell(depr, 2).Value = dep.Headcount;
            depts.Cell(depr, 3).Value = Num(dep.AverageScore);
            depr++;
        }
        depts.Columns().AdjustToContents();

        WritePerformerSheet(workbook, "Top Performers", d.TopPerformers);
        if (d.BottomPerformers.Count > 0)
            WritePerformerSheet(workbook, "Bottom Performers", d.BottomPerformers);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WritePerformerSheet(XLWorkbook workbook, string name, IReadOnlyList<PerformerDto> performers)
    {
        var ws = workbook.Worksheets.Add(name);
        ws.Cell(1, 1).Value = "Employee"; ws.Cell(1, 2).Value = "Employee No";
        ws.Cell(1, 3).Value = "Department"; ws.Cell(1, 4).Value = "Score";
        ws.Range(1, 1, 1, 4).Style.Font.Bold = true;
        int r = 2;
        foreach (var p in performers)
        {
            ws.Cell(r, 1).Value = p.EmployeeName;
            ws.Cell(r, 2).Value = p.EmployeeNo;
            ws.Cell(r, 3).Value = p.DepartmentName;
            ws.Cell(r, 4).Value = Num(p.Score);
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>Resolves the cycle to report on: the explicit id, else the tenant's most recently started cycle.</summary>
    private async Task<Domain.Performance.AppraisalCycle?> ResolveCycleAsync(Guid? cycleId, CancellationToken ct)
    {
        if (cycleId is { } id)
            return await _dbContext.AppraisalCycles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, ct);

        return await _dbContext.AppraisalCycles.AsNoTracking()
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync(ct);
    }

    private static EmploymentType? ParseEmploymentType(string? value)
        => Enum.TryParse<EmploymentType>(value, ignoreCase: true, out var et) ? et : null;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string? NormalizeFormat(string? format)
    {
        var f = format?.Trim().ToLowerInvariant();
        return f switch
        {
            "csv" => "csv",
            "xlsx" or "excel" => "xlsx",
            _ => null,
        };
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
