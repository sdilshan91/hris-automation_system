using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

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
    public DownloadPayslipQueryHandler(IPayslipGenerationService service) => _service = service;

    public Task<Result<PayslipFileDto>> Handle(DownloadPayslipQuery request, CancellationToken cancellationToken)
        => _service.DownloadOneAsync(request.RunId, request.EmployeeId, cancellationToken);
}

/// <summary>Bulk-downloads all generated payslip PDFs for a run as a ZIP (US-PAY-004 FR-6/AC-3).</summary>
public sealed record DownloadAllPayslipsQuery(Guid RunId) : IRequest<Result<PayslipFileDto>>;

public sealed class DownloadAllPayslipsQueryHandler : IRequestHandler<DownloadAllPayslipsQuery, Result<PayslipFileDto>>
{
    private readonly IPayslipGenerationService _service;
    public DownloadAllPayslipsQueryHandler(IPayslipGenerationService service) => _service = service;

    public Task<Result<PayslipFileDto>> Handle(DownloadAllPayslipsQuery request, CancellationToken cancellationToken)
        => _service.DownloadAllZipAsync(request.RunId, cancellationToken);
}
