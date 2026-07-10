using FluentValidation;
using HRM.Application.Features.Workflows.DTOs;
using HRM.Domain.Enums;
using HRM.Domain.Workflows;

namespace HRM.Application.Features.Workflows.Validators;

/// <summary>
/// US-ADM-007 FR-5: validates a single workflow step — valid approver type, positive SLA, well-formed
/// condition JSON, and a present approver identifier for Role/NamedUser approver types. The cross-tenant
/// EXISTENCE of the referenced role/user is checked in the service (it needs the DB); this validator covers
/// the shape.
/// </summary>
public sealed class WorkflowStepRequestValidator : AbstractValidator<WorkflowStepRequest>
{
    public WorkflowStepRequestValidator()
    {
        RuleFor(s => s.StepOrder)
            .GreaterThan(0).WithMessage("Step order must be a positive integer.");

        RuleFor(s => s.ApproverType)
            .Must(BeAValidApproverType)
            .WithMessage(s => $"Invalid approver type '{s.ApproverType}'. Valid: {string.Join(", ", Enum.GetNames<WorkflowApproverType>())}.");

        // FR-5: SLA hours must be a positive integer.
        RuleFor(s => s.SlaHours)
            .GreaterThan(0).WithMessage("SLA hours must be a positive integer.");

        // Role / NamedUser approver types require an identifier; LineManager / DepartmentHead must not carry one.
        RuleFor(s => s)
            .Must(IdentifierMatchesApproverType)
            .WithMessage("Role and Named User approver types require an approver identifier; Line Manager and Department Head must not have one.");

        // Escalation identifier only makes sense when an escalation Role/NamedUser type is set.
        RuleFor(s => s)
            .Must(EscalationIdentifierIsConsistent)
            .WithMessage("Escalation approver identifier is only valid with a Role or Named User escalation approver type.");

        // BR-5: condition JSON must be well-formed if present.
        RuleFor(s => s.ConditionJson)
            .Must(json => WorkflowCondition.Validate(json) is null)
            .WithMessage(s => WorkflowCondition.Validate(s.ConditionJson) ?? "Invalid condition.");

        // Delegation backup user is required when delegation is enabled (AC-5 config).
        RuleFor(s => s.DelegationBackupUserId)
            .NotNull()
            .When(s => s.DelegationEnabled)
            .WithMessage("A delegation backup user is required when delegation is enabled.");

        // US-ADM-011b: each additional parallel approver id (when present on a parallel step) must be a real,
        // non-empty Guid. No minimum count is enforced — a parallel step may carry only its primary approver.
        RuleForEach(s => s.ParallelApproverIdentifiers)
            .NotEqual(Guid.Empty)
            .When(s => s.IsParallel && s.ParallelApproverIdentifiers is { Count: > 0 })
            .WithMessage("Each parallel approver must be a valid user id.");
    }

    private static bool BeAValidApproverType(string value)
        => Enum.TryParse<WorkflowApproverType>(value, ignoreCase: true, out _);

    private static bool IdentifierMatchesApproverType(WorkflowStepRequest s)
    {
        if (!Enum.TryParse<WorkflowApproverType>(s.ApproverType, ignoreCase: true, out var type))
            return true; // type validity reported by the dedicated rule

        return type switch
        {
            WorkflowApproverType.Role or WorkflowApproverType.NamedUser => s.ApproverIdentifier.HasValue,
            _ => !s.ApproverIdentifier.HasValue,
        };
    }

    private static bool EscalationIdentifierIsConsistent(WorkflowStepRequest s)
    {
        if (s.EscalationApproverType is null)
            return s.EscalationApproverIdentifier is null;

        if (!Enum.TryParse<WorkflowApproverType>(s.EscalationApproverType, ignoreCase: true, out var type))
            return true; // validity reported by the create/update validator if needed

        return type switch
        {
            WorkflowApproverType.Role or WorkflowApproverType.NamedUser => s.EscalationApproverIdentifier.HasValue,
            _ => !s.EscalationApproverIdentifier.HasValue,
        };
    }
}
