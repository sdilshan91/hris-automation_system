using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.JobTitles.Commands;

/// <summary>
/// Deactivates (soft-deletes) a job title (US-CHR-005 AC-5, FR-5, FR-7).
/// NOTE: Employee assignment check (AC-5) is deferred to US-CHR-001;
/// for now the handler always reports 0 active employees.
/// </summary>
public sealed record DeactivateJobTitleCommand(
    Guid JobTitleId
) : IRequest<Result>;
