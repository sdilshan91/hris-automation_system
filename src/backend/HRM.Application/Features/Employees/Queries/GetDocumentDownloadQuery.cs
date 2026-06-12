using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Generates a short-lived signed download URL for a document (US-CHR-008 FR-6, AC-4).
/// </summary>
public sealed record GetDocumentDownloadQuery(
    Guid EmployeeId,
    Guid DocumentId
) : IRequest<Result<DocumentDownloadResult>>;
