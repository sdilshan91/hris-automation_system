using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// MediatR query for getting direct reports of a manager (US-CHR-011 FR-5, AC-4).
/// </summary>
public sealed record GetDirectReportsQuery(Guid ManagerId) : IRequest<Result<DirectReportsResult>>;
