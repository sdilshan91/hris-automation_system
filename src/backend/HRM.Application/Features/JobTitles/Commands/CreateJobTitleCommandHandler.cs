using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.JobTitles.DTOs;
using MediatR;

namespace HRM.Application.Features.JobTitles.Commands;

public sealed class CreateJobTitleCommandHandler
    : IRequestHandler<CreateJobTitleCommand, Result<JobTitleDto>>
{
    private readonly IJobTitleService _jobTitleService;

    public CreateJobTitleCommandHandler(IJobTitleService jobTitleService)
    {
        _jobTitleService = jobTitleService;
    }

    public Task<Result<JobTitleDto>> Handle(
        CreateJobTitleCommand request, CancellationToken cancellationToken)
    {
        return _jobTitleService.CreateAsync(
            request.TitleName, request.Description, request.GradeId,
            cancellationToken);
    }
}
