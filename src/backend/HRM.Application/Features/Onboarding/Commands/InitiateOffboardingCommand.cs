using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>
/// US-ONB-005 AC-1/FR-2: initiates offboarding for a departing employee — creates the instance and
/// exit/clearance task instances with due dates = last_working_day - offset_days.
/// </summary>
public sealed record InitiateOffboardingCommand(
    Guid EmployeeId,
    DateTime LastWorkingDay,
    Guid? OffboardingTemplateId,
    OffboardingReason Reason,
    string? Notes
) : IRequest<Result<OffboardingInstanceDto>>;

public sealed class InitiateOffboardingCommandHandler
    : IRequestHandler<InitiateOffboardingCommand, Result<OffboardingInstanceDto>>
{
    private readonly IOffboardingService _service;

    public InitiateOffboardingCommandHandler(IOffboardingService service) => _service = service;

    public Task<Result<OffboardingInstanceDto>> Handle(
        InitiateOffboardingCommand request, CancellationToken cancellationToken)
        => _service.InitiateAsync(new InitiateOffboardingInput(
            request.EmployeeId,
            request.LastWorkingDay,
            request.OffboardingTemplateId,
            request.Reason,
            request.Notes),
            cancellationToken);
}
