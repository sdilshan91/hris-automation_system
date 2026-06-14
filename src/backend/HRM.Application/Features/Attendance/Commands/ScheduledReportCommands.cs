using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

// ════════════════════════════════════════════════════════════════════════
//  US-ATT-010 FR-8 — Scheduled report config CRUD commands.
// ════════════════════════════════════════════════════════════════════════

public sealed record CreateScheduledReportCommand(ScheduledReportConfigDto Dto)
    : IRequest<Result<ScheduledReportConfigDto>>;

public sealed class CreateScheduledReportCommandHandler
    : IRequestHandler<CreateScheduledReportCommand, Result<ScheduledReportConfigDto>>
{
    private readonly IAttendanceDashboardService _service;
    public CreateScheduledReportCommandHandler(IAttendanceDashboardService service) => _service = service;

    public Task<Result<ScheduledReportConfigDto>> Handle(
        CreateScheduledReportCommand request, CancellationToken cancellationToken)
        => _service.CreateScheduledReportAsync(request.Dto, cancellationToken);
}

public sealed record UpdateScheduledReportCommand(Guid Id, ScheduledReportConfigDto Dto)
    : IRequest<Result<ScheduledReportConfigDto>>;

public sealed class UpdateScheduledReportCommandHandler
    : IRequestHandler<UpdateScheduledReportCommand, Result<ScheduledReportConfigDto>>
{
    private readonly IAttendanceDashboardService _service;
    public UpdateScheduledReportCommandHandler(IAttendanceDashboardService service) => _service = service;

    public Task<Result<ScheduledReportConfigDto>> Handle(
        UpdateScheduledReportCommand request, CancellationToken cancellationToken)
        => _service.UpdateScheduledReportAsync(request.Id, request.Dto, cancellationToken);
}

public sealed record DeleteScheduledReportCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteScheduledReportCommandHandler
    : IRequestHandler<DeleteScheduledReportCommand, Result>
{
    private readonly IAttendanceDashboardService _service;
    public DeleteScheduledReportCommandHandler(IAttendanceDashboardService service) => _service = service;

    public Task<Result> Handle(DeleteScheduledReportCommand request, CancellationToken cancellationToken)
        => _service.DeleteScheduledReportAsync(request.Id, cancellationToken);
}
