using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// Creates a new employee in the current tenant (US-CHR-001 AC-2).
/// </summary>
public sealed record CreateEmployeeCommand(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    DateTime? DateOfBirth,
    Gender? Gender,
    DateTime DateOfJoining,
    Guid DepartmentId,
    Guid JobTitleId,
    EmploymentType EmploymentType,
    EmployeeStatus? Status,
    string? Location,
    Guid? LocationId,
    string? CustomFields,
    Guid? UserId,
    // ⚠ APPEND-ONLY below this line. Every parameter above is positional and mostly nullable: inserting a
    // new one in the middle still COMPILES at every call site and silently shifts arguments onto the wrong
    // properties. New fields go at the END, with a default.
    /// <summary>US-CHR-013: full-time equivalent, (0, 1.00]. Null → 1.00 (full-time).</summary>
    decimal? Fte = null,
    /// <summary>US-CHR-013: work arrangement. Null → OnSite.</summary>
    WorkArrangement? WorkArrangement = null,
    /// <summary>ISSUE-293: national identity number (free-text, max 50, PII — encrypted at rest). Null → not captured.</summary>
    string? NationalId = null
) : IRequest<Result<EmployeeDto>>;
