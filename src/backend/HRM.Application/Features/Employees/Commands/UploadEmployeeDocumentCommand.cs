using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// Uploads a document for an employee (US-CHR-008 FR-1).
/// </summary>
public sealed record UploadEmployeeDocumentCommand(
    Guid EmployeeId,
    Stream FileStream,
    string FileName,
    string ContentType,
    long FileSize,
    string Category,
    string? Description,
    DateTime? ExpiryDate
) : IRequest<Result<EmployeeDocumentDto>>;
