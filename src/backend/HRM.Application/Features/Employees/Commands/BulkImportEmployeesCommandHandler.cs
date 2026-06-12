using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

public sealed class BulkImportEmployeesCommandHandler
    : IRequestHandler<BulkImportEmployeesCommand, Result<BulkImportResult>>
{
    private readonly IBulkEmployeeImportService _importService;

    public BulkImportEmployeesCommandHandler(IBulkEmployeeImportService importService)
    {
        _importService = importService;
    }

    public Task<Result<BulkImportResult>> Handle(
        BulkImportEmployeesCommand request, CancellationToken cancellationToken)
    {
        return _importService.ImportAsync(
            request.FileStream,
            request.FileName,
            request.FileSize,
            request.ImportUpToLimit,
            cancellationToken);
    }
}
