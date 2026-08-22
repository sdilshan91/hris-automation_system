using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// GAP-027: streams an employee document's bytes from an authenticated endpoint.
/// </summary>
public sealed record DownloadEmployeeDocumentQuery(Guid EmployeeId, Guid DocumentId)
    : IRequest<Result<DocumentContentResult>>;

public sealed class DownloadEmployeeDocumentQueryHandler
    : IRequestHandler<DownloadEmployeeDocumentQuery, Result<DocumentContentResult>>
{
    private readonly IEmployeeDocumentService _service;

    public DownloadEmployeeDocumentQueryHandler(IEmployeeDocumentService service) => _service = service;

    public Task<Result<DocumentContentResult>> Handle(
        DownloadEmployeeDocumentQuery request, CancellationToken cancellationToken)
        => _service.DownloadAsync(request.EmployeeId, request.DocumentId, cancellationToken);
}
