using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// MediatR query for getting the full ascending reporting chain of an employee
/// (DF-8 / ISSUE-218). Element 0 is the employee; the last element is the root manager.
/// </summary>
public sealed record GetReportingChainQuery(Guid EmployeeId) : IRequest<Result<ReportingChainDto>>;
