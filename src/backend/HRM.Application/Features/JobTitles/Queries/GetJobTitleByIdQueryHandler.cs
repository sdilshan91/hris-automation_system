using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.JobTitles.DTOs;
using MediatR;

namespace HRM.Application.Features.JobTitles.Queries;

public sealed class GetJobTitleByIdQueryHandler
    : IRequestHandler<GetJobTitleByIdQuery, Result<JobTitleDto>>
{
    private readonly IJobTitleService _jobTitleService;

    public GetJobTitleByIdQueryHandler(IJobTitleService jobTitleService)
    {
        _jobTitleService = jobTitleService;
    }

    public Task<Result<JobTitleDto>> Handle(
        GetJobTitleByIdQuery request, CancellationToken cancellationToken)
    {
        return _jobTitleService.GetByIdAsync(request.JobTitleId, cancellationToken);
    }
}
