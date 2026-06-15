using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// Submits an application against a vacancy (US-REC-002 AC-1/AC-4). Used for BOTH the anonymous public
/// path (Source = Public, LinkedEmployeeId = null) and the authenticated internal path
/// (Source = Internal, LinkedEmployeeId = the employee id). The resume is carried as a stream +
/// metadata, exactly like UploadEmployeeDocumentCommand.
/// </summary>
public sealed record SubmitApplicationCommand(
    Guid VacancyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? CoverLetter,
    Stream ResumeStream,
    string ResumeFileName,
    string ResumeContentType,
    long ResumeFileSize,
    ApplicationSource Source,
    Guid? LinkedEmployeeId
) : IRequest<Result<ApplicationConfirmationDto>>;

public sealed class SubmitApplicationCommandHandler
    : IRequestHandler<SubmitApplicationCommand, Result<ApplicationConfirmationDto>>
{
    private readonly IApplicantService _service;

    public SubmitApplicationCommandHandler(IApplicantService service) => _service = service;

    public Task<Result<ApplicationConfirmationDto>> Handle(SubmitApplicationCommand request, CancellationToken cancellationToken)
        => _service.SubmitAsync(new SubmitApplicationInput(
            request.VacancyId, request.FirstName, request.LastName, request.Email, request.Phone,
            request.CoverLetter, request.ResumeStream, request.ResumeFileName, request.ResumeContentType,
            request.ResumeFileSize, request.Source, request.LinkedEmployeeId), cancellationToken);
}
