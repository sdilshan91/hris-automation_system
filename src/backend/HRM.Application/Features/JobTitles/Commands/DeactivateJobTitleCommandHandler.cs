using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.JobTitles.Commands;

public sealed class DeactivateJobTitleCommandHandler
    : IRequestHandler<DeactivateJobTitleCommand, Result>
{
    private readonly IJobTitleService _jobTitleService;

    public DeactivateJobTitleCommandHandler(IJobTitleService jobTitleService)
    {
        _jobTitleService = jobTitleService;
    }

    public Task<Result> Handle(
        DeactivateJobTitleCommand request, CancellationToken cancellationToken)
    {
        return _jobTitleService.DeactivateAsync(request.JobTitleId, cancellationToken);
    }
}
