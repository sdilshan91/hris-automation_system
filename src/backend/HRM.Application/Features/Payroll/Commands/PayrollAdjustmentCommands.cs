using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>
/// Creates a payroll adjustment (US-PAY-007 FR-1). For a recurring adjustment, also auto-creates the future
/// Pending series (FR-5/BR-6). Handler delegates to <see cref="IPayrollAdjustmentService"/>.
/// </summary>
public sealed record CreatePayrollAdjustmentCommand(
    Guid EmployeeId,
    string AdjustmentType,
    decimal Amount,
    string Description,
    int ApplicablePayMonth,
    int ApplicablePayYear,
    bool IsTaxable,
    bool IsRecurring,
    int? RecurrenceEndMonth,
    int? RecurrenceEndYear,
    Guid? ReferencePayrollSlipId) : IRequest<Result<CreatePayrollAdjustmentResult>>;

public sealed class CreatePayrollAdjustmentCommandHandler
    : IRequestHandler<CreatePayrollAdjustmentCommand, Result<CreatePayrollAdjustmentResult>>
{
    private readonly IPayrollAdjustmentService _service;
    public CreatePayrollAdjustmentCommandHandler(IPayrollAdjustmentService service) => _service = service;

    public Task<Result<CreatePayrollAdjustmentResult>> Handle(CreatePayrollAdjustmentCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(new CreateAdjustmentInput(
            request.EmployeeId, request.AdjustmentType, request.Amount, request.Description,
            request.ApplicablePayMonth, request.ApplicablePayYear, request.IsTaxable, request.IsRecurring,
            request.RecurrenceEndMonth, request.RecurrenceEndYear, request.ReferencePayrollSlipId), cancellationToken);
}

/// <summary>Cancels a Pending payroll adjustment (US-PAY-007 FR-6).</summary>
public sealed record CancelPayrollAdjustmentCommand(Guid AdjustmentId) : IRequest<Result>;

public sealed class CancelPayrollAdjustmentCommandHandler : IRequestHandler<CancelPayrollAdjustmentCommand, Result>
{
    private readonly IPayrollAdjustmentService _service;
    public CancelPayrollAdjustmentCommandHandler(IPayrollAdjustmentService service) => _service = service;

    public Task<Result> Handle(CancelPayrollAdjustmentCommand request, CancellationToken cancellationToken)
        => _service.CancelAsync(request.AdjustmentId, cancellationToken);
}

/// <summary>Bulk-creates adjustments from a parsed CSV for one period (US-PAY-007 FR-2).</summary>
public sealed record BulkCreatePayrollAdjustmentsCommand(int PayMonth, int PayYear, Stream CsvContent)
    : IRequest<Result<BulkAdjustmentResultDto>>;

public sealed class BulkCreatePayrollAdjustmentsCommandHandler
    : IRequestHandler<BulkCreatePayrollAdjustmentsCommand, Result<BulkAdjustmentResultDto>>
{
    private readonly IPayrollAdjustmentService _service;
    public BulkCreatePayrollAdjustmentsCommandHandler(IPayrollAdjustmentService service) => _service = service;

    public Task<Result<BulkAdjustmentResultDto>> Handle(BulkCreatePayrollAdjustmentsCommand request, CancellationToken cancellationToken)
        => _service.BulkCreateAsync(request.PayMonth, request.PayYear, request.CsvContent, cancellationToken);
}

/// <summary>Uploads a supporting document for an adjustment (US-PAY-007 AC-3/NFR-5).</summary>
public sealed record UploadAdjustmentDocumentCommand(
    Guid AdjustmentId, Stream Content, string FileName, string ContentType, long Length) : IRequest<Result<string>>;

public sealed class UploadAdjustmentDocumentCommandHandler
    : IRequestHandler<UploadAdjustmentDocumentCommand, Result<string>>
{
    private readonly IPayrollAdjustmentService _service;
    public UploadAdjustmentDocumentCommandHandler(IPayrollAdjustmentService service) => _service = service;

    public Task<Result<string>> Handle(UploadAdjustmentDocumentCommand request, CancellationToken cancellationToken)
        => _service.UploadDocumentAsync(
            request.AdjustmentId, request.Content, request.FileName, request.ContentType, request.Length, cancellationToken);
}
