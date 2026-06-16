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
