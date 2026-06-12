using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;
using MediatR;

namespace HRM.Application.Features.Locations.Commands;

/// <summary>
/// Creates a new location in the current tenant (US-CHR-007 AC-2).
/// </summary>
public sealed record CreateLocationCommand(
    string Name,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateProvince,
    string? Country,
    string? PostalCode,
    string TimeZone,
    string? Phone
) : IRequest<Result<LocationDto>>;
