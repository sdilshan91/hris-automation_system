using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// Query for the acting employee's own attendance regularization requests (US-ATT-003 §8 — drives the
/// attendance-history status pills). Most recent first. Tenant-scoped via the resolved tenant context;
/// the acting employee is resolved from the JWT. Carries no parameters.
/// </summary>
public sealed record GetMyRegularizationsQuery : IRequest<Result<IReadOnlyList<RegularizationDto>>>;
