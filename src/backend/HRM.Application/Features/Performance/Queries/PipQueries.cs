using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

/// <summary>Lists PIPs for the dedicated PIP section, scoped to the caller's visibility (US-PRF-008 AC-1/FR-8/BR-5).</summary>
public sealed record ListPipsQuery : IRequest<Result<IReadOnlyList<PipSummaryDto>>>;

public sealed class ListPipsQueryHandler : IRequestHandler<ListPipsQuery, Result<IReadOnlyList<PipSummaryDto>>>
{
    private readonly IPipService _service;
    public ListPipsQueryHandler(IPipService service) => _service = service;

    public Task<Result<IReadOnlyList<PipSummaryDto>>> Handle(ListPipsQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(cancellationToken);
}

/// <summary>
/// Pre-fill payload for the create-PIP form (US-PRF-008 AC-1). HR-only (same BR-1 gate as create). Resolves the
/// target employee + a suggested reason from the flagged origin manager review (US-PRF-003) + the configured
/// escalation actions (BR-6) + a BR-2 pre-check. <paramref name="EmployeeId"/> is optional (null = blank form).
/// </summary>
public sealed record GetPipDraftQuery(Guid? EmployeeId, Guid? ReviewId) : IRequest<Result<PipDraftDto>>;

public sealed class GetPipDraftQueryHandler : IRequestHandler<GetPipDraftQuery, Result<PipDraftDto>>
{
    private readonly IPipService _service;
    public GetPipDraftQueryHandler(IPipService service) => _service = service;

    public Task<Result<PipDraftDto>> Handle(GetPipDraftQuery request, CancellationToken cancellationToken)
        => _service.GetDraftAsync(new GetPipDraftInput(request.EmployeeId, request.ReviewId), cancellationToken);
}

/// <summary>
/// Gets one full PIP including its immutable checkpoint + event history (US-PRF-008 AC-1/FR-5). VISIBILITY-
/// restricted server-side: only the employee, their manager, HR or the assigned mentor (FR-8/BR-1).
/// </summary>
public sealed record GetPipQuery(Guid PipId) : IRequest<Result<PipDto>>;

public sealed class GetPipQueryHandler : IRequestHandler<GetPipQuery, Result<PipDto>>
{
    private readonly IPipService _service;
    public GetPipQueryHandler(IPipService service) => _service = service;

    public Task<Result<PipDto>> Handle(GetPipQuery request, CancellationToken cancellationToken)
        => _service.GetAsync(request.PipId, cancellationToken);
}

/// <summary>Renders one full PIP as a branded PDF download (US-PRF-008 AC-1/FR-5) — FR-8 visibility restricted.</summary>
public sealed record ExportPipQuery(Guid PipId, string? Format) : IRequest<Result<PerformanceExportFile>>;

public sealed class ExportPipQueryHandler : IRequestHandler<ExportPipQuery, Result<PerformanceExportFile>>
{
    private readonly IPipService _service;
    public ExportPipQueryHandler(IPipService service) => _service = service;

    public Task<Result<PerformanceExportFile>> Handle(ExportPipQuery request, CancellationToken cancellationToken)
        => _service.ExportPdfAsync(request.PipId, request.Format, cancellationToken);
}
