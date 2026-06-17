using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationPreferences.DTOs;
using MediatR;

namespace HRM.Application.Features.NotificationPreferences.Commands;

/// <summary>
/// US-NTF-003 FR-7: reset the current user's preferences by deleting their override rows, reverting the
/// matrix to tenant defaults. Returns the reverted-to-defaults matrix. Operates on the authenticated caller only.
/// </summary>
public sealed record ResetPreferencesCommand : IRequest<Result<PreferenceMatrixDto>>;

public sealed class ResetPreferencesCommandHandler
    : IRequestHandler<ResetPreferencesCommand, Result<PreferenceMatrixDto>>
{
    private readonly INotificationPreferenceService _service;

    public ResetPreferencesCommandHandler(INotificationPreferenceService service) => _service = service;

    public Task<Result<PreferenceMatrixDto>> Handle(ResetPreferencesCommand request, CancellationToken cancellationToken)
        => _service.ResetAsync(cancellationToken);
}
