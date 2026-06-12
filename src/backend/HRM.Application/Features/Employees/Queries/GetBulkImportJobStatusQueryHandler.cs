using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

public sealed class GetBulkImportJobStatusQueryHandler
    : IRequestHandler<GetBulkImportJobStatusQuery, Result<BulkImportJobStatus>>,
      IRequestHandler<GetBulkImportErrorReportQuery, Result<ExportFileResult>>
{
    private readonly IBulkEmployeeImportService _importService;

    public GetBulkImportJobStatusQueryHandler(IBulkEmployeeImportService importService)
    {
        _importService = importService;
    }

    public Task<Result<BulkImportJobStatus>> Handle(
        GetBulkImportJobStatusQuery request, CancellationToken cancellationToken)
    {
        return _importService.GetJobStatusAsync(request.JobId, cancellationToken);
    }

    public Task<Result<ExportFileResult>> Handle(
        GetBulkImportErrorReportQuery request, CancellationToken cancellationToken)
    {
        return _importService.GetErrorReportAsync(request.JobId, cancellationToken);
    }
}
