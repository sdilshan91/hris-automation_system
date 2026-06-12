using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

public sealed class GetBulkImportTemplateQueryHandler
    : IRequestHandler<GetBulkImportTemplateQuery, Result<ExportFileResult>>
{
    private readonly IBulkEmployeeImportService _importService;

    public GetBulkImportTemplateQueryHandler(IBulkEmployeeImportService importService)
    {
        _importService = importService;
    }

    public Task<Result<ExportFileResult>> Handle(
        GetBulkImportTemplateQuery request, CancellationToken cancellationToken)
    {
        return _importService.GenerateTemplateAsync(request.Format, cancellationToken);
    }
}
