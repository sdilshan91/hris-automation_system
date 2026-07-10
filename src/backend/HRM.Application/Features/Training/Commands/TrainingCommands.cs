using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Training.DTOs;
using MediatR;

namespace HRM.Application.Features.Training.Commands;

// ── Create course (AC-1) ────────────────────────────────────────────────────
public sealed record CreateCourseCommand(CreateCourseRequest Request) : IRequest<Result<CourseDto>>;

public sealed class CreateCourseCommandHandler : IRequestHandler<CreateCourseCommand, Result<CourseDto>>
{
    private readonly ITrainingService _service;
    public CreateCourseCommandHandler(ITrainingService service) => _service = service;

    public Task<Result<CourseDto>> Handle(CreateCourseCommand request, CancellationToken cancellationToken)
        => _service.CreateCourseAsync(request.Request, cancellationToken);
}

// ── Update course metadata (FR-3) ───────────────────────────────────────────
public sealed record UpdateCourseCommand(Guid CourseId, UpdateCourseRequest Request) : IRequest<Result<CourseDto>>;

public sealed class UpdateCourseCommandHandler : IRequestHandler<UpdateCourseCommand, Result<CourseDto>>
{
    private readonly ITrainingService _service;
    public UpdateCourseCommandHandler(ITrainingService service) => _service = service;

    public Task<Result<CourseDto>> Handle(UpdateCourseCommand request, CancellationToken cancellationToken)
        => _service.UpdateCourseAsync(request.CourseId, request.Request, cancellationToken);
}

// ── Change course status (AC-2) ─────────────────────────────────────────────
public sealed record ChangeCourseStatusCommand(Guid CourseId, string Status) : IRequest<Result<CourseDto>>;

public sealed class ChangeCourseStatusCommandHandler : IRequestHandler<ChangeCourseStatusCommand, Result<CourseDto>>
{
    private readonly ITrainingService _service;
    public ChangeCourseStatusCommandHandler(ITrainingService service) => _service = service;

    public Task<Result<CourseDto>> Handle(ChangeCourseStatusCommand request, CancellationToken cancellationToken)
        => _service.ChangeStatusAsync(request.CourseId, request.Status, cancellationToken);
}

// ── Enroll (AC-4/AC-5/AC-6) ─────────────────────────────────────────────────
public sealed record EnrollCommand(Guid CourseId, EnrollRequest Request) : IRequest<Result<EnrollmentDto>>;

public sealed class EnrollCommandHandler : IRequestHandler<EnrollCommand, Result<EnrollmentDto>>
{
    private readonly ITrainingService _service;
    public EnrollCommandHandler(ITrainingService service) => _service = service;

    public Task<Result<EnrollmentDto>> Handle(EnrollCommand request, CancellationToken cancellationToken)
        => _service.EnrollAsync(request.CourseId, request.Request, cancellationToken);
}

// ── Cancel enrollment (AC-7) ────────────────────────────────────────────────
public sealed record CancelEnrollmentCommand(Guid EnrollmentId) : IRequest<Result<EnrollmentDto>>;

public sealed class CancelEnrollmentCommandHandler : IRequestHandler<CancelEnrollmentCommand, Result<EnrollmentDto>>
{
    private readonly ITrainingService _service;
    public CancelEnrollmentCommandHandler(ITrainingService service) => _service = service;

    public Task<Result<EnrollmentDto>> Handle(CancelEnrollmentCommand request, CancellationToken cancellationToken)
        => _service.CancelAsync(request.EnrollmentId, cancellationToken);
}

// ── Complete enrollment (AC-8) ──────────────────────────────────────────────
public sealed record CompleteEnrollmentCommand(Guid EnrollmentId, CompleteEnrollmentRequest Request) : IRequest<Result<EnrollmentDto>>;

public sealed class CompleteEnrollmentCommandHandler : IRequestHandler<CompleteEnrollmentCommand, Result<EnrollmentDto>>
{
    private readonly ITrainingService _service;
    public CompleteEnrollmentCommandHandler(ITrainingService service) => _service = service;

    public Task<Result<EnrollmentDto>> Handle(CompleteEnrollmentCommand request, CancellationToken cancellationToken)
        => _service.CompleteAsync(request.EnrollmentId, request.Request, cancellationToken);
}
