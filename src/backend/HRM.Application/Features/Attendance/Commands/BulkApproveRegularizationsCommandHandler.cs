using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

public sealed class BulkApproveRegularizationsCommandHandler
    : IRequestHandler<BulkApproveRegularizationsCommand, Result<BulkApproveRegularizationResult>>
{
    private readonly IRegularizationApprovalService _service;

    public BulkApproveRegularizationsCommandHandler(IRegularizationApprovalService service)
    {
        _service = service;
    }

    public Task<Result<BulkApproveRegularizationResult>> Handle(
        BulkApproveRegularizationsCommand request, CancellationToken cancellationToken)
    {
        return _service.BulkApproveAsync(request.RegularizationIds, request.Comment, cancellationToken);
    }
}
