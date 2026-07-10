using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Training.DTOs;
using MediatR;

namespace HRM.Application.Features.Training.Queries;

// ── List courses (AC-3) ─────────────────────────────────────────────────────
public sealed record GetCoursesQuery : IRequest<Result<IReadOnlyList<CourseDto>>>;

public sealed class GetCoursesQueryHandler : IRequestHandler<GetCoursesQuery, Result<IReadOnlyList<CourseDto>>>
{
    private readonly ITrainingService _service;
    public GetCoursesQueryHandler(ITrainingService service) => _service = service;

    public Task<Result<IReadOnlyList<CourseDto>>> Handle(GetCoursesQuery request, CancellationToken cancellationToken)
        => _service.GetCoursesAsync(cancellationToken);
}

// ── Get one course (AC-3) ───────────────────────────────────────────────────
public sealed record GetCourseByIdQuery(Guid CourseId) : IRequest<Result<CourseDto>>;

public sealed class GetCourseByIdQueryHandler : IRequestHandler<GetCourseByIdQuery, Result<CourseDto>>
{
    private readonly ITrainingService _service;
    public GetCourseByIdQueryHandler(ITrainingService service) => _service = service;

    public Task<Result<CourseDto>> Handle(GetCourseByIdQuery request, CancellationToken cancellationToken)
        => _service.GetCourseByIdAsync(request.CourseId, cancellationToken);
}

// ── My enrollments (AC-9) ───────────────────────────────────────────────────
public sealed record GetMyEnrollmentsQuery : IRequest<Result<IReadOnlyList<EnrollmentDto>>>;

public sealed class GetMyEnrollmentsQueryHandler : IRequestHandler<GetMyEnrollmentsQuery, Result<IReadOnlyList<EnrollmentDto>>>
{
    private readonly ITrainingService _service;
    public GetMyEnrollmentsQueryHandler(ITrainingService service) => _service = service;

    public Task<Result<IReadOnlyList<EnrollmentDto>>> Handle(GetMyEnrollmentsQuery request, CancellationToken cancellationToken)
        => _service.GetMyEnrollmentsAsync(cancellationToken);
}

// ── Employee training history (AC-9) ────────────────────────────────────────
public sealed record GetTrainingHistoryQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<EnrollmentDto>>>;

public sealed class GetTrainingHistoryQueryHandler : IRequestHandler<GetTrainingHistoryQuery, Result<IReadOnlyList<EnrollmentDto>>>
{
    private readonly ITrainingService _service;
    public GetTrainingHistoryQueryHandler(ITrainingService service) => _service = service;

    public Task<Result<IReadOnlyList<EnrollmentDto>>> Handle(GetTrainingHistoryQuery request, CancellationToken cancellationToken)
        => _service.GetTrainingHistoryAsync(request.EmployeeId, cancellationToken);
}
