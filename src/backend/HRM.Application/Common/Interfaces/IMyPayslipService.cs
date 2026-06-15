using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Employee self-service payslip read service (US-PAY-005). Every method resolves the caller's employee_id
/// from <see cref="ICurrentUser"/> and scopes to it (FR-5/FR-8) — combined with the EF global query filter
/// the result is doubly isolated (own employee + own tenant). Only payslips from Finalized runs are visible
/// (BR-1). A request for another employee's payslip id returns 403 (AC-4), never another employee's data
/// (BR-5).
/// </summary>
public interface IMyPayslipService
{
    /// <summary>
    /// Lists the authenticated employee's own payslips from Finalized runs (AC-1/BR-1), most-recent-first,
    /// paginated (FR-6 default 12/page, optional <paramref name="year"/> filter). 403 when no employee record
    /// is linked to the current user (BR-5 — never leaks anyone else's data).
    /// </summary>
    Task<Result<MyPayslipListDto>> ListMineAsync(int? year, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full breakdown for ONE of the caller's own payslips (AC-2/FR-3). 404 when the slip does not exist for
    /// the tenant; 403 when the slip belongs to another employee (AC-4) or its run is not Finalized (BR-1).
    /// </summary>
    Task<Result<MyPayslipDetailDto>> GetMineAsync(Guid payslipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the pre-generated PDF for ONE of the caller's own payslips (AC-3/FR-4). Same authz as
    /// <see cref="GetMineAsync"/>; 404 when the PDF has not been generated yet.
    /// </summary>
    Task<Result<PayslipFileDto>> DownloadMineAsync(Guid payslipId, CancellationToken cancellationToken = default);
}
