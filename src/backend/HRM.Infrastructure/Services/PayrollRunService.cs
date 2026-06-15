using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Payroll-run service (US-PAY-003) — the INITIATE + READ side. All queries are tenant-scoped via
/// ITenantContext + the EF global query filter (AC-7). Creates the queued run, enqueues the Hangfire
/// processing job via the optional <see cref="IPayrollRunJobScheduler"/> seam (FR-2), enforces one
/// non-cancelled run per period (BR-1) + the already-finalized rejection (AC-4) + idempotency (FR-9), and
/// exposes the run list/detail/summary/progress reads (FR-6/FR-8).
///
/// <para>LOCKING (NFR-3): no distributed-lock infra exists; the documented choice is the partial unique
/// index <c>ix_payroll_run_one_active_per_period</c> on (tenant, year, month) for non-cancelled runs, plus
/// an in-service pre-check for a fast, friendly 409. A concurrent racing insert is caught by the unique
/// index (DbUpdateException) and surfaced as the same 409.</para>
/// </summary>
public sealed class PayrollRunService : IPayrollRunService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPayrollRunJobScheduler? _jobScheduler;
    private readonly ILogger<PayrollRunService> _logger;

    public PayrollRunService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<PayrollRunService> logger,
        IPayrollRunJobScheduler? jobScheduler = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
        _jobScheduler = jobScheduler;
    }

    public async Task<Result<PayrollRunAcceptedDto>> InitiateAsync(InitiatePayrollRunInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunAcceptedDto>.Failure("Tenant context is not resolved.", 400);

        if (input.PayMonth is < 1 or > 12)
            return Result<PayrollRunAcceptedDto>.Failure("Pay month must be between 1 and 12.", 400, "invalid_month");

        // FR-9: a re-used idempotency key returns the existing run (idempotent — no duplicate run created).
        if (!string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            var existingByKey = await _dbContext.PayrollRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.IdempotencyKey == input.IdempotencyKey, cancellationToken);
            if (existingByKey is not null)
                return Result<PayrollRunAcceptedDto>.Failure(
                    "A payroll run with this idempotency key already exists.", 409, "duplicate_idempotency_key");
        }

        // BR-1 / AC-4: only one non-cancelled run per (tenant, year, month). An already-Finalized run for the
        // period is the AC-4 case (distinct error code so the FE can message "already finalized").
        var existingForPeriod = await _dbContext.PayrollRuns.AsNoTracking()
            .Where(r => r.PayYear == input.PayYear && r.PayMonth == input.PayMonth && r.Status != PayrollRunStatus.Cancelled)
            .OrderByDescending(r => r.InitiatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingForPeriod is not null)
        {
            return existingForPeriod.Status == PayrollRunStatus.Finalized
                ? Result<PayrollRunAcceptedDto>.Failure(
                    $"Payroll for {input.PayYear:D4}-{input.PayMonth:D2} is already finalized.", 409, "period_already_finalized")
                : Result<PayrollRunAcceptedDto>.Failure(
                    $"A payroll run for {input.PayYear:D4}-{input.PayMonth:D2} is already in progress.", 409, "run_in_progress");
        }

        var now = DateTime.UtcNow;
        var run = new PayrollRun
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            PayMonth = input.PayMonth,
            PayYear = input.PayYear,
            Status = PayrollRunStatus.Queued,
            InitiatedBy = _currentUser.UserId,
            InitiatedAt = now,
            IdempotencyKey = string.IsNullOrWhiteSpace(input.IdempotencyKey) ? null : input.IdempotencyKey.Trim(),
            IsDeleted = false,
        };

        _dbContext.PayrollRuns.Add(run);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent racing insert tripped the partial unique index (BR-1 / FR-9). Treat as 409.
            _logger.LogWarning(ex,
                "Payroll run initiate conflicted on the unique index. Tenant={TenantId}, Period={Year}-{Month}",
                _tenantContext.TenantId, input.PayYear, input.PayMonth);
            return Result<PayrollRunAcceptedDto>.Failure(
                $"A payroll run for {input.PayYear:D4}-{input.PayMonth:D2} is already in progress.", 409, "run_in_progress");
        }

        // FR-2/FR-3: enqueue the tenant-aware processing job (when the Hangfire-backed scheduler is registered).
        if (_jobScheduler is not null)
        {
            _jobScheduler.Enqueue(_tenantContext.TenantId, _tenantContext.Subdomain, run.Id);
        }
        else
        {
            _logger.LogInformation(
                "Payroll run {RunId} queued but no IPayrollRunJobScheduler is registered — processing must be triggered directly (dev/test).",
                run.Id);
        }

        _logger.LogInformation(
            "Payroll run initiated. RunId={RunId}, Period={Year}-{Month}, Tenant={TenantId}, By={User}",
            run.Id, input.PayYear, input.PayMonth, _tenantContext.TenantId, _currentUser.Email);

        return Result<PayrollRunAcceptedDto>.Success(new PayrollRunAcceptedDto
        {
            RunId = run.Id,
            Status = run.Status.ToString(),
            PayMonth = run.PayMonth,
            PayYear = run.PayYear,
        });
    }

    public async Task<Result<IReadOnlyList<PayrollRunDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PayrollRunDto>>.Failure("Tenant context is not resolved.", 400);

        var runs = await _dbContext.PayrollRuns.AsNoTracking()
            .OrderByDescending(r => r.PayYear).ThenByDescending(r => r.PayMonth).ThenByDescending(r => r.InitiatedAt)
            .Select(r => Map(r))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PayrollRunDto>>.Success(runs);
    }

    public async Task<Result<PayrollRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        return run is null
            ? Result<PayrollRunDto>.Failure("Payroll run not found.", 404, "run_not_found")
            : Result<PayrollRunDto>.Success(Map(run));
    }

    public async Task<Result<PayrollRunSummaryDto>> GetSummaryAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunSummaryDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        return run is null
            ? Result<PayrollRunSummaryDto>.Failure("Payroll run not found.", 404, "run_not_found")
            : Result<PayrollRunSummaryDto>.Success(new PayrollRunSummaryDto { Run = Map(run), RunLog = run.RunLog });
    }

    public async Task<Result<PayrollRunProgressDto>> GetProgressAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunProgressDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayrollRunProgressDto>.Failure("Payroll run not found.", 404, "run_not_found");

        var complete = run.Status is not (PayrollRunStatus.Queued or PayrollRunStatus.Processing);

        return Result<PayrollRunProgressDto>.Success(new PayrollRunProgressDto
        {
            RunId = run.Id,
            Status = run.Status.ToString(),
            TotalEmployees = run.TotalEmployees,
            ProcessedEmployees = run.ProcessedEmployees,
            SkippedEmployees = run.SkippedEmployees,
            IsComplete = complete,
        });
    }

    private static PayrollRunDto Map(PayrollRun r) => new()
    {
        Id = r.Id,
        PayMonth = r.PayMonth,
        PayYear = r.PayYear,
        Status = r.Status.ToString(),
        TotalEmployees = r.TotalEmployees,
        ProcessedEmployees = r.ProcessedEmployees,
        SkippedEmployees = r.SkippedEmployees,
        TotalGross = r.TotalGross,
        TotalDeductions = r.TotalDeductions,
        TotalNet = r.TotalNet,
        TotalStatutory = r.TotalStatutory,
        InitiatedBy = r.InitiatedBy,
        InitiatedAt = r.InitiatedAt,
        CompletedAt = r.CompletedAt,
        ApprovedBy = r.ApprovedBy,
        ApprovedAt = r.ApprovedAt,
        FinalizedAt = r.FinalizedAt,
    };
}
