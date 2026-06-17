using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.Features.TenantSettings.DTOs;
using MediatR;

namespace HRM.Application.Features.TenantSettings.Commands;

/// <summary>
/// US-ADM-006 AC-2/FR-2/NFR-2: upload a branding asset (logo / email-logo / favicon). The controller buffers
/// the IFormFile into <paramref name="Content"/> before dispatching, keeping the Application layer free of any
/// ASP.NET upload type.
/// </summary>
public sealed record UploadBrandingCommand(
    BrandingAssetKind Kind,
    byte[] Content,
    string OriginalFileName) : IRequest<Result<BrandingUploadResultDto>>;

public sealed class UploadBrandingCommandHandler
    : IRequestHandler<UploadBrandingCommand, Result<BrandingUploadResultDto>>
{
    private readonly ITenantSettingsService _service;

    public UploadBrandingCommandHandler(ITenantSettingsService service) => _service = service;

    public Task<Result<BrandingUploadResultDto>> Handle(UploadBrandingCommand request, CancellationToken cancellationToken)
        => _service.UploadBrandingAsync(request.Kind, request.Content, request.OriginalFileName, cancellationToken);
}
