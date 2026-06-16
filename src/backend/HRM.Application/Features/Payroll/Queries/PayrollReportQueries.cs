using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>Generates a payroll report (JSON preview) for a period with filters (US-PAY-009 FR-1/FR-3).</summary>
public sealed record GetPayrollReportQuery(
    PayrollReportType ReportType,
    PayrollReportQueryParams QueryParams) : IRequest<Result<PayrollReportResult>>;

public sealed class GetPayrollReportQueryHandler
    : IRequestHandler<GetPayrollReportQuery, Result<PayrollReportResult>>
{
    private readonly IPayrollReportService _service;
    public GetPayrollReportQueryHandler(IPayrollReportService service) => _service = service;

    public Task<Result<PayrollReportResult>> Handle(GetPayrollReportQuery request, CancellationToken cancellationToken)
        => _service.GenerateReportAsync(request.ReportType, request.QueryParams, cancellationToken);
}

/// <summary>Returns chart-library-agnostic dashboard analytics (US-PAY-009 FR-5).</summary>
public sealed record GetPayrollAnalyticsQuery(
    PayrollAnalyticsChartType ChartType,
    PayrollReportQueryParams QueryParams) : IRequest<Result<PayrollAnalyticsResult>>;

public sealed class GetPayrollAnalyticsQueryHandler
    : IRequestHandler<GetPayrollAnalyticsQuery, Result<PayrollAnalyticsResult>>
{
    private readonly IPayrollReportService _service;
    public GetPayrollAnalyticsQueryHandler(IPayrollReportService service) => _service = service;

    public Task<Result<PayrollAnalyticsResult>> Handle(GetPayrollAnalyticsQuery request, CancellationToken cancellationToken)
        => _service.GetAnalyticsAsync(request.ChartType, request.QueryParams, cancellationToken);
}

/// <summary>The masked bank-advice preview (US-PAY-009 AC-2 / BR-2). Account numbers show last 4 only.</summary>
public sealed record GetBankAdvicePreviewQuery(
    PayrollReportQueryParams QueryParams) : IRequest<Result<BankAdvicePreviewDto>>;

public sealed class GetBankAdvicePreviewQueryHandler
    : IRequestHandler<GetBankAdvicePreviewQuery, Result<BankAdvicePreviewDto>>
{
    private readonly IPayrollReportService _service;
    public GetBankAdvicePreviewQueryHandler(IPayrollReportService service) => _service = service;

    public Task<Result<BankAdvicePreviewDto>> Handle(GetBankAdvicePreviewQuery request, CancellationToken cancellationToken)
        => _service.GetBankAdvicePreviewAsync(request.QueryParams, cancellationToken);
}

/// <summary>
/// Exports a payroll report to CSV / Excel / PDF (US-PAY-009 FR-2/AC-4). For BankAdvice the file carries
/// FULL account numbers (BR-2). Synchronous (large-async export is a documented follow-up).
/// </summary>
public sealed record ExportPayrollReportQuery(
    PayrollReportType ReportType,
    PayrollExportFormat Format,
    PayrollReportQueryParams QueryParams) : IRequest<Result<PayrollReportExportResult>>;

public sealed class ExportPayrollReportQueryHandler
    : IRequestHandler<ExportPayrollReportQuery, Result<PayrollReportExportResult>>
{
    private readonly IPayrollReportService _service;
    public ExportPayrollReportQueryHandler(IPayrollReportService service) => _service = service;

    public Task<Result<PayrollReportExportResult>> Handle(ExportPayrollReportQuery request, CancellationToken cancellationToken)
        => _service.ExportReportAsync(request.ReportType, request.Format, request.QueryParams, cancellationToken);
}
