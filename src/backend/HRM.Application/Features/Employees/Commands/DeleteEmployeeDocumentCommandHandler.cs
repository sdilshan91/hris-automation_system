using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

public sealed class DeleteEmployeeDocumentCommandHandler
    : IRequestHandler<DeleteEmployeeDocumentCommand, Result>
{
    private readonly IEmployeeDocumentService _documentService;

    public DeleteEmployeeDocumentCommandHandler(IEmployeeDocumentService documentService)
    {
        _documentService = documentService;
    }

    public Task<Result> Handle(
        DeleteEmployeeDocumentCommand request, CancellationToken cancellationToken)
    {
        return _documentService.DeleteAsync(
            request.EmployeeId,
            request.DocumentId,
            cancellationToken);
    }
}
