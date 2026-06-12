using HRM.Application.Common.Models;
using HRM.Application.Features.JobTitles.DTOs;
using MediatR;

namespace HRM.Application.Features.JobTitles.Commands;

/// <summary>
/// Creates a new job title in the current tenant (US-CHR-005 AC-2).
/// </summary>
public sealed record CreateJobTitleCommand(
    string TitleName,
    string? Description,
    Guid? GradeId
) : IRequest<Result<JobTitleDto>>;
