using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Payroll;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

// BUG-083: the two payslip DOWNLOAD paths below expose full salary (a deliberate sensitive reveal), so each
// emits a "Payslip.ViewSensitive" read-audit AFTER a successful, authorized read. The list view
// (ListRunPayslipsQuery, above) is deliberately NOT audited to avoid audit-log flooding on routine list reads —
// add later if compliance requires list-read auditing. The After JSON names the exposed fields but stores NO
// salary values. The audit is written via LogAndSaveAsync (its own SaveChanges) because a read has no business
// SaveChanges to piggy-back on.

/// <summary>The sensitive salary fields a payslip PDF exposes — NAMES ONLY, recorded in the BUG-083 read-audit's
/// After payload so no monetary values are ever persisted in the audit trail.</summary>
file static class PayslipReveal
{
    public static readonly string[] Fields =
        ["grossSalary", "netSalary", "earnings", "deductions", "taxes", "employerContributions"];
}

/// <summary>Per-status counts for a run's payslip PDFs (US-PAY-004 FR-7, §8 status bar).</summary>
public sealed record GetPayslipGenerationStatusQuery(Guid RunId) : IRequest<Result<PayslipGenerationStatusDto>>;

public sealed class GetPayslipGenerationStatusQueryHandler
    : IRequestHandler<GetPayslipGenerationStatusQuery, Result<PayslipGenerationStatusDto>>
{
    private readonly IPayslipGenerationService _service;
    public GetPayslipGenerationStatusQueryHandler(IPayslipGenerationService service) => _service = service;

    public Task<Result<PayslipGenerationStatusDto>> Handle(GetPayslipGenerationStatusQuery request, CancellationToken cancellationToken)
        => _service.GetStatusAsync(request.RunId, cancellationToken);
}

/// <summary>Lists the run's payslips for the §8 table (US-PAY-004). Tenant-scoped (AC-4).</summary>
public sealed record ListRunPayslipsQuery(Guid RunId) : IRequest<Result<IReadOnlyList<PayslipListItemDto>>>;

public sealed class ListRunPayslipsQueryHandler
    : IRequestHandler<ListRunPayslipsQuery, Result<IReadOnlyList<PayslipListItemDto>>>
{
    private readonly IPayslipGenerationService _service;
    public ListRunPayslipsQueryHandler(IPayslipGenerationService service) => _service = service;

    public Task<Result<IReadOnlyList<PayslipListItemDto>>> Handle(ListRunPayslipsQuery request, CancellationToken cancellationToken)
        => _service.ListForRunAsync(request.RunId, cancellationToken);
}

/// <summary>Streams ONE employee's payslip PDF for a run (US-PAY-004 FR-6). Tenant-scoped (AC-4).</summary>
public sealed record DownloadPayslipQuery(Guid RunId, Guid EmployeeId) : IRequest<Result<PayslipFileDto>>;

public sealed class DownloadPayslipQueryHandler : IRequestHandler<DownloadPayslipQuery, Result<PayslipFileDto>>
{
    private readonly IPayslipGenerationService _service;
    private readonly IPayrollAuditLogger _auditLogger;

    public DownloadPayslipQueryHandler(IPayslipGenerationService service, IPayrollAuditLogger auditLogger)
    {
        _service = service;
        _auditLogger = auditLogger;
    }

    public async Task<Result<PayslipFileDto>> Handle(DownloadPayslipQuery request, CancellationToken cancellationToken)
    {
        var result = await _service.DownloadOneAsync(request.RunId, request.EmployeeId, cancellationToken);
        if (!result.IsSuccess)
            return result;

        // BUG-083: audit the sensitive reveal — name the exposed fields, store NO values.
        await _auditLogger.LogAndSaveAsync(
            action: PayrollAuditAction.PayslipViewSensitive,
            resourceType: PayrollAuditAction.ResourceType.Payslip,
            resourceId: request.EmployeeId.ToString(),
            before: null,
            after: new { fields = PayslipReveal.Fields, runId = request.RunId, employeeId = request.EmployeeId },
            cancellationToken: cancellationToken);

        return result;
    }
}

/// <summary>Bulk-downloads all generated payslip PDFs for a run as a ZIP (US-PAY-004 FR-6/AC-3).</summary>
public sealed record DownloadAllPayslipsQuery(Guid RunId) : IRequest<Result<PayslipFileDto>>;

public sealed class DownloadAllPayslipsQueryHandler : IRequestHandler<DownloadAllPayslipsQuery, Result<PayslipFileDto>>
{
    private readonly IPayslipGenerationService _service;
    private readonly IPayrollAuditLogger _auditLogger;

    public DownloadAllPayslipsQueryHandler(IPayslipGenerationService service, IPayrollAuditLogger auditLogger)
    {
        _service = service;
        _auditLogger = auditLogger;
    }

    public async Task<Result<PayslipFileDto>> Handle(DownloadAllPayslipsQuery request, CancellationToken cancellationToken)
    {
        var result = await _service.DownloadAllZipAsync(request.RunId, cancellationToken);
        if (!result.IsSuccess)
            return result;

        // BUG-083: bulk reveal — audit ONCE per call, naming the run scope (not one row per payslip).
        await _auditLogger.LogAndSaveAsync(
            action: PayrollAuditAction.PayslipViewSensitive,
            resourceType: PayrollAuditAction.ResourceType.Payslip,
            resourceId: request.RunId.ToString(),
            before: null,
            after: new { fields = PayslipReveal.Fields, runId = request.RunId, scope = "run-all" },
            cancellationToken: cancellationToken);

        return result;
    }
}
