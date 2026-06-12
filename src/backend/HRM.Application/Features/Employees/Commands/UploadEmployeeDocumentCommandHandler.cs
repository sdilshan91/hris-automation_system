using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

public sealed class UploadEmployeeDocumentCommandHandler
    : IRequestHandler<UploadEmployeeDocumentCommand, Result<EmployeeDocumentDto>>
{
    private readonly IEmployeeDocumentService _documentService;

    public UploadEmployeeDocumentCommandHandler(IEmployeeDocumentService documentService)
    {
        _documentService = documentService;
    }

    public Task<Result<EmployeeDocumentDto>> Handle(
        UploadEmployeeDocumentCommand request, CancellationToken cancellationToken)
    {
        var metadata = new UploadEmployeeDocumentRequest
        {
            Category = request.Category,
            Description = request.Description,
            ExpiryDate = request.ExpiryDate,
        };

        return _documentService.UploadAsync(
            request.EmployeeId,
            request.FileStream,
            request.FileName,
            request.ContentType,
            request.FileSize,
            metadata,
            cancellationToken);
    }
}
