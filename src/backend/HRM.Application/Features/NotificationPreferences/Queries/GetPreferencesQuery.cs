using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationPreferences.DTOs;
using MediatR;

namespace HRM.Application.Features.NotificationPreferences.Queries;

/// <summary>
/// US-NTF-003 AC-1: the current user's effective preference matrix (saved overrides merged over defaults)
/// plus quiet hours. Operates on the authenticated caller only — no userId is accepted.
/// </summary>
public sealed record GetPreferencesQuery : IRequest<Result<PreferenceMatrixDto>>;

public sealed class GetPreferencesQueryHandler
    : IRequestHandler<GetPreferencesQuery, Result<PreferenceMatrixDto>>
{
    private readonly INotificationPreferenceService _service;

    public GetPreferencesQueryHandler(INotificationPreferenceService service) => _service = service;

    public Task<Result<PreferenceMatrixDto>> Handle(GetPreferencesQuery request, CancellationToken cancellationToken)
        => _service.GetMatrixAsync(cancellationToken);
}
