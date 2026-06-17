using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Tenants.DTOs;
using MediatR;

namespace HRM.Application.Features.Tenants.Commands;

// ── Suspend (AC-1/FR-1) ─────────────────────────────────────────────────────

/// <summary>Suspends a tenant (US-ADM-004 AC-1/FR-1). Thin command; the work lives in <see cref="ITenantLifecycleService"/>.</summary>
public sealed record SuspendTenantCommand(Guid TenantId, string Reason) : IRequest<Result<TenantLifecycleResultDto>>;

public sealed class SuspendTenantCommandHandler : IRequestHandler<SuspendTenantCommand, Result<TenantLifecycleResultDto>>
{
    private readonly ITenantLifecycleService _service;
    public SuspendTenantCommandHandler(ITenantLifecycleService service) => _service = service;

    public Task<Result<TenantLifecycleResultDto>> Handle(SuspendTenantCommand request, CancellationToken cancellationToken)
        => _service.SuspendAsync(new SuspendTenantInput(request.TenantId, request.Reason), cancellationToken);
}

// ── Terminate (AC-3/FR-2) ───────────────────────────────────────────────────

/// <summary>Terminates a tenant into the grace period (US-ADM-004 AC-3/FR-2).</summary>
public sealed record TerminateTenantCommand(Guid TenantId, string Reason, int GraceDays)
    : IRequest<Result<TenantLifecycleResultDto>>;

public sealed class TerminateTenantCommandHandler : IRequestHandler<TerminateTenantCommand, Result<TenantLifecycleResultDto>>
{
    private readonly ITenantLifecycleService _service;
    public TerminateTenantCommandHandler(ITenantLifecycleService service) => _service = service;

    public Task<Result<TenantLifecycleResultDto>> Handle(TerminateTenantCommand request, CancellationToken cancellationToken)
        => _service.TerminateAsync(new TerminateTenantInput(request.TenantId, request.Reason, request.GraceDays), cancellationToken);
}

// ── Reactivate (AC-5/FR-5) ──────────────────────────────────────────────────

/// <summary>Reactivates a suspended tenant (US-ADM-004 AC-5/FR-5).</summary>
public sealed record ReactivateTenantCommand(Guid TenantId) : IRequest<Result<TenantLifecycleResultDto>>;

public sealed class ReactivateTenantCommandHandler : IRequestHandler<ReactivateTenantCommand, Result<TenantLifecycleResultDto>>
{
    private readonly ITenantLifecycleService _service;
    public ReactivateTenantCommandHandler(ITenantLifecycleService service) => _service = service;

    public Task<Result<TenantLifecycleResultDto>> Handle(ReactivateTenantCommand request, CancellationToken cancellationToken)
        => _service.ReactivateAsync(new ReactivateTenantInput(request.TenantId), cancellationToken);
}

// ── Restore (AC-6/FR-6) ─────────────────────────────────────────────────────

/// <summary>Restores a terminating tenant within grace (US-ADM-004 AC-6/FR-6).</summary>
public sealed record RestoreTenantCommand(Guid TenantId) : IRequest<Result<TenantLifecycleResultDto>>;

public sealed class RestoreTenantCommandHandler : IRequestHandler<RestoreTenantCommand, Result<TenantLifecycleResultDto>>
{
    private readonly ITenantLifecycleService _service;
    public RestoreTenantCommandHandler(ITenantLifecycleService service) => _service = service;

    public Task<Result<TenantLifecycleResultDto>> Handle(RestoreTenantCommand request, CancellationToken cancellationToken)
        => _service.RestoreAsync(new RestoreTenantInput(request.TenantId), cancellationToken);
}
