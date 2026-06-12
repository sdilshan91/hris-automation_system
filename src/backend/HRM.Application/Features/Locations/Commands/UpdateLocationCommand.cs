using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;
using MediatR;

namespace HRM.Application.Features.Locations.Commands;

/// <summary>
/// Updates an existing location (US-CHR-007 AC-2, AC-4).
/// Validates name uniqueness within the tenant (excluding self).
/// </summary>
public sealed record UpdateLocationCommand(
    Guid LocationId,
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
