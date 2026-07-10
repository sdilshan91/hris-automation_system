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
/// <para>Phase 1 wires <see cref="WorkflowEntityType.Leave"/>: the decision is routed to the Leave service,
/// which drives the runtime and applies the leave ledger/status atomically. Other entity types are wired in
/// US-ADM-011c.</para>
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

    public DecideWorkflowInstanceCommandHandler(
        IWorkflowRuntime runtime, ILeaveRequestService leaveRequestService)
    {
        _runtime = runtime;
        _leaveRequestService = leaveRequestService;
    }

    public async Task<Result<WorkflowDecisionResponseDto>> Handle(
        DecideWorkflowInstanceCommand request, CancellationToken cancellationToken)
    {
        var entity = await _runtime.GetInstanceEntityAsync(request.InstanceId, cancellationToken);
        if (entity is null)
            return Result<WorkflowDecisionResponseDto>.Failure("Workflow instance not found.", 404, "workflow_instance_not_found");

        if (entity.Status != WorkflowInstanceStatus.InProgress)
            return Result<WorkflowDecisionResponseDto>.Failure("This workflow instance is already completed.", 409, "workflow_not_in_flight");

        // Phase 1: only Leave is wired through the runtime. Reject/comment validation lives in the domain service.
        if (entity.EntityType != WorkflowEntityType.Leave)
            return Result<WorkflowDecisionResponseDto>.Failure(
                $"Decisions for {entity.EntityType} are not yet wired to the workflow runtime.", 400, "entity_type_not_wired");

        if (request.Action == WorkflowDecisionAction.Reject && string.IsNullOrWhiteSpace(request.Comment))
            return Result<WorkflowDecisionResponseDto>.Failure("A rejection reason is required.", 400);

        var decision = request.Action == WorkflowDecisionAction.Approve
            ? await _leaveRequestService.ApproveAsync(entity.EntityId, request.Comment, cancellationToken)
            : await _leaveRequestService.RejectAsync(entity.EntityId, request.Comment!, cancellationToken);

        if (decision.IsFailure)
            return Result<WorkflowDecisionResponseDto>.Failure(decision.Error!, decision.StatusCode ?? 400, decision.ErrorCode);

        return Result<WorkflowDecisionResponseDto>.Success(
            new WorkflowDecisionResponseDto(request.InstanceId, entity.EntityType.ToString(), decision.Value!.Status));
    }
}
