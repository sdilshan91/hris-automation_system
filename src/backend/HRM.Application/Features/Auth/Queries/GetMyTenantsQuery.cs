using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Queries;

public sealed record GetMyTenantsQuery(Guid UserId, Guid CurrentTenantId)
    : IRequest<Result<IReadOnlyList<TenantMembershipDto>>>;
