using System.IO.Compression;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Payroll;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Payslip-PDF generation service (US-PAY-004) — the ENQUEUE + STATUS + DOWNLOAD side. All queries are
/// tenant-scoped via ITenantContext + the EF global query filter (AC-4 — cross-tenant download is rejected
/// because the slip simply isn't visible). Marks the run's slips Pending + enqueues the batch job (AC-1/FR-4),
/// reports per-status counts (FR-7), and streams single / bulk-ZIP downloads (FR-6). The heavy render lives in
/// <see cref="IPayslipBatchRenderer"/>; when no Hangfire scheduler is registered (tests/dev) the caller invokes
/// the renderer directly.
/// </summary>
public sealed class PayslipGenerationService : IPayslipGenerationService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IFileStorage _fileStorage;
    private readonly IPayslipGenerationJobScheduler? _jobScheduler;
    private readonly ILogger<PayslipGenerationService> _logger;
    private readonly IPayrollAuditLogger _audit;

    public PayslipGenerationService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IFileStorage fileStorage,
        ILogger<PayslipGenerationService> logger,
        IPayrollAuditLogger audit,
        IPayslipGenerationJobScheduler? jobScheduler = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _fileStorage = fileStorage;
        _logger = logger;
        _audit = audit;
        _jobScheduler = jobScheduler;
    }

    public async Task<Result<PayslipGenerationAcceptedDto>> GenerateAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipGenerationAcceptedDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayslipGenerationAcceptedDto>.Failure("Payroll run not found.", 404, "run_not_found");

        // BR-1: payslips only for ReviewPending / Approved / Finalized runs.
        if (run.Status is not (PayrollRunStatus.ReviewPending or PayrollRunStatus.Approved or PayrollRunStatus.Finalized))
            return Result<PayslipGenerationAcceptedDto>.Failure(
                "Payslips can only be generated for runs that are ReviewPending, Approved, or Finalized.",
                400, "run_not_ready_for_payslips");

        var slips = await _dbContext.PayrollSlips
            .Where(s => s.PayrollRunId == runId)
            .ToListAsync(cancellationToken);

        if (slips.Count == 0)
            return Result<PayslipGenerationAcceptedDto>.Failure(
                "The run has no payslips to generate.", 400, "no_slips");

        // AC-5: regenerate overwrites — detect whether any slip was already generated, then reset all to Pending.
        var regenerated = slips.Any(s => s.PdfStatus == PayslipPdfStatus.Generated);
        foreach (var slip in slips)
        {
            slip.PdfStatus = PayslipPdfStatus.Pending;
            slip.PdfGeneratedAt = null;
            slip.PdfFileSizeBytes = null;
            // PdfStoragePath is left as-is; the renderer overwrites the same GUID-derived path (AC-5).
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-4: enqueue the tenant-aware batch job (when the Hangfire-backed scheduler is registered).
        if (_jobScheduler is not null)
        {
            _jobScheduler.Enqueue(_tenantContext.TenantId, _tenantContext.Subdomain, run.Id);
        }
        else
        {
            _logger.LogInformation(
                "Payslip generation for run {RunId} marked Pending but no IPayslipGenerationJobScheduler is registered — " +
                "rendering must be triggered directly (dev/test).", run.Id);
        }

        return Result<PayslipGenerationAcceptedDto>.Success(new PayslipGenerationAcceptedDto
        {
            RunId = run.Id,
            QueuedCount = slips.Count,
            Regenerated = regenerated,
        });
    }

    public async Task<Result<PayslipGenerationAcceptedDto>> RetryOneAsync(Guid runId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipGenerationAcceptedDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayslipGenerationAcceptedDto>.Failure("Payroll run not found.", 404, "run_not_found");

        // BR-1 (unchanged): payslips only for ReviewPending / Approved / Finalized runs. Retrying a failed slip on a
        // Finalized run is the primary FR-8 use case, so Finalized is explicitly allowed (not restricted).
        if (run.Status is not (PayrollRunStatus.ReviewPending or PayrollRunStatus.Approved or PayrollRunStatus.Finalized))
            return Result<PayslipGenerationAcceptedDto>.Failure(
                "Payslips can only be generated for runs that are ReviewPending, Approved, or Finalized.",
                400, "run_not_ready_for_payslips");

        // Load the ONE slip. The EF global query filter scopes to the caller's tenant — a cross-tenant/unknown slip
        // is simply not visible → 404 (AC-4). No request body carries tenant_id (runId+employeeId are route-bound),
        // preserving AC-4/NFR-6.
        var slip = await _dbContext.PayrollSlips
            .FirstOrDefaultAsync(s => s.PayrollRunId == runId && s.EmployeeId == employeeId, cancellationToken);
        if (slip is null)
            return Result<PayslipGenerationAcceptedDto>.Failure("Payslip not found.", 404, "payslip_not_found");

        // BE-permissive (locked decision): retry ANY slip state (Failed / stuck-Pending / even Generated) — the
        // render is idempotent, and the FE only surfaces Retry on Failed. Reset THIS slip to Pending and clear the
        // generated markers; PdfStoragePath is left as-is (the renderer overwrites the same GUID-derived path).
        var originalStatus = slip.PdfStatus;
        var regenerated = slip.PdfStatus == PayslipPdfStatus.Generated;
        slip.PdfStatus = PayslipPdfStatus.Pending;
        slip.PdfGeneratedAt = null;
        slip.PdfFileSizeBytes = null;

        // DF-55/US-PAY-012 (BR-1 complete audit trail): attribute this MANUAL per-employee retry to the acting
        // HR user — distinct from the system-actor PayslipPDF.Generated render audit the job later writes. Staged
        // (default actor = current user) before SaveChanges so the audit commits atomically with the slip reset.
        _audit.Log(PayrollAuditAction.PayslipRetried, PayrollAuditAction.ResourceType.Payslip, slip.Id.ToString(),
            before: new { PdfStatus = originalStatus.ToString() },
            after: new { RunId = run.Id, EmployeeId = employeeId, Regenerated = regenerated });

        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-8: enqueue the single-slip retry job (when the Hangfire-backed scheduler is registered); otherwise the
        // slip is left Pending and the render must be triggered directly via IPayslipBatchRenderer.RenderOneAsync
        // (dev/test) — mirrors GenerateAsync's enqueue-or-log.
        if (_jobScheduler is not null)
        {
            _jobScheduler.Enqueue(_tenantContext.TenantId, _tenantContext.Subdomain, run.Id, employeeId);
        }
        else
        {
            _logger.LogInformation(
                "Payslip retry for run {RunId}, employee {EmployeeId} marked Pending but no IPayslipGenerationJobScheduler " +
                "is registered — rendering must be triggered directly (dev/test).", run.Id, employeeId);
        }

        return Result<PayslipGenerationAcceptedDto>.Success(new PayslipGenerationAcceptedDto
        {
            RunId = run.Id,
            QueuedCount = 1,
            Regenerated = regenerated,
        });
    }

    public async Task<Result<PayslipGenerationStatusDto>> GetStatusAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipGenerationStatusDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayslipGenerationStatusDto>.Failure("Payroll run not found.", 404, "run_not_found");

        var statuses = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.PayrollRunId == runId)
            .Select(s => s.PdfStatus)
            .ToListAsync(cancellationToken);

        var total = statuses.Count;
        var generated = statuses.Count(s => s == PayslipPdfStatus.Generated);
        var failed = statuses.Count(s => s == PayslipPdfStatus.Failed);
        var pending = total - generated - failed; // null (never generated) counts as pending.

        return Result<PayslipGenerationStatusDto>.Success(new PayslipGenerationStatusDto
        {
            RunId = run.Id,
            TotalSlips = total,
            Generated = generated,
            Pending = pending,
            Failed = failed,
            IsComplete = total > 0 && generated == total,
        });
    }

    public async Task<Result<IReadOnlyList<PayslipListItemDto>>> ListForRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PayslipListItemDto>>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<IReadOnlyList<PayslipListItemDto>>.Failure("Payroll run not found.", 404, "run_not_found");

        var slips = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.PayrollRunId == runId)
            .ToListAsync(cancellationToken);

        if (slips.Count == 0)
            return Result<IReadOnlyList<PayslipListItemDto>>.Success(Array.Empty<PayslipListItemDto>());

        var employeeIds = slips.Select(s => s.EmployeeId).Distinct().ToList();
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeNo, e.FirstName, e.LastName, e.DepartmentId })
            .ToListAsync(cancellationToken);
        var employeeById = employees.ToDictionary(e => e.Id);

        var departments = await _dbContext.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var items = slips
            .Select(s =>
            {
                employeeById.TryGetValue(s.EmployeeId, out var emp);
                return new PayslipListItemDto
                {
                    SlipId = s.Id,
                    EmployeeId = s.EmployeeId,
                    EmployeeNo = emp?.EmployeeNo ?? s.EmployeeId.ToString(),
                    EmployeeName = emp is null ? "Employee" : $"{emp.FirstName} {emp.LastName}".Trim(),
                    // ISSUE-165: prefer the slip's point-in-time department snapshot; fall back to live resolution
                    // only for legacy slips whose snapshot is null.
                    Department = s.DepartmentSnapshot
                        ?? (emp is not null ? departments.GetValueOrDefault(emp.DepartmentId) : null),
                    NetSalary = s.NetSalary,
                    PdfStatus = s.PdfStatus,
                    PdfGeneratedAt = s.PdfGeneratedAt,
                    PdfFileSizeBytes = s.PdfFileSizeBytes,
                };
            })
            .OrderBy(i => i.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<IReadOnlyList<PayslipListItemDto>>.Success(items);
    }

    public async Task<Result<PayslipFileDto>> DownloadOneAsync(Guid runId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipFileDto>.Failure("Tenant context is not resolved.", 400);

        // AC-4: the global query filter scopes this to the caller's tenant — a cross-tenant slip is invisible (404).
        var slip = await _dbContext.PayrollSlips.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PayrollRunId == runId && s.EmployeeId == employeeId, cancellationToken);
        if (slip is null)
            return Result<PayslipFileDto>.Failure("Payslip not found.", 404, "payslip_not_found");

        if (slip.PdfStatus != PayslipPdfStatus.Generated || string.IsNullOrWhiteSpace(slip.PdfStoragePath))
            return Result<PayslipFileDto>.Failure("Payslip PDF has not been generated.", 404, "pdf_not_generated");

        var bytes = await ReadPdfAsync(slip.PdfStoragePath!, cancellationToken);
        if (bytes is null)
            return Result<PayslipFileDto>.Failure("Payslip PDF file is missing from storage.", 404, "pdf_missing");

        var employeeNo = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId).Select(e => e.EmployeeNo).FirstOrDefaultAsync(cancellationToken);

        var fileName = PayslipStoragePath.DownloadFileName(employeeNo ?? employeeId.ToString(), slip.PayMonth, slip.PayYear);
        return Result<PayslipFileDto>.Success(new PayslipFileDto(bytes, fileName, "application/pdf"));
    }

    public async Task<Result<PayslipFileDto>> DownloadAllZipAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipFileDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayslipFileDto>.Failure("Payroll run not found.", 404, "run_not_found");

        var slips = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.PayrollRunId == runId && s.PdfStatus == PayslipPdfStatus.Generated && s.PdfStoragePath != null)
            .ToListAsync(cancellationToken);

        if (slips.Count == 0)
            return Result<PayslipFileDto>.Failure("The run has no generated payslips to download.", 404, "no_generated_payslips");

        var employeeNos = await _dbContext.Employees.AsNoTracking()
            .Where(e => slips.Select(s => s.EmployeeId).Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.EmployeeNo, cancellationToken);

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var slip in slips)
            {
                var bytes = await ReadPdfAsync(slip.PdfStoragePath!, cancellationToken);
                if (bytes is null) continue; // skip a slip whose file vanished; the rest still download.

                var employeeNo = employeeNos.GetValueOrDefault(slip.EmployeeId, slip.EmployeeId.ToString());
                var entryName = PayslipStoragePath.DownloadFileName(employeeNo, slip.PayMonth, slip.PayYear);
                PayslipStoragePath.AssertSafe($"payroll/{entryName}"); // NFR-6: entry name is sanitized + asserted.

                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(bytes, cancellationToken);
            }
        }

        var zipName = $"payslips_{run.PayMonth}_{run.PayYear}.zip";
        return Result<PayslipFileDto>.Success(new PayslipFileDto(zipStream.ToArray(), zipName, "application/zip"));
    }

    /// <summary>Reads a stored PDF into memory via the tenant-isolated storage abstraction (NFR-6 path is GUID-derived).</summary>
    private async Task<byte[]?> ReadPdfAsync(string relativePath, CancellationToken ct)
    {
        PayslipStoragePath.AssertSafe(relativePath); // NFR-6 belt-and-braces on the persisted path.
        await using var stream = await _fileStorage.OpenReadAsync(_tenantContext.TenantId, relativePath, ct);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
