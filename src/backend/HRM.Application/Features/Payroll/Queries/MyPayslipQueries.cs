using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>
/// Lists the authenticated employee's own payslips from Finalized runs (US-PAY-005 AC-1/FR-2/FR-6), most
/// recent first, paginated. Scoped to the caller's employee + tenant (FR-8). Optional year + month
/// (pay-period) filter (ISSUE-164/FR-6).
/// </summary>
public sealed record GetMyPayslipsQuery(int? Year, int? Month, int Page, int PageSize) : IRequest<Result<MyPayslipListDto>>;

public sealed class GetMyPayslipsQueryHandler : IRequestHandler<GetMyPayslipsQuery, Result<MyPayslipListDto>>
{
    private readonly IMyPayslipService _service;
    public GetMyPayslipsQueryHandler(IMyPayslipService service) => _service = service;

    public Task<Result<MyPayslipListDto>> Handle(GetMyPayslipsQuery request, CancellationToken cancellationToken)
        => _service.ListMineAsync(request.Year, request.Month, request.Page, request.PageSize, cancellationToken);
}

/// <summary>
/// Full breakdown for ONE of the caller's own payslips (US-PAY-005 AC-2/FR-3). 403 when the slip belongs to
/// another employee (AC-4) or its run is not Finalized (BR-1).
/// </summary>
public sealed record GetMyPayslipDetailQuery(Guid PayslipId) : IRequest<Result<MyPayslipDetailDto>>;

public sealed class GetMyPayslipDetailQueryHandler : IRequestHandler<GetMyPayslipDetailQuery, Result<MyPayslipDetailDto>>
{
    private readonly IMyPayslipService _service;
    public GetMyPayslipDetailQueryHandler(IMyPayslipService service) => _service = service;

    public Task<Result<MyPayslipDetailDto>> Handle(GetMyPayslipDetailQuery request, CancellationToken cancellationToken)
        => _service.GetMineAsync(request.PayslipId, cancellationToken);
}

/// <summary>
/// Streams the pre-generated PDF for ONE of the caller's own payslips (US-PAY-005 AC-3/FR-4). Same authz as
/// <see cref="GetMyPayslipDetailQuery"/>.
/// </summary>
public sealed record DownloadMyPayslipQuery(Guid PayslipId) : IRequest<Result<PayslipFileDto>>;

public sealed class DownloadMyPayslipQueryHandler : IRequestHandler<DownloadMyPayslipQuery, Result<PayslipFileDto>>
{
    private readonly IMyPayslipService _service;
    public DownloadMyPayslipQueryHandler(IMyPayslipService service) => _service = service;

    public Task<Result<PayslipFileDto>> Handle(DownloadMyPayslipQuery request, CancellationToken cancellationToken)
        => _service.DownloadMineAsync(request.PayslipId, cancellationToken);
}
