using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>GAP-027: streams an employee's profile photo from an authenticated endpoint.</summary>
public sealed record GetProfilePhotoQuery(Guid EmployeeId) : IRequest<Result<StoredFileResult>>;

public sealed class GetProfilePhotoQueryHandler
    : IRequestHandler<GetProfilePhotoQuery, Result<StoredFileResult>>
{
    private readonly IEmployeeService _service;

    public GetProfilePhotoQueryHandler(IEmployeeService service) => _service = service;

    public Task<Result<StoredFileResult>> Handle(GetProfilePhotoQuery request, CancellationToken cancellationToken)
        => _service.GetProfilePhotoAsync(request.EmployeeId, cancellationToken);
}
