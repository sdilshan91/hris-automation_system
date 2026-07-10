using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Workflows.Commands;

/// <summary>
/// US-ADM-011 FR-12: an approver's decision (approve/reject + optional comment) on the active step of a live
/// workflow instance. Routing is dynamic — the runtime enforces that only the resolved approver of the current
/// Pending step may decide (AC-10), so no static permission is required beyond authentication.
///
/// <para>US-ADM-011c: routes the decision to the owning domain service by entity type — Leave / Attendance /
/// Overtime / Offer. Each service drives the runtime through its own <c>TryWorkflowDecisionAsync</c> adapter and
/// applies its domain side-effect (or, for Offer, none) atomically inside the runtime transaction.</para>
/// </summary>
public sealed record DecideWorkflowInstanceCommand(
    Guid InstanceId, WorkflowDecisionAction Action, string? Comment)
    : IRequest<Result<WorkflowDecisionResponseDto>>;

/// <summary>The outcome surfaced to the approver after a decision.</summary>
public sealed record WorkflowDecisionResponseDto(Guid InstanceId, string EntityType, string RequestStatus);

public sealed class DecideWorkflowInstanceCommandHandler
    : IRequestHandler<DecideWorkflowInstanceCommand, Result<WorkflowDecisionResponseDto>>
{
    private readonly IWorkflowRuntime _runtime;
    private readonly ILeaveRequestService _leaveRequestService;
    private readonly IRegularizationApprovalService _regularizationApprovalService;
    private readonly IOvertimeService _overtimeService;
    private readonly IOfferService _offerService;

    public DecideWorkflowInstanceCommandHandler(
        IWorkflowRuntime runtime,
        ILeaveRequestService leaveRequestService,
        IRegularizationApprovalService regularizationApprovalService,
        IOvertimeService overtimeService,
        IOfferService offerService)
    {
        _runtime = runtime;
        _leaveRequestService = leaveRequestService;
        _regularizationApprovalService = regularizationApprovalService;
        _overtimeService = overtimeService;
        _offerService = offerService;
    }

    public async Task<Result<WorkflowDecisionResponseDto>> Handle(
        DecideWorkflowInstanceCommand request, CancellationToken cancellationToken)
    {
        var entity = await _runtime.GetInstanceEntityAsync(request.InstanceId, cancellationToken);
        if (entity is null)
            return Result<WorkflowDecisionResponseDto>.Failure("Workflow instance not found.", 404, "workflow_instance_not_found");

        if (entity.Status != WorkflowInstanceStatus.InProgress)
            return Result<WorkflowDecisionResponseDto>.Failure("This workflow instance is already completed.", 409, "workflow_not_in_flight");

        // FR-12 / BR-2: a rejection always requires a reason. Each domain service re-checks defensively.
        if (request.Action == WorkflowDecisionAction.Reject && string.IsNullOrWhiteSpace(request.Comment))
            return Result<WorkflowDecisionResponseDto>.Failure("A rejection reason is required.", 400);

        bool approve = request.Action == WorkflowDecisionAction.Approve;

        // US-ADM-011c: route to the owning domain service. Each service's Approve/Reject internally routes through
        // its own TryWorkflowDecisionAsync (so the actual instance advance happens there), returning a status
        // string ("Approved"/"Rejected"/"Pending"/enum name) that is surfaced to the approver.
        var (ok, error, statusCode, errorCode, status) = entity.EntityType switch
        {
            WorkflowEntityType.Leave => Map(approve
                ? await _leaveRequestService.ApproveAsync(entity.EntityId, request.Comment, cancellationToken)
                : await _leaveRequestService.RejectAsync(entity.EntityId, request.Comment!, cancellationToken)),

            WorkflowEntityType.Attendance => Map(approve
                ? await _regularizationApprovalService.ApproveAsync(entity.EntityId, request.Comment, cancellationToken)
                : await _regularizationApprovalService.RejectAsync(entity.EntityId, request.Comment!, cancellationToken)),

            WorkflowEntityType.Overtime => Map(approve
                ? await _overtimeService.ApproveAsync(entity.EntityId, null, request.Comment, cancellationToken)
                : await _overtimeService.RejectAsync(entity.EntityId, request.Comment!, cancellationToken)),

            WorkflowEntityType.Offer => Map(
                await _offerService.DecideApprovalAsync(entity.EntityId, request.Action, request.Comment, cancellationToken)),

            _ => (false, $"Decisions for {entity.EntityType} are not yet wired to the workflow runtime.",
                  400, "entity_type_not_wired", (string?)null),
        };

        if (!ok)
            return Result<WorkflowDecisionResponseDto>.Failure(error!, statusCode ?? 400, errorCode);

        return Result<WorkflowDecisionResponseDto>.Success(
            new WorkflowDecisionResponseDto(request.InstanceId, entity.EntityType.ToString(), status ?? string.Empty));
    }

    // Normalizes each service's Result<T> into a common tuple. The T just needs a Status string.
    private static (bool ok, string? error, int? statusCode, string? errorCode, string? status) Map(
        Result<HRM.Application.Features.LeaveRequests.DTOs.LeaveApprovalResultDto> r)
        => r.IsSuccess ? (true, null, null, null, r.Value!.Status) : (false, r.Error, r.StatusCode, r.ErrorCode, null);

    private static (bool ok, string? error, int? statusCode, string? errorCode, string? status) Map(
        Result<HRM.Application.Features.Attendance.DTOs.RegularizationDecisionDto> r)
        => r.IsSuccess ? (true, null, null, null, r.Value!.Status) : (false, r.Error, r.StatusCode, r.ErrorCode, null);

    private static (bool ok, string? error, int? statusCode, string? errorCode, string? status) Map(
        Result<HRM.Application.Features.Attendance.DTOs.OvertimeDecisionDto> r)
        => r.IsSuccess ? (true, null, null, null, r.Value!.Status) : (false, r.Error, r.StatusCode, r.ErrorCode, null);

    private static (bool ok, string? error, int? statusCode, string? errorCode, string? status) Map(
        Result<HRM.Application.Features.Recruitment.DTOs.OfferApprovalDecisionDto> r)
        => r.IsSuccess ? (true, null, null, null, r.Value!.Status) : (false, r.Error, r.StatusCode, r.ErrorCode, null);
}
