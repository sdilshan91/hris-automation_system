using HRM.Application.Common.Models;
using HRM.Application.Features.JobTitles.DTOs;
using MediatR;

namespace HRM.Application.Features.JobTitles.Queries;

/// <summary>
/// Lists all job titles for the current tenant as a flat list.
/// Optionally filters by IsActive.
/// </summary>
public sealed record GetJobTitlesQuery(
    bool? ActiveOnly = null
) : IRequest<Result<IReadOnlyList<JobTitleDto>>>;
