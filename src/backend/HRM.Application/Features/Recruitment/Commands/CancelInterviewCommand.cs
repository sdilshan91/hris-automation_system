using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-005 (AC-3): cancels an interview — status Cancelled, reminder job removed (BR-6), participants
/// notified (FR-3). Does not change the applicant's pipeline stage (BR-4). Tenant-scoped.
/// </summary>
public sealed record CancelInterviewCommand(Guid InterviewId) : IRequest<Result<InterviewDto>>;

public sealed class CancelInterviewCommandHandler
    : IRequestHandler<CancelInterviewCommand, Result<InterviewDto>>
{
    private readonly IInterviewService _service;

    public CancelInterviewCommandHandler(IInterviewService service) => _service = service;

    public Task<Result<InterviewDto>> Handle(CancelInterviewCommand request, CancellationToken cancellationToken)
        => _service.CancelAsync(request.InterviewId, cancellationToken);
}
