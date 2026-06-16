using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

// ── Save as draft ──────────────────────────────────────────────────────

/// <summary>Persists partial manager-review progress without submitting (US-PRF-003).</summary>
public sealed record SaveManagerReviewDraftCommand(
    Guid CycleId,
    Guid EmployeeId,
    string? SummaryComment,
    ReviewFlag Flag,
    IReadOnlyList<ManagerReviewItemInput> Items
) : IRequest<Result<ManagerReviewDto>>;

public sealed class SaveManagerReviewDraftCommandHandler
    : IRequestHandler<SaveManagerReviewDraftCommand, Result<ManagerReviewDto>>
{
    private readonly IManagerReviewService _service;
    public SaveManagerReviewDraftCommandHandler(IManagerReviewService service) => _service = service;

    public Task<Result<ManagerReviewDto>> Handle(SaveManagerReviewDraftCommand request, CancellationToken cancellationToken)
        => _service.SaveDraftAsync(
            new SaveManagerReviewInput(request.CycleId, request.EmployeeId, request.SummaryComment, request.Flag, request.Items),
            cancellationToken);
}

// ── Submit (AC-2) ──────────────────────────────────────────────────────

/// <summary>Submits the manager review after all goals are rated with comments ≥20 chars (US-PRF-003 AC-2/AC-3).</summary>
public sealed record SubmitManagerReviewCommand(
    Guid CycleId,
    Guid EmployeeId,
    string? SummaryComment,
    ReviewFlag Flag,
    IReadOnlyList<ManagerReviewItemInput> Items
) : IRequest<Result<ManagerReviewDto>>;

public sealed class SubmitManagerReviewCommandHandler
    : IRequestHandler<SubmitManagerReviewCommand, Result<ManagerReviewDto>>
{
    private readonly IManagerReviewService _service;
    public SubmitManagerReviewCommandHandler(IManagerReviewService service) => _service = service;

    public Task<Result<ManagerReviewDto>> Handle(SubmitManagerReviewCommand request, CancellationToken cancellationToken)
        => _service.SubmitAsync(
            new SaveManagerReviewInput(request.CycleId, request.EmployeeId, request.SummaryComment, request.Flag, request.Items),
            cancellationToken);
}

// ── Reopen (AC-5/BR-3, HR only) ────────────────────────────────────────

/// <summary>Reopens a submitted manager review for editing — HR-only (US-PRF-003 AC-5/BR-3).</summary>
public sealed record ReopenManagerReviewCommand(Guid EmployeeId, Guid CycleId)
    : IRequest<Result<ManagerReviewDto>>;

public sealed class ReopenManagerReviewCommandHandler
    : IRequestHandler<ReopenManagerReviewCommand, Result<ManagerReviewDto>>
{
    private readonly IManagerReviewService _service;
    public ReopenManagerReviewCommandHandler(IManagerReviewService service) => _service = service;

    public Task<Result<ManagerReviewDto>> Handle(ReopenManagerReviewCommand request, CancellationToken cancellationToken)
        => _service.ReopenAsync(request.EmployeeId, request.CycleId, cancellationToken);
}
