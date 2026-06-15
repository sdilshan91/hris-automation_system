using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-005 (AC-1/FR-1/FR-2): schedules a new interview for an applicant. Requires ≥1 interviewer, a
/// future date (BR-3), interviewers that are active employees in the tenant (BR-2), and location for
/// in-person / video link for video (FR-1). Tenant-scoped.
/// </summary>
public sealed record ScheduleInterviewCommand(
    Guid ApplicantId,
    InterviewType InterviewType,
    DateOnly ScheduledDate,
    TimeOnly StartTime,
    int DurationMinutes,
    string? Location,
    string? VideoLink,
    string? Notes,
    IReadOnlyList<Guid> InterviewerEmployeeIds
) : IRequest<Result<InterviewDto>>;

public sealed class ScheduleInterviewCommandHandler
    : IRequestHandler<ScheduleInterviewCommand, Result<InterviewDto>>
{
    private readonly IInterviewService _service;

    public ScheduleInterviewCommandHandler(IInterviewService service) => _service = service;

    public Task<Result<InterviewDto>> Handle(ScheduleInterviewCommand request, CancellationToken cancellationToken)
        => _service.ScheduleAsync(new ScheduleInterviewInput
        {
            ApplicantId = request.ApplicantId,
            InterviewType = request.InterviewType,
            ScheduledDate = request.ScheduledDate,
            StartTime = request.StartTime,
            DurationMinutes = request.DurationMinutes,
            Location = request.Location,
            VideoLink = request.VideoLink,
            Notes = request.Notes,
            InterviewerEmployeeIds = request.InterviewerEmployeeIds,
        }, cancellationToken);
}
