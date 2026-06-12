using HRM.Application.Common.Models;
using HRM.Application.Features.JobTitles.DTOs;
using MediatR;

namespace HRM.Application.Features.JobTitles.Commands;

/// <summary>
/// Updates an existing job title (US-CHR-005 AC-2, AC-3).
/// Validates title_name uniqueness within the tenant (excluding self).
/// </summary>
public sealed record UpdateJobTitleCommand(
    Guid JobTitleId,
    string TitleName,
    string? Description,
    Guid? GradeId
) : IRequest<Result<JobTitleDto>>;
