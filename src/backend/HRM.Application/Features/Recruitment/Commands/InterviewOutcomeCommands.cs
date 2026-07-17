using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-005 (FR-6): marks a scheduled interview <see cref="InterviewStatus.Completed"/> — it took place.
/// Only a still-Scheduled interview can be concluded (else 409). Reminder job removed; participants notified
/// (FR-3). Tenant-scoped.
/// </summary>
public sealed record CompleteInterviewCommand(Guid InterviewId) : IRequest<Result<InterviewDto>>;

public sealed class CompleteInterviewCommandHandler
    : IRequestHandler<CompleteInterviewCommand, Result<InterviewDto>>
{
    private readonly IInterviewService _service;

    public CompleteInterviewCommandHandler(IInterviewService service) => _service = service;

    public Task<Result<InterviewDto>> Handle(CompleteInterviewCommand request, CancellationToken cancellationToken)
        => _service.MarkOutcomeAsync(request.InterviewId, InterviewStatus.Completed, cancellationToken);
}

/// <summary>
/// US-REC-005 (FR-6): marks a scheduled interview <see cref="InterviewStatus.NoShow"/> — a participant failed
/// to attend. Only a still-Scheduled interview can be concluded (else 409). Reminder job removed; participants
/// notified (FR-3). Tenant-scoped.
/// </summary>
public sealed record MarkInterviewNoShowCommand(Guid InterviewId) : IRequest<Result<InterviewDto>>;

public sealed class MarkInterviewNoShowCommandHandler
    : IRequestHandler<MarkInterviewNoShowCommand, Result<InterviewDto>>
{
    private readonly IInterviewService _service;

    public MarkInterviewNoShowCommandHandler(IInterviewService service) => _service = service;

    public Task<Result<InterviewDto>> Handle(MarkInterviewNoShowCommand request, CancellationToken cancellationToken)
        => _service.MarkOutcomeAsync(request.InterviewId, InterviewStatus.NoShow, cancellationToken);
}
