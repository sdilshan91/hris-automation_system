using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationPreferences.DTOs;
using MediatR;

namespace HRM.Application.Features.NotificationPreferences.Commands;

/// <summary>
/// US-NTF-003 FR-9/BR-5: set the current user's quiet-hours window. When <c>Enabled</c> is false the window
/// is cleared. When enabled, start/end/timezone are required and the IANA timezone is validated. Operates on
/// the authenticated caller only.
/// </summary>
public sealed record UpdateQuietHoursCommand(
    bool Enabled,
    TimeOnly? Start,
    TimeOnly? End,
    string? Timezone) : IRequest<Result<QuietHoursDto>>;

public sealed class UpdateQuietHoursCommandHandler
    : IRequestHandler<UpdateQuietHoursCommand, Result<QuietHoursDto>>
{
    private readonly INotificationPreferenceService _service;

    public UpdateQuietHoursCommandHandler(INotificationPreferenceService service) => _service = service;

    public Task<Result<QuietHoursDto>> Handle(UpdateQuietHoursCommand request, CancellationToken cancellationToken)
        => _service.UpdateQuietHoursAsync(
            request.Enabled, request.Start, request.End, request.Timezone, cancellationToken);
}
