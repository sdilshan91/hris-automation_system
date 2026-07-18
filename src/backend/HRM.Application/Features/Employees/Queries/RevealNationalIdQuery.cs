using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// ISSUE-293: reveals the FULL decrypted national ID for a tenant-scoped employee. The audited, sensitive-PII
/// counterpart to <see cref="GetEmployeeByIdQuery"/> (which returns the masked value). Employee.View.All-gated
/// at the controller; the handler delegates to <c>IEmployeeService.RevealNationalIdAsync</c>, which writes the
/// Employee.NationalId.ViewSensitive audit row.
/// </summary>
public sealed record RevealNationalIdQuery(
    Guid EmployeeId
) : IRequest<Result<NationalIdRevealDto>>;
