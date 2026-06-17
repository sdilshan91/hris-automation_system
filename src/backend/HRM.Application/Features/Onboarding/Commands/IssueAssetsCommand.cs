using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>
/// US-ONB-004 FR-5: bulk-issues one or more assets to an employee in a single transaction (NFR-5). The
/// optional acknowledgment attachment stream (FR-6) is owned by the caller. Business rules — available
/// gate (BR-1/FR-3), double-assignment guard (AC-3), per-tenant tag/serial uniqueness (BR-3), not-future
/// issue date, optional linked-task completion (AC-2) — are enforced in the service; structural rules in
/// the validator.
/// </summary>
public sealed record IssueAssetsCommand(
    Guid EmployeeId,
    Guid? TaskInstanceId,
    IReadOnlyList<IssueAssetCommandLine> Assets,
    Stream? AcknowledgmentStream,
    string? AcknowledgmentFileName,
    string? AcknowledgmentContentType,
    long AcknowledgmentSize
) : IRequest<Result<IReadOnlyList<AssetDto>>>;

/// <summary>One asset line in an <see cref="IssueAssetsCommand"/> (FR-5).</summary>
public sealed record IssueAssetCommandLine(
    Guid? AssetId,
    string AssetType,
    string AssetTag,
    string? SerialNumber,
    string? Brand,
    string? Model,
    AssetCondition Condition,
    DateTime IssueDate,
    string? Notes);

public sealed class IssueAssetsCommandHandler
    : IRequestHandler<IssueAssetsCommand, Result<IReadOnlyList<AssetDto>>>
{
    private readonly IAssetService _service;

    public IssueAssetsCommandHandler(IAssetService service) => _service = service;

    public Task<Result<IReadOnlyList<AssetDto>>> Handle(
        IssueAssetsCommand request, CancellationToken cancellationToken)
        => _service.IssueAsync(new IssueAssetsInput(
            request.EmployeeId,
            request.TaskInstanceId,
            request.Assets.Select(a => new IssueAssetLineInput(
                a.AssetId, a.AssetType, a.AssetTag, a.SerialNumber, a.Brand, a.Model,
                a.Condition, a.IssueDate, a.Notes)).ToList(),
            request.AcknowledgmentStream,
            request.AcknowledgmentFileName,
            request.AcknowledgmentContentType,
            request.AcknowledgmentSize),
            cancellationToken);
}
