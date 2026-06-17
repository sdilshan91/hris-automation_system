using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationPreferences.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.NotificationPreferences.Commands;

/// <summary>
/// US-NTF-003 AC-2/AC-3/AC-4: update one category's channel toggles for the current user. Enforces BR-2
/// (a mandatory category cannot be disabled) and BR-3 (a non-mandatory category must keep ≥1 channel on) —
/// both surface as a 422 with a clear message. Operates on the authenticated caller only.
/// </summary>
public sealed record UpdateCategoryPreferenceCommand(
    NotificationCategory Category,
    bool ChannelInApp,
    bool ChannelEmail) : IRequest<Result<CategoryPreferenceDto>>;

public sealed class UpdateCategoryPreferenceCommandHandler
    : IRequestHandler<UpdateCategoryPreferenceCommand, Result<CategoryPreferenceDto>>
{
    private readonly INotificationPreferenceService _service;

    public UpdateCategoryPreferenceCommandHandler(INotificationPreferenceService service) => _service = service;

    public Task<Result<CategoryPreferenceDto>> Handle(
        UpdateCategoryPreferenceCommand request, CancellationToken cancellationToken)
        => _service.UpdateCategoryAsync(
            request.Category, request.ChannelInApp, request.ChannelEmail, cancellationToken);
}
