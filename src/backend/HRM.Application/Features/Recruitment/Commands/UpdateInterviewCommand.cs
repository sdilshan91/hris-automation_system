using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-005 (AC-3/BR-6): reschedules / updates an interview. Same field rules as scheduling; on a
/// timing change the reminder job is swapped (delete + recreate). Tenant-scoped.
/// </summary>
public sealed record UpdateInterviewCommand(
    Guid InterviewId,
    InterviewType InterviewType,
    DateOnly ScheduledDate,
    TimeOnly StartTime,
    int DurationMinutes,
    string? Location,
    string? VideoLink,
    string? Notes,
    IReadOnlyList<Guid> InterviewerEmployeeIds
) : IRequest<Result<InterviewDto>>;

public sealed class UpdateInterviewCommandHandler
    : IRequestHandler<UpdateInterviewCommand, Result<InterviewDto>>
{
    private readonly IInterviewService _service;

    public UpdateInterviewCommandHandler(IInterviewService service) => _service = service;

    public Task<Result<InterviewDto>> Handle(UpdateInterviewCommand request, CancellationToken cancellationToken)
        => _service.UpdateAsync(request.InterviewId, new UpdateInterviewInput
        {
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
