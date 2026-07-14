namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>A single effective-dated Full-and-Final settlement policy version (ISSUE-294 Phase 1).</summary>
public sealed record FnFPolicyDto
{
    /// <summary>The policy row id. <see cref="System.Guid.Empty"/> when this is the (unpersisted) code-default.</summary>
    public Guid Id { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public bool IncludeProRatedFinalPay { get; init; }
    public bool IncludeStatutory { get; init; }
    public bool IncludeLeaveEncashment { get; init; }
    public bool FinalPeriodOwnedBySettlement { get; init; }
    public bool IsActive { get; init; }
    /// <summary>True when this DTO is the code-default (no persisted policy configured for the tenant).</summary>
    public bool IsDefault { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Request body to configure a new effective-dated F&amp;F policy version (ISSUE-294 Phase 1).</summary>
public sealed record CreateFnFPolicyRequest
{
    public DateOnly EffectiveFrom { get; init; }
    public bool IncludeProRatedFinalPay { get; init; } = true;
    public bool IncludeStatutory { get; init; } = true;
    public bool IncludeLeaveEncashment { get; init; } = true;
    public bool FinalPeriodOwnedBySettlement { get; init; } = true;
    public bool IsActive { get; init; } = true;
}
