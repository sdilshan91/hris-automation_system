using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.JobTitles.DTOs;
using MediatR;

namespace HRM.Application.Features.JobTitles.Commands;

public sealed class UpdateJobTitleCommandHandler
    : IRequestHandler<UpdateJobTitleCommand, Result<JobTitleDto>>
{
    private readonly IJobTitleService _jobTitleService;

    public UpdateJobTitleCommandHandler(IJobTitleService jobTitleService)
    {
        _jobTitleService = jobTitleService;
    }

    public Task<Result<JobTitleDto>> Handle(
        UpdateJobTitleCommand request, CancellationToken cancellationToken)
    {
        return _jobTitleService.UpdateAsync(
            request.JobTitleId, request.TitleName, request.Description,
            request.GradeId, cancellationToken);
    }
}
