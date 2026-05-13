using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Queries;

/// <summary>
/// Query to get the current authenticated user's profile and tenant context.
/// </summary>
public sealed record GetCurrentUserQuery(Guid UserId, Guid TenantId) : IRequest<Result<CurrentUserDto>>;
