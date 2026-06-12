using HRM.Application.Common.Models;
using HRM.Application.Features.JobTitles.DTOs;
using MediatR;

namespace HRM.Application.Features.JobTitles.Queries;

/// <summary>
/// Gets a single job title by its ID within the current tenant.
/// </summary>
public sealed record GetJobTitleByIdQuery(
    Guid JobTitleId
) : IRequest<Result<JobTitleDto>>;
