using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>GAP-027: streams an onboarding task's attachment from an authenticated endpoint.</summary>
public sealed record DownloadTaskAttachmentQuery(Guid TaskInstanceId) : IRequest<Result<StoredFileResult>>;

public sealed class DownloadTaskAttachmentQueryHandler
    : IRequestHandler<DownloadTaskAttachmentQuery, Result<StoredFileResult>>
{
    private readonly IOnboardingChecklistService _service;

    public DownloadTaskAttachmentQueryHandler(IOnboardingChecklistService service) => _service = service;

    public Task<Result<StoredFileResult>> Handle(
        DownloadTaskAttachmentQuery request, CancellationToken cancellationToken)
        => _service.DownloadTaskAttachmentAsync(request.TaskInstanceId, cancellationToken);
}
