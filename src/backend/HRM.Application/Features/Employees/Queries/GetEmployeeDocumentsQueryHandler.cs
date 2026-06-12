using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

public sealed class GetEmployeeDocumentsQueryHandler
    : IRequestHandler<GetEmployeeDocumentsQuery, Result<EmployeeDocumentListResult>>
{
    private readonly IEmployeeDocumentService _documentService;

    public GetEmployeeDocumentsQueryHandler(IEmployeeDocumentService documentService)
    {
        _documentService = documentService;
    }

    public Task<Result<EmployeeDocumentListResult>> Handle(
        GetEmployeeDocumentsQuery request, CancellationToken cancellationToken)
    {
        return _documentService.ListAsync(
            request.EmployeeId,
            request.Category,
            cancellationToken);
    }
}
