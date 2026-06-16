using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>Lists payroll adjustments with §8 filters (US-PAY-007), most-recent-first, paginated.</summary>
public sealed record ListPayrollAdjustmentsQuery(
    string? Status,
    string? AdjustmentType,
    int? PayMonth,
    int? PayYear,
    Guid? EmployeeId,
    int Page,
    int PageSize) : IRequest<Result<PayrollAdjustmentPageDto>>;

public sealed class ListPayrollAdjustmentsQueryHandler
    : IRequestHandler<ListPayrollAdjustmentsQuery, Result<PayrollAdjustmentPageDto>>
{
    private readonly IPayrollAdjustmentService _service;
    public ListPayrollAdjustmentsQueryHandler(IPayrollAdjustmentService service) => _service = service;

    public Task<Result<PayrollAdjustmentPageDto>> Handle(ListPayrollAdjustmentsQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(new AdjustmentListFilter(
            request.Status, request.AdjustmentType, request.PayMonth, request.PayYear,
            request.EmployeeId, request.Page, request.PageSize), cancellationToken);
}

/// <summary>Gets a single payroll adjustment by id (US-PAY-007).</summary>
public sealed record GetPayrollAdjustmentQuery(Guid AdjustmentId) : IRequest<Result<PayrollAdjustmentDto>>;

public sealed class GetPayrollAdjustmentQueryHandler
    : IRequestHandler<GetPayrollAdjustmentQuery, Result<PayrollAdjustmentDto>>
{
    private readonly IPayrollAdjustmentService _service;
    public GetPayrollAdjustmentQueryHandler(IPayrollAdjustmentService service) => _service = service;

    public Task<Result<PayrollAdjustmentDto>> Handle(GetPayrollAdjustmentQuery request, CancellationToken cancellationToken)
        => _service.GetAsync(request.AdjustmentId, cancellationToken);
}

/// <summary>Downloads an adjustment's supporting document (US-PAY-007 AC-3).</summary>
public sealed record DownloadAdjustmentDocumentQuery(Guid AdjustmentId) : IRequest<Result<AdjustmentDocumentDto>>;

public sealed class DownloadAdjustmentDocumentQueryHandler
    : IRequestHandler<DownloadAdjustmentDocumentQuery, Result<AdjustmentDocumentDto>>
{
    private readonly IPayrollAdjustmentService _service;
    public DownloadAdjustmentDocumentQueryHandler(IPayrollAdjustmentService service) => _service = service;

    public Task<Result<AdjustmentDocumentDto>> Handle(DownloadAdjustmentDocumentQuery request, CancellationToken cancellationToken)
        => _service.DownloadDocumentAsync(request.AdjustmentId, cancellationToken);
}
