using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// Uploads a profile photo for an employee (US-CHR-001 AC-4, FR-6).
/// </summary>
public sealed record UploadProfilePhotoCommand(
    Guid EmployeeId,
    Stream FileStream,
    string FileName,
    string ContentType,
    long FileSize
) : IRequest<Result<string>>;
