using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-006 (AC-1/FR-1/FR-2/FR-3/BR-1/BR-3): submits (or edits) the current interviewer's structured
/// scorecard for an interview. The interviewer is resolved from the auth context (BR-1) — never supplied by
/// the client. Marks the interview Completed when all assigned interviewers have submitted (FR-4), notifies
/// the recruiter (FR-5) and audit-logs the submission (FR-7). Tenant-scoped.
/// </summary>
public sealed record SubmitScorecardCommand(
    Guid InterviewId,
    OverallRecommendation OverallRecommendation,
    string? GeneralNotes,
    IReadOnlyList<CriterionRatingInput> Ratings
) : IRequest<Result<ScorecardDto>>;

public sealed class SubmitScorecardCommandHandler
    : IRequestHandler<SubmitScorecardCommand, Result<ScorecardDto>>
{
    private readonly IScorecardService _service;

    public SubmitScorecardCommandHandler(IScorecardService service) => _service = service;

    public Task<Result<ScorecardDto>> Handle(SubmitScorecardCommand request, CancellationToken cancellationToken)
        => _service.SubmitAsync(new SubmitScorecardInput
        {
            InterviewId = request.InterviewId,
            OverallRecommendation = request.OverallRecommendation,
            GeneralNotes = request.GeneralNotes,
            Ratings = request.Ratings,
        }, cancellationToken);
}
