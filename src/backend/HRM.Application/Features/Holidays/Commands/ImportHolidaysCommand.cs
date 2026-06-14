using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using MediatR;

namespace HRM.Application.Features.Holidays.Commands;

/// <summary>
/// Bulk-imports holidays from a CSV stream (US-LV-007 FR-4, AC-3).
/// </summary>
public sealed record ImportHolidaysCommand(
    Stream CsvStream,
    string FileName
) : IRequest<Result<HolidayImportResult>>;
