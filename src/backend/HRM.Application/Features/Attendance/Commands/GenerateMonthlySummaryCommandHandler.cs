using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

public sealed class GenerateMonthlySummaryCommandHandler
    : IRequestHandler<GenerateMonthlySummaryCommand, Result<SummaryGenerationStatusDto>>
{
    private readonly IAttendanceSummaryService _service;

    public GenerateMonthlySummaryCommandHandler(IAttendanceSummaryService service)
    {
        _service = service;
    }

    public Task<Result<SummaryGenerationStatusDto>> Handle(
        GenerateMonthlySummaryCommand request, CancellationToken cancellationToken)
        => _service.GenerateAsync(request.Year, request.Month, cancellationToken);
}
