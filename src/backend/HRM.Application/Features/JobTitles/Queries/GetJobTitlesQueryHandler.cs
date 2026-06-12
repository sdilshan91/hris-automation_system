using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.JobTitles.DTOs;
using MediatR;

namespace HRM.Application.Features.JobTitles.Queries;

public sealed class GetJobTitlesQueryHandler
    : IRequestHandler<GetJobTitlesQuery, Result<IReadOnlyList<JobTitleDto>>>
{
    private readonly IJobTitleService _jobTitleService;

    public GetJobTitlesQueryHandler(IJobTitleService jobTitleService)
    {
        _jobTitleService = jobTitleService;
    }

    public Task<Result<IReadOnlyList<JobTitleDto>>> Handle(
        GetJobTitlesQuery request, CancellationToken cancellationToken)
    {
        return _jobTitleService.GetAllAsync(request.ActiveOnly, cancellationToken);
    }
}
