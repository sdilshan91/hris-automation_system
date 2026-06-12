using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

public sealed class GetDocumentDownloadQueryHandler
    : IRequestHandler<GetDocumentDownloadQuery, Result<DocumentDownloadResult>>
{
    private readonly IEmployeeDocumentService _documentService;

    public GetDocumentDownloadQueryHandler(IEmployeeDocumentService documentService)
    {
        _documentService = documentService;
    }

    public Task<Result<DocumentDownloadResult>> Handle(
        GetDocumentDownloadQuery request, CancellationToken cancellationToken)
    {
        return _documentService.GetDownloadUrlAsync(
            request.EmployeeId,
            request.DocumentId,
            cancellationToken);
    }
}
