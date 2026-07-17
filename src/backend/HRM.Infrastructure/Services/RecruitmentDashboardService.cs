using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Recruitment dashboard + analytics service (US-REC-009). See <see cref="IRecruitmentDashboardService"/>
/// for the full responsibility/deferral list. Every query runs under the EF global query filter
/// (TenantId == tenant) so the whole dashboard is tenant-isolated (AC-5) — no Postgres RLS (deferred).
/// The pure formulas live in <see cref="RecruitmentAnalytics"/> (BR-1/BR-2/BR-3). Data is loaded in a
/// handful of bulk queries (no per-applicant round-trips) and projected to DTOs.
/// </summary>
public sealed class RecruitmentDashboardService : IRecruitmentDashboardService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RecruitmentDashboardService> _logger;

    /// <summary>The pipeline funnel order (FR-2/AC-3). Rejected is NOT a funnel stage.</summary>
    private static readonly ApplicantStage[] FunnelStages =
    {
        ApplicantStage.Applied,
        ApplicantStage.Screening,
        ApplicantStage.Interview,
        ApplicantStage.Offer,
        ApplicantStage.Hired,
    };

    private const int RecentActivityCap = 20;

    public RecruitmentDashboardService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<RecruitmentDashboardService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<RecruitmentDashboardDto>> GetDashboardAsync(
        RecruitmentDashboardFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecruitmentDashboardDto>.Failure("Tenant context is not resolved.", 400);

        var (from, to) = ResolveRange(filter);
        if (to < from)
            return Result<RecruitmentDashboardDto>.Failure(
                "The 'to' date must be on or after the 'from' date.", 400, "invalid_range");

        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtcExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // ── Resolve the candidate vacancy set for the optional dept/vacancy filters (FR-7). ──
        // null = no vacancy restriction (all vacancies in the tenant).
        HashSet<Guid>? vacancyScope = await ResolveVacancyScopeAsync(filter, cancellationToken);

        // ── Applicants in scope (current state + applied_at), filtered by period for period metrics. ──
        var applicantsQuery = _dbContext.Applicants.AsNoTracking();
        if (vacancyScope is not null)
            applicantsQuery = applicantsQuery.Where(a => vacancyScope.Contains(a.VacancyId));

        var periodApplicants = await applicantsQuery
            .Where(a => a.AppliedAt >= fromUtc && a.AppliedAt < toUtcExclusive)
            .Select(a => new ApplicantRow(a.Id, a.VacancyId, a.Source, a.Stage, a.AppliedAt, a.FirstName, a.LastName))
            .ToListAsync(cancellationToken);

        // ── Stage history in scope (drives funnel reach + BR-1 hire timestamps). ──
        var historyQuery = _dbContext.ApplicantStageHistories.AsNoTracking();
        if (vacancyScope is not null)
        {
            // Restrict history to applicants belonging to in-scope vacancies.
            var scopedApplicantIds = await applicantsQuery.Select(a => a.Id).ToListAsync(cancellationToken);
            var scopedSet = scopedApplicantIds.ToHashSet();
            var allHistory = await historyQuery
                .Select(h => new HistoryRow(h.ApplicantId, h.FromStage, h.ToStage, h.ChangedAt))
                .ToListAsync(cancellationToken);
            return await BuildAsync(filter, from, to, fromUtc, toUtcExclusive, periodApplicants,
                allHistory.Where(h => scopedSet.Contains(h.ApplicantId)).ToList(), vacancyScope, cancellationToken);
        }

        var history = await historyQuery
            .Select(h => new HistoryRow(h.ApplicantId, h.FromStage, h.ToStage, h.ChangedAt))
            .ToListAsync(cancellationToken);

        return await BuildAsync(filter, from, to, fromUtc, toUtcExclusive, periodApplicants, history,
            vacancyScope, cancellationToken);
    }

    private async Task<Result<RecruitmentDashboardDto>> BuildAsync(
        RecruitmentDashboardFilter filter, DateOnly from, DateOnly to,
        DateTime fromUtc, DateTime toUtcExclusive,
        List<ApplicantRow> periodApplicants, List<HistoryRow> history,
        HashSet<Guid>? vacancyScope, CancellationToken ct)
    {
        // ── Vacancy status summary (FR-5) + open-vacancy KPI (live state, not period). ──
        var vacancyQuery = _dbContext.Vacancies.AsNoTracking();
        if (vacancyScope is not null)
            vacancyQuery = vacancyQuery.Where(v => vacancyScope.Contains(v.Id));

        var vacancyStatuses = await vacancyQuery
            .GroupBy(v => v.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var vacancyStatusByEnum = vacancyStatuses.ToDictionary(x => x.Status, x => x.Count);

        int openVacancies = vacancyStatusByEnum.GetValueOrDefault(VacancyStatus.Open, 0);

        var vacancyTitles = await vacancyQuery
            .Select(v => new { v.Id, v.Title })
            .ToListAsync(ct);
        var vacancyTitleById = vacancyTitles.ToDictionary(x => x.Id, x => x.Title);

        // ── Offers (BR-2 acceptance, offers-pending KPI, recent activity). ──
        var offerQuery = _dbContext.Offers.AsNoTracking();
        if (vacancyScope is not null)
            offerQuery = offerQuery.Where(o => vacancyScope.Contains(o.VacancyId));

        var offers = await offerQuery
            .Select(o => new OfferRow(o.Id, o.ApplicantId, o.VacancyId, o.Status, o.SentAt, o.RespondedAt, o.Response))
            .ToListAsync(ct);

        // Offers SENT within the period (BR-2 denominator); accepted = of those, status Accepted.
        var sentInPeriod = offers
            .Where(o => o.SentAt is { } sent && sent >= fromUtc && sent < toUtcExclusive)
            .ToList();
        int sentCount = sentInPeriod.Count;
        int acceptedCount = sentInPeriod.Count(o => o.Status == OfferStatus.Accepted);
        decimal acceptanceRate = RecruitmentAnalytics.AcceptanceRate(acceptedCount, sentCount);
        int offersPending = offers.Count(o => o.Status == OfferStatus.Sent);

        // ── BR-1 hire timestamps: the moment each applicant entered the Hired stage. ──
        // The earliest history row whose ToStage == Hired per applicant.
        var hiredAtByApplicant = history
            .Where(h => h.ToStage == ApplicantStage.Hired)
            .GroupBy(h => h.ApplicantId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.ChangedAt));

        // applied_at per applicant — need it for hires that may have applied before the period.
        var appliedAtByApplicant = await AppliedAtLookupAsync(hiredAtByApplicant.Keys, vacancyScope, ct);

        // Hires within the period = entered Hired within [from, to].
        var periodHires = hiredAtByApplicant
            .Where(kvp => kvp.Value >= fromUtc && kvp.Value < toUtcExclusive)
            .ToList();
        int hires = periodHires.Count;

        // Time-to-hire spans for period hires (BR-1).
        var tthSpans = new List<int>();
        foreach (var (applicantId, hiredAt) in periodHires)
            if (appliedAtByApplicant.TryGetValue(applicantId, out var appliedAt))
                tthSpans.Add(RecruitmentAnalytics.TimeToHireDays(appliedAt, hiredAt));
        decimal avgTth = RecruitmentAnalytics.AverageDays(tthSpans);

        var kpis = new RecruitmentKpiDto
        {
            OpenVacancies = openVacancies,
            TotalApplicants = periodApplicants.Count,
            Hires = hires,
            AverageTimeToHireDays = avgTth,
            OfferAcceptanceRate = acceptanceRate,
            OffersPending = offersPending,
        };

        // ── Funnel (FR-2/BR-3): count of period applicants that REACHED each stage. ──
        var funnel = BuildFunnel(periodApplicants, history);

        // ── Source effectiveness (FR-3/BR-6): period applicants by source + hire conversion. ──
        var sources = BuildSources(periodApplicants, hiredAtByApplicant);

        // ── Time-to-hire trend (FR-4): avg days per week/month bucket over the period. ──
        // Month buckets when the range spans > 90 days, otherwise weekly (keeps the chart readable).
        var granularity = (to.DayNumber - from.DayNumber) > 90 ? "month" : "week";
        var trend = BuildTrend(periodHires, appliedAtByApplicant, granularity);

        // ── Vacancy status summary (FR-5): one row per enum status, incl. zero-count. ──
        var vacancyStatusSummary = Enum.GetValues<VacancyStatus>()
            .Select(s => new VacancyStatusCountDto
            {
                Status = s.ToString(),
                Count = vacancyStatusByEnum.GetValueOrDefault(s, 0),
            })
            .ToList();

        // ── Recent activity (FR-9): newest recruitment events, capped. ──
        var recent = BuildRecentActivity(history, offers, periodApplicants, vacancyTitleById);
        var recentResolved = await ResolveActivityNamesAsync(recent, vacancyTitleById, ct);

        var dto = new RecruitmentDashboardDto
        {
            From = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Kpis = kpis,
            Funnel = funnel,
            Sources = sources,
            TimeToHireTrend = trend,
            VacancyStatusSummary = vacancyStatusSummary,
            RecentActivity = recentResolved,
        };

        return Result<RecruitmentDashboardDto>.Success(dto);
    }

    // ══════════════════════════════════════════════════════════════
    //  Funnel (FR-2/BR-3)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Counts how many of the period's applicants REACHED each funnel stage, then computes the
    /// adjacent-stage conversion % (BR-3). "Reached stage S" = current stage index ≥ S OR a history row
    /// with ToStage == S exists for the applicant. Applied is reached by everyone in the period.
    /// </summary>
    private static List<FunnelStageDto> BuildFunnel(
        List<ApplicantRow> periodApplicants, List<HistoryRow> history)
    {
        var periodIds = periodApplicants.Select(a => a.Id).ToHashSet();

        // Stages each applicant reached via history (ToStage), restricted to the period applicants.
        var reachedByApplicant = new Dictionary<Guid, HashSet<ApplicantStage>>();
        foreach (var h in history)
        {
            if (!periodIds.Contains(h.ApplicantId)) continue;
            if (!reachedByApplicant.TryGetValue(h.ApplicantId, out var set))
                reachedByApplicant[h.ApplicantId] = set = new HashSet<ApplicantStage>();
            set.Add(h.ToStage);
        }

        var counts = new int[FunnelStages.Length];
        foreach (var a in periodApplicants)
        {
            var reached = reachedByApplicant.GetValueOrDefault(a.Id);
            for (int i = 0; i < FunnelStages.Length; i++)
            {
                var stage = FunnelStages[i];
                bool reachedByCurrent = StageRank(a.Stage) >= StageRank(stage);
                bool reachedByHistory = reached is not null && reached.Contains(stage);
                if (reachedByCurrent || reachedByHistory)
                    counts[i]++;
            }
        }

        var result = new List<FunnelStageDto>(FunnelStages.Length);
        for (int i = 0; i < FunnelStages.Length; i++)
        {
            decimal? conv = i == 0 ? null : RecruitmentAnalytics.ConversionRate(counts[i], counts[i - 1]);
            result.Add(new FunnelStageDto
            {
                Stage = FunnelStages[i].ToString(),
                Count = counts[i],
                ConversionRate = conv,
            });
        }
        return result;
    }

    /// <summary>
    /// Rank of a stage within the linear funnel (Applied=0..Hired=4). Rejected is OFF the funnel and
    /// ranks as -1 (a rejected applicant has not "reached" any funnel stage by virtue of current state;
    /// their history rows still credit the stages they passed through).
    /// </summary>
    private static int StageRank(ApplicantStage stage)
    {
        var idx = Array.IndexOf(FunnelStages, stage);
        return idx; // -1 for Rejected (not in the array).
    }

    // ══════════════════════════════════════════════════════════════
    //  Source effectiveness (FR-3/BR-6)
    // ══════════════════════════════════════════════════════════════

    private static List<SourceEffectivenessDto> BuildSources(
        List<ApplicantRow> periodApplicants, Dictionary<Guid, DateTime> hiredAtByApplicant)
    {
        var result = new List<SourceEffectivenessDto>();
        // Skip the Unknown sentinel (ISSUE-231): it is not a real acquisition channel, only the read-time
        // fallback for a corrupt source string, so it must not appear as a source-effectiveness line.
        foreach (var source in Enum.GetValues<ApplicationSource>().Where(s => s != ApplicationSource.Unknown))
        {
            var inSource = periodApplicants.Where(a => a.Source == source).ToList();
            if (inSource.Count == 0) continue; // omit sources with no applicants in the period.

            int applicantCount = inSource.Count;
            int hireCount = inSource.Count(a => hiredAtByApplicant.ContainsKey(a.Id));
            result.Add(new SourceEffectivenessDto
            {
                Source = source.ToString(),
                ApplicantCount = applicantCount,
                HireCount = hireCount,
                ConversionRate = RecruitmentAnalytics.SourceConversionRate(hireCount, applicantCount),
            });
        }
        return result.OrderByDescending(s => s.ApplicantCount).ToList();
    }

    // ══════════════════════════════════════════════════════════════
    //  Time-to-hire trend (FR-4)
    // ══════════════════════════════════════════════════════════════

    private static List<TimeToHireTrendPointDto> BuildTrend(
        List<KeyValuePair<Guid, DateTime>> periodHires,
        Dictionary<Guid, DateTime> appliedAtByApplicant,
        string granularity)
    {
        // bucket -> list of day-spans.
        var buckets = new Dictionary<string, List<int>>();
        foreach (var (applicantId, hiredAt) in periodHires)
        {
            if (!appliedAtByApplicant.TryGetValue(applicantId, out var appliedAt)) continue;
            var bucket = RecruitmentAnalytics.TrendBucket(hiredAt, granularity);
            if (!buckets.TryGetValue(bucket, out var list))
                buckets[bucket] = list = new List<int>();
            list.Add(RecruitmentAnalytics.TimeToHireDays(appliedAt, hiredAt));
        }

        return buckets
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => new TimeToHireTrendPointDto
            {
                Period = kvp.Key,
                AverageDays = RecruitmentAnalytics.AverageDays(kvp.Value),
                HireCount = kvp.Value.Count,
            })
            .ToList();
    }

    // ══════════════════════════════════════════════════════════════
    //  Recent activity (FR-9)
    // ══════════════════════════════════════════════════════════════

    private static List<RecruitmentActivityDto> BuildRecentActivity(
        List<HistoryRow> history, List<OfferRow> offers, List<ApplicantRow> periodApplicants,
        Dictionary<Guid, string> vacancyTitleById)
    {
        var events = new List<RecruitmentActivityDto>();

        // New applicants (Applied) within the loaded period set.
        foreach (var a in periodApplicants)
        {
            events.Add(new RecruitmentActivityDto
            {
                Type = "Applied",
                Description = $"{FullName(a.FirstName, a.LastName)} applied",
                OccurredAt = a.AppliedAt,
                ApplicantId = a.Id,
                ApplicantName = FullName(a.FirstName, a.LastName),
                VacancyId = a.VacancyId,
                VacancyTitle = vacancyTitleById.GetValueOrDefault(a.VacancyId),
            });
        }

        // Stage changes (and hires) from the stage history.
        foreach (var h in history)
        {
            bool isHire = h.ToStage == ApplicantStage.Hired;
            events.Add(new RecruitmentActivityDto
            {
                Type = isHire ? "Hired" : "StageChanged",
                Description = isHire
                    ? "Applicant hired"
                    : $"Moved to {h.ToStage}",
                OccurredAt = h.ChangedAt,
                ApplicantId = h.ApplicantId,
                VacancyId = null,
                VacancyTitle = null,
            });
        }

        // Offers sent.
        foreach (var o in offers.Where(o => o.SentAt is not null))
        {
            events.Add(new RecruitmentActivityDto
            {
                Type = "OfferSent",
                Description = "Offer sent",
                OccurredAt = o.SentAt!.Value,
                ApplicantId = o.ApplicantId,
                VacancyId = o.VacancyId,
                VacancyTitle = vacancyTitleById.GetValueOrDefault(o.VacancyId),
            });
        }

        return events
            .OrderByDescending(e => e.OccurredAt)
            .Take(RecentActivityCap)
            .ToList();
    }

    /// <summary>
    /// Fills in applicant display names + vacancy titles for the (already capped) activity feed in one
    /// extra query, so we don't load every applicant just to label 20 events.
    /// </summary>
    private async Task<List<RecruitmentActivityDto>> ResolveActivityNamesAsync(
        List<RecruitmentActivityDto> events, Dictionary<Guid, string> vacancyTitleById, CancellationToken ct)
    {
        var missingApplicantIds = events
            .Where(e => e.ApplicantId is not null && string.IsNullOrEmpty(e.ApplicantName))
            .Select(e => e.ApplicantId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, (string Name, Guid VacancyId)> applicantInfo = new();
        if (missingApplicantIds.Count > 0)
        {
            applicantInfo = (await _dbContext.Applicants.AsNoTracking()
                    .Where(a => missingApplicantIds.Contains(a.Id))
                    .Select(a => new { a.Id, a.FirstName, a.LastName, a.VacancyId })
                    .ToListAsync(ct))
                .ToDictionary(a => a.Id, a => (FullName(a.FirstName, a.LastName), a.VacancyId));
        }

        return events.Select(e =>
        {
            if (e.ApplicantId is { } id && applicantInfo.TryGetValue(id, out var info))
            {
                return e with
                {
                    ApplicantName = string.IsNullOrEmpty(e.ApplicantName) ? info.Name : e.ApplicantName,
                    VacancyId = e.VacancyId ?? info.VacancyId,
                    VacancyTitle = e.VacancyTitle ?? vacancyTitleById.GetValueOrDefault(info.VacancyId),
                };
            }
            return e;
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════════
    //  Export (FR-8): CSV + XLSX (ClosedXML). PDF deferred.
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<RecruitmentDashboardExportResult>> ExportDashboardAsync(
        RecruitmentDashboardFilter filter, string format, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RecruitmentDashboardExportResult>.Failure("Tenant context is not resolved.", 400);

        var normalized = NormalizeFormat(format);
        if (normalized is null)
            return Result<RecruitmentDashboardExportResult>.Failure(
                "Export format must be one of csv, xlsx.", 400, "invalid_format");

        var dashboard = await GetDashboardAsync(filter, cancellationToken);
        if (dashboard.IsFailure)
            return Result<RecruitmentDashboardExportResult>.Failure(
                dashboard.Error!, dashboard.StatusCode ?? 400, dashboard.ErrorCode);

        var (content, fileName, contentType) = RenderExport(normalized, dashboard.Value!);
        return Result<RecruitmentDashboardExportResult>.Success(new RecruitmentDashboardExportResult
        {
            FileContent = content,
            FileName = fileName,
            ContentType = contentType,
        });
    }

    private static (byte[] Content, string FileName, string ContentType) RenderExport(
        string format, RecruitmentDashboardDto d)
    {
        var baseName = $"recruitment-dashboard-{d.From.Replace("-", string.Empty)}-{d.To.Replace("-", string.Empty)}";
        return format switch
        {
            "xlsx" => (RenderXlsx(d), $"{baseName}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            _ => (RenderCsv(d), $"{baseName}.csv", "text/csv"),
        };
    }

    private static byte[] RenderCsv(RecruitmentDashboardDto d)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Recruitment Dashboard");
        sb.AppendLine($"Period,{d.From} to {d.To}");
        sb.AppendLine();

        sb.AppendLine("KPIs");
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"Open Vacancies,{d.Kpis.OpenVacancies}");
        sb.AppendLine($"Total Applicants,{d.Kpis.TotalApplicants}");
        sb.AppendLine($"Hires,{d.Kpis.Hires}");
        sb.AppendLine($"Average Time-to-Hire (days),{Num(d.Kpis.AverageTimeToHireDays)}");
        sb.AppendLine($"Offer Acceptance Rate (%),{Num(d.Kpis.OfferAcceptanceRate)}");
        sb.AppendLine($"Offers Pending,{d.Kpis.OffersPending}");
        sb.AppendLine();

        sb.AppendLine("Funnel");
        sb.AppendLine("Stage,Count,Conversion %");
        foreach (var f in d.Funnel)
            sb.AppendLine($"{Csv(f.Stage)},{f.Count},{(f.ConversionRate is { } c ? Num(c) : string.Empty)}");
        sb.AppendLine();

        sb.AppendLine("Source Effectiveness");
        sb.AppendLine("Source,Applicants,Hires,Conversion %");
        foreach (var s in d.Sources)
            sb.AppendLine($"{Csv(s.Source)},{s.ApplicantCount},{s.HireCount},{Num(s.ConversionRate)}");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] RenderXlsx(RecruitmentDashboardDto d)
    {
        using var workbook = new XLWorkbook();

        var kpi = workbook.Worksheets.Add("KPIs");
        kpi.Cell(1, 1).Value = "Metric"; kpi.Cell(1, 2).Value = "Value";
        kpi.Range(1, 1, 1, 2).Style.Font.Bold = true;
        var kpiRows = new (string, string)[]
        {
            ("Open Vacancies", d.Kpis.OpenVacancies.ToString(CultureInfo.InvariantCulture)),
            ("Total Applicants", d.Kpis.TotalApplicants.ToString(CultureInfo.InvariantCulture)),
            ("Hires", d.Kpis.Hires.ToString(CultureInfo.InvariantCulture)),
            ("Average Time-to-Hire (days)", Num(d.Kpis.AverageTimeToHireDays)),
            ("Offer Acceptance Rate (%)", Num(d.Kpis.OfferAcceptanceRate)),
            ("Offers Pending", d.Kpis.OffersPending.ToString(CultureInfo.InvariantCulture)),
        };
        for (int i = 0; i < kpiRows.Length; i++)
        {
            kpi.Cell(i + 2, 1).Value = kpiRows[i].Item1;
            kpi.Cell(i + 2, 2).Value = kpiRows[i].Item2;
        }
        kpi.Columns().AdjustToContents();

        var funnel = workbook.Worksheets.Add("Funnel");
        funnel.Cell(1, 1).Value = "Stage"; funnel.Cell(1, 2).Value = "Count"; funnel.Cell(1, 3).Value = "Conversion %";
        funnel.Range(1, 1, 1, 3).Style.Font.Bold = true;
        int fr = 2;
        foreach (var f in d.Funnel)
        {
            funnel.Cell(fr, 1).Value = f.Stage;
            funnel.Cell(fr, 2).Value = f.Count;
            funnel.Cell(fr, 3).Value = f.ConversionRate is { } c ? Num(c) : string.Empty;
            fr++;
        }
        funnel.Columns().AdjustToContents();

        var sources = workbook.Worksheets.Add("Sources");
        sources.Cell(1, 1).Value = "Source"; sources.Cell(1, 2).Value = "Applicants";
        sources.Cell(1, 3).Value = "Hires"; sources.Cell(1, 4).Value = "Conversion %";
        sources.Range(1, 1, 1, 4).Style.Font.Bold = true;
        int sr = 2;
        foreach (var s in d.Sources)
        {
            sources.Cell(sr, 1).Value = s.Source;
            sources.Cell(sr, 2).Value = s.ApplicantCount;
            sources.Cell(sr, 3).Value = s.HireCount;
            sources.Cell(sr, 4).Value = Num(s.ConversionRate);
            sr++;
        }
        sources.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private static (DateOnly From, DateOnly To) ResolveRange(RecruitmentDashboardFilter filter)
    {
        var to = filter.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var from = filter.From ?? to.AddDays(-30);
        return (from, to);
    }

    /// <summary>
    /// Resolves the set of in-scope vacancy ids for the dashboard (FR-7 + BR-5 / ISSUE-137), honouring the
    /// caller's authorization scope. Two scopes, mirroring <c>HrReportService.ResolveScopeAsync</c>:
    ///
    ///   • FULL access (holds <c>Recruitment.View</c> OR <c>Reports.View.All</c>): behaviour UNCHANGED — the
    ///     optional caller-supplied VacancyId/DepartmentId filters apply, and null (no filter) = whole tenant.
    ///
    ///   • DEPARTMENT-scoped only (holds <c>Reports.View.Department</c> but NEITHER full perm): metrics are
    ///     auto-restricted to the caller's OWN department and can NEVER be widened via the departmentId param
    ///     (clamp, not trust). FAIL-CLOSED: a caller with no resolvable employee/department gets an EMPTY set
    ///     (zero vacancies), never null (never the whole tenant). A supplied VacancyId is INTERSECTED with the
    ///     own-department set, so an out-of-department vacancy resolves to nothing.
    ///
    /// Return contract: <c>null</c> = no restriction (whole tenant) — used ONLY by the full-access path;
    /// an empty set = "scoped to nothing". The empty-vs-null distinction is load-bearing.
    /// </summary>
    private async Task<HashSet<Guid>?> ResolveVacancyScopeAsync(
        RecruitmentDashboardFilter filter, CancellationToken ct)
    {
        var perms = _currentUser?.Permissions ?? [];
        bool hasFullAccess = perms.Contains(PermissionCatalog.Recruitment.View)
            || perms.Contains(PermissionCatalog.Reports.ViewAll);

        if (hasFullAccess)
            return await ResolveFullAccessScopeAsync(filter, ct);

        // ── Department-scoped only (BR-5 / ISSUE-137) ──
        // FAIL-CLOSED: no employee / no resolvable department → empty scope (zero vacancies), never whole-tenant.
        var me = await GetCurrentEmployeeAsync(ct);
        if (me is null)
        {
            _logger.LogWarning(
                "Department-scoped recruitment dashboard: no employee record for user {UserId} — returning empty scope (fail-closed).",
                _currentUser?.UserId);
            return new HashSet<Guid>();
        }

        // ALWAYS scope to the caller's OWN department, IGNORING any supplied departmentId (anti-widening).
        var deptVacancyIds = (await _dbContext.Vacancies.AsNoTracking()
                .Where(v => v.DepartmentId == me.DepartmentId)
                .Select(v => v.Id)
                .ToListAsync(ct))
            .ToHashSet();

        // A supplied VacancyId can only NARROW within the own-department set, never widen out of it.
        if (filter.VacancyId is { } scopedVacancyId)
            deptVacancyIds.IntersectWith(new[] { scopedVacancyId });

        return deptVacancyIds;
    }

    /// <summary>
    /// Full-access scope resolution (unchanged pre-ISSUE-137 behaviour): a specific vacancy filter takes
    /// precedence over the department filter; null = no restriction (all the tenant's vacancies).
    /// </summary>
    private async Task<HashSet<Guid>?> ResolveFullAccessScopeAsync(
        RecruitmentDashboardFilter filter, CancellationToken ct)
    {
        if (filter.VacancyId is { } vacancyId)
            return new HashSet<Guid> { vacancyId };

        if (filter.DepartmentId is { } deptId)
        {
            var ids = await _dbContext.Vacancies.AsNoTracking()
                .Where(v => v.DepartmentId == deptId)
                .Select(v => v.Id)
                .ToListAsync(ct);
            return ids.ToHashSet();
        }

        return null;
    }

    /// <summary>
    /// Resolves the acting caller's own employee record (for the BR-5 department scope). Mirrors
    /// <c>HrReportService</c>/<c>GoalProgressService</c>. Returns null when no employee row matches the user.
    /// </summary>
    private async Task<Employee?> GetCurrentEmployeeAsync(CancellationToken ct)
    {
        if (_currentUser is null)
            return null;

        return await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, ct);
    }

    private async Task<Dictionary<Guid, DateTime>> AppliedAtLookupAsync(
        IEnumerable<Guid> applicantIds, HashSet<Guid>? vacancyScope, CancellationToken ct)
    {
        var ids = applicantIds.ToList();
        if (ids.Count == 0) return new Dictionary<Guid, DateTime>();

        var query = _dbContext.Applicants.AsNoTracking().Where(a => ids.Contains(a.Id));
        if (vacancyScope is not null)
            query = query.Where(a => vacancyScope.Contains(a.VacancyId));

        return (await query.Select(a => new { a.Id, a.AppliedAt }).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => a.AppliedAt);
    }

    private static string FullName(string first, string last) => $"{first} {last}".Trim();

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

    private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    // ── Lightweight projection records (kept private to the service) ─────

    private readonly record struct ApplicantRow(
        Guid Id, Guid VacancyId, ApplicationSource Source, ApplicantStage Stage,
        DateTime AppliedAt, string FirstName, string LastName);

    private readonly record struct HistoryRow(
        Guid ApplicantId, ApplicantStage FromStage, ApplicantStage ToStage, DateTime ChangedAt);

    private readonly record struct OfferRow(
        Guid Id, Guid ApplicantId, Guid VacancyId, OfferStatus Status,
        DateTime? SentAt, DateTime? RespondedAt, string? Response);
}
