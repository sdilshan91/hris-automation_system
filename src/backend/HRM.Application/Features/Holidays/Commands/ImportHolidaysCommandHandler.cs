using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using MediatR;

namespace HRM.Application.Features.Holidays.Commands;

public sealed class ImportHolidaysCommandHandler
    : IRequestHandler<ImportHolidaysCommand, Result<HolidayImportResult>>
{
    private readonly IHolidayService _holidayService;

    public ImportHolidaysCommandHandler(IHolidayService holidayService)
    {
        _holidayService = holidayService;
    }

    public Task<Result<HolidayImportResult>> Handle(
        ImportHolidaysCommand request, CancellationToken cancellationToken)
        => _holidayService.ImportCsvAsync(request.CsvStream, request.FileName, cancellationToken);
}
