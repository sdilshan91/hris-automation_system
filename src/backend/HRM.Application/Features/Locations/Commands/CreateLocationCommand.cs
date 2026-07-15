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
    string? Phone,
    string? CountryCode = null,
    // US-ATT-011 AC-1: appended LAST on purpose — these are positional params, so inserting mid-list would
    // silently re-bind every call site.
    Guid? DefaultShiftId = null
) : IRequest<Result<LocationDto>>;
