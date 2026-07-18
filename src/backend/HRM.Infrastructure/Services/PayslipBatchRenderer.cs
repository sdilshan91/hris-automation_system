using System.Collections.Concurrent;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Payroll;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using PA = HRM.Domain.Payroll.PayrollAuditAction;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// The compute side of payslip generation (US-PAY-004 FR-4/FR-7). Renders + stores every slip's PDF for a
/// run with bounded concurrency (NFR-3), updates each slip's PDF fields, and returns (generated, failed)
/// counts. Tenant-scoped via the EF global query filter (AC-4) — the run + slips + employees + details are
/// all naturally scoped to the run's tenant. Invoked by the Hangfire GeneratePayslipsJob (after it restores
/// the tenant context) or directly in tests.
///
/// <para>FR-8: a failed render/store flips that one slip to <c>Failed</c> and logs the error; the batch
/// continues, and a re-run (AC-5 regenerate) retries the failed slips. NFR-6: storage paths are GUID-derived
/// and asserted safe by <see cref="PayslipStoragePath"/>.</para>
///
/// <para>YTD (BR-4/FR-2): when the tenant enables YTD display, each component's YTD column is the sum of that
/// component (by name) across the run's pay period and prior months of the SAME calendar year. The tenant
/// YTD-enable flag does not yet have a config surface, so it DEFAULTS OFF — see the module note for the
/// deferred per-tenant config.</para>
/// </summary>
public sealed class PayslipBatchRenderer : IPayslipBatchRenderer
{
    private const int MaxConcurrency = 10; // NFR-3: bounded parallelism (PDF render is CPU-bound).
    private const string DefaultDisclaimer =
        "This is a computer-generated document and does not require a signature.";

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IFileStorage _fileStorage;
    private readonly IPayrollAuditLogger _audit;
    private readonly ILogger<PayslipBatchRenderer> _logger;

    public PayslipBatchRenderer(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IFileStorage fileStorage,
        IPayrollAuditLogger audit,
        ILogger<PayslipBatchRenderer> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _fileStorage = fileStorage;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Thin convenience for non-job callers (tests): runs the READ → WORK → WRITE phases in sequence in one flow
    /// (identical result to the pre-ISSUE-269 single-method version). The Hangfire job orchestrates the phases
    /// itself so the heavy WORK phase runs outside any tenant-GUC transaction.
    /// </summary>
    public async Task<Result<PayslipBatchResult>> RenderRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var planResult = await LoadRenderPlanAsync(runId, cancellationToken);
        if (planResult.IsFailure)
            return Result<PayslipBatchResult>.Failure(planResult.Error!, planResult.StatusCode ?? 400, planResult.ErrorCode);

        var plan = planResult.Value!;
        if (plan.Items.Count == 0)
            return Result<PayslipBatchResult>.Success(new PayslipBatchResult(0, 0));

        var outcomes = await RenderAndUploadAsync(plan, cancellationToken);
        await PersistRenderResultsAsync(outcomes, cancellationToken);

        var generated = outcomes.Count(o => o.Ok);
        var failed = outcomes.Count(o => !o.Ok);

        _logger.LogInformation(
            "Payslip batch render complete. RunId={RunId}, Generated={Generated}, Failed={Failed}, Tenant={TenantId}",
            plan.RunId, generated, failed, plan.TenantId);

        return Result<PayslipBatchResult>.Success(new PayslipBatchResult(generated, failed));
    }

    /// <inheritdoc />
    public async Task<Result<PayslipRenderPlan>> LoadRenderPlanAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipRenderPlan>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayslipRenderPlan>.Failure("Payroll run not found.", 404, "run_not_found");

        var tenantId = _tenantContext.TenantId;

        // ISSUE-269: load slips AsNoTracking + project to SlipId — no tracked entities are carried into WORK.
        var slips = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.PayrollRunId == runId)
            .Select(s => new
            {
                s.Id, s.EmployeeId, s.PayMonth, s.PayYear,
                s.GrossEarnings, s.TotalDeductions, s.NetSalary, s.WorkingDays, s.PaidDays, s.LopDays,
                s.DepartmentSnapshot, s.DesignationSnapshot, // ISSUE-165: point-in-time dept/designation names.
            })
            .ToListAsync(cancellationToken);

        if (slips.Count == 0)
        {
            var emptyPlan = new PayslipRenderPlan(
                runId, tenantId, new PayslipBranding { CompanyName = "Company" },
                new Dictionary<Guid, string>(), new Dictionary<Guid, string>(),
                false, null, []);
            return Result<PayslipRenderPlan>.Success(emptyPlan);
        }

        // Bulk-load the supporting data ONCE (avoids N+1 across 5,000 slips, NFR-1).
        var employeeIds = slips.Select(s => s.EmployeeId).Distinct().ToList();
        var slipIds = slips.Select(s => s.Id).ToList();

        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(
                e => e.Id,
                e => new PayslipEmployeeSnapshot(
                    e.FirstName, e.LastName, e.EmployeeNo, e.DepartmentId, e.JobTitleId, e.DateOfJoining,
                    e.BankAccountNumber), // ISSUE-161: masked to last-4 in BuildModel.
                cancellationToken);

        var departments = await _dbContext.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
        var jobTitles = await _dbContext.JobTitles.AsNoTracking()
            .ToDictionaryAsync(j => j.Id, j => j.TitleName, cancellationToken);

        var detailsBySlip = (await _dbContext.PayrollSlipDetails.AsNoTracking()
                .Where(d => slipIds.Contains(d.PayrollSlipId))
                .Select(d => new { d.PayrollSlipId, d.ComponentName, d.ComponentType, d.Amount })
                .ToListAsync(cancellationToken))
            .GroupBy(d => d.PayrollSlipId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PayslipDetailSnapshot>)g
                    .Select(d => new PayslipDetailSnapshot(d.ComponentName, d.ComponentType, d.Amount)).ToList());

        var showYtd = TenantYtdEnabled();
        var ytdByComponent = showYtd
            ? await BuildYtdAsync(run.PayYear, run.PayMonth, employeeIds, cancellationToken)
            : null;

        // ISSUE-158: per-tenant branding (name/address/colour + logo) applied to every slip in the run. The
        // Tenant row is NOT covered by the global query filter (it IS the tenant), so it is loaded strictly by
        // the resolved tenant id — no cross-tenant access. Logo bytes are resolved ONCE (shared across slips).
        var tenant = await _dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var logoBytes = tenant is not null ? await ResolveLogoBytesAsync(tenant, cancellationToken) : null;
        var branding = tenant is not null
            ? PayslipBranding.From(tenant, logoBytes, _tenantContext.Subdomain)
            : new PayslipBranding
            {
                CompanyName = string.IsNullOrWhiteSpace(_tenantContext.Subdomain) ? "Company" : _tenantContext.Subdomain!,
            };

        var items = slips.Select(s => new PayslipRenderItem(
            s.Id, s.EmployeeId,
            new PayslipSlipSnapshot(
                s.EmployeeId, s.PayMonth, s.PayYear, s.GrossEarnings, s.TotalDeductions, s.NetSalary,
                s.WorkingDays, s.PaidDays, s.LopDays, s.DepartmentSnapshot, s.DesignationSnapshot),
            employees.GetValueOrDefault(s.EmployeeId),
            detailsBySlip.GetValueOrDefault(s.Id, []))).ToList();

        return Result<PayslipRenderPlan>.Success(new PayslipRenderPlan(
            runId, tenantId, branding, departments, jobTitles, showYtd, ytdByComponent, items));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PayslipRenderOutcome>> RenderAndUploadAsync(
        PayslipRenderPlan plan, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId; // still set by the runner's SetTenant across phases (ISSUE-269).
        var outcomes = new ConcurrentBag<PayslipRenderOutcome>();

        using var gate = new SemaphoreSlim(MaxConcurrency);
        var tasks = plan.Items.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var relativePath = PayslipStoragePath.ForSlip(plan.RunId, item.EmployeeId); // NFR-6: GUID-derived, asserted safe.
                try
                {
                    var model = BuildModel(
                        item.Slip, item.Employee, item.Details, plan.Branding, plan.Departments, plan.JobTitles,
                        plan.ShowYtd,
                        plan.YtdByComponent?.GetValueOrDefault((item.EmployeeId, false)),
                        plan.YtdByComponent?.GetValueOrDefault((item.EmployeeId, true)));

                    var bytes = PayslipPdfRenderer.Render(model);

                    using var stream = new MemoryStream(bytes, writable: false);
                    await _fileStorage.UploadAsync(tenantId, relativePath, stream, "application/pdf", cancellationToken);

                    outcomes.Add(new PayslipRenderOutcome(item.SlipId, true, relativePath, bytes.Length));
                }
                catch (Exception ex)
                {
                    // FR-8: individual failure — flag + log, keep the batch going (retryable on regenerate).
                    _logger.LogError(ex,
                        "Payslip render failed. RunId={RunId}, SlipId={SlipId}, EmployeeId={EmployeeId}, Tenant={TenantId}",
                        plan.RunId, item.SlipId, item.EmployeeId, tenantId);
                    outcomes.Add(new PayslipRenderOutcome(item.SlipId, false, null, 0));
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return outcomes.ToList();
    }

    /// <inheritdoc />
    public async Task PersistRenderResultsAsync(
        IReadOnlyList<PayslipRenderOutcome> chunk, CancellationToken cancellationToken = default)
    {
        if (chunk.Count == 0)
            return;

        var ids = chunk.Select(c => c.SlipId).ToList();
        var slips = await _dbContext.PayrollSlips
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(cancellationToken);
        var slipById = slips.ToDictionary(s => s.Id);

        var now = DateTime.UtcNow;
        foreach (var outcome in chunk)
        {
            if (!slipById.TryGetValue(outcome.SlipId, out var slip))
                continue;

            if (outcome.Ok)
            {
                slip.PdfStatus = PayslipPdfStatus.Generated;
                slip.PdfStoragePath = outcome.Path;
                slip.PdfGeneratedAt = now;
                slip.PdfFileSizeBytes = outcome.Size;

                // US-PAY-012 (BUG-080): audit each generated payslip PDF (BR-1 "no exceptions" → one row per
                // slip). Job-driven (no HTTP user) → system actor (BR-7). Staged into THIS SaveChanges so the
                // audit row commits atomically with the slip's PDF-fields update.
                _audit.Log(PA.PayslipPdfGenerated, PA.ResourceType.PayrollSlip,
                    slip.Id.ToString(),
                    before: null,
                    after: new { SlipId = slip.Id, slip.EmployeeId, slip.PayYear, slip.PayMonth },
                    systemActor: true);
            }
            else
            {
                slip.PdfStatus = PayslipPdfStatus.Failed;
                slip.PdfGeneratedAt = null;
                slip.PdfFileSizeBytes = null;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear(); // keep the tracker bounded across chunks of a large run.
    }

    /// <summary>
    /// BR-4/FR-2: YTD per (employee, isStatutory-bucket → component name → summed amount) across the run
    /// period and prior months of the SAME calendar year. Earnings and deductions are kept in separate
    /// buckets so a same-named earning/deduction never collide. Tenant-scoped by the global filter.
    /// </summary>
    private async Task<Dictionary<(Guid EmployeeId, bool IsDeductionSide), Dictionary<string, decimal>>> BuildYtdAsync(
        int payYear, int payMonth, IReadOnlyList<Guid> employeeIds, CancellationToken ct)
    {
        // Slips for these employees up to and including the current pay period, same calendar year.
        var priorSlips = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.PayYear == payYear && s.PayMonth <= payMonth && employeeIds.Contains(s.EmployeeId))
            .Select(s => new { s.Id, s.EmployeeId })
            .ToListAsync(ct);

        var slipToEmployee = priorSlips.ToDictionary(s => s.Id, s => s.EmployeeId);
        var priorSlipIds = priorSlips.Select(s => s.Id).ToList();

        var result = new Dictionary<(Guid, bool), Dictionary<string, decimal>>();
        if (priorSlipIds.Count == 0) return result;

        var details = await _dbContext.PayrollSlipDetails.AsNoTracking()
            .Where(d => priorSlipIds.Contains(d.PayrollSlipId))
            .Select(d => new { d.PayrollSlipId, d.ComponentName, d.ComponentType, d.Amount })
            .ToListAsync(ct);

        foreach (var d in details)
        {
            if (!slipToEmployee.TryGetValue(d.PayrollSlipId, out var empId)) continue;
            var isDeductionSide = IsDeductionSide(d.ComponentType);
            var key = (empId, isDeductionSide);
            if (!result.TryGetValue(key, out var map))
                result[key] = map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            map[d.ComponentName] = map.GetValueOrDefault(d.ComponentName) + d.Amount;
        }

        return result;
    }

    // internal (not private) so the employee→model mapping — including the ISSUE-161 masked bank account — is
    // directly unit-testable via InternalsVisibleTo("HRM.Tests") without spinning up the whole render pipeline.
    internal static PayslipDocumentModel BuildModel(
        PayslipSlipSnapshot slip,
        PayslipEmployeeSnapshot? employee,
        IReadOnlyList<PayslipDetailSnapshot> details,
        PayslipBranding branding,
        IReadOnlyDictionary<Guid, string> departments,
        IReadOnlyDictionary<Guid, string> jobTitles,
        bool showYtd,
        IReadOnlyDictionary<string, decimal>? earningYtd,
        IReadOnlyDictionary<string, decimal>? deductionYtd)
    {
        PayslipLine ToLine(PayslipDetailSnapshot d, bool deductionSide)
        {
            var isStatutory = string.Equals(d.ComponentType, nameof(SalaryComponentType.Statutory), StringComparison.OrdinalIgnoreCase);
            decimal? ytd = null;
            if (showYtd)
            {
                var map = deductionSide ? deductionYtd : earningYtd;
                if (map is not null && map.TryGetValue(d.ComponentName, out var v)) ytd = v;
            }
            return new PayslipLine(d.ComponentName, isStatutory, d.Amount, ytd);
        }

        var earnings = details.Where(d => !IsDeductionSide(d.ComponentType)).Select(d => ToLine(d, false)).ToList();
        var deductions = details.Where(d => IsDeductionSide(d.ComponentType)).Select(d => ToLine(d, true)).ToList();

        return new PayslipDocumentModel
        {
            CompanyName = branding.CompanyName,
            CompanyAddress = branding.CompanyAddress, // ISSUE-158: tenant registered address (org.address).
            CompanyLogoUrl = null,                    // logo carried as bytes below (renderer never fetches a URL).
            CompanyLogoBytes = branding.LogoBytes,    // ISSUE-158: pre-resolved tenant logo (PNG/JPEG) or null.
            BrandPrimaryColor = branding.PrimaryColor, // ISSUE-158: tenant primary colour for header/accents.
            FooterDisclaimer = DefaultDisclaimer, // BR-3: tenant-configurable footer deferred — see module note.
            PayMonth = slip.PayMonth,
            PayYear = slip.PayYear,
            EmployeeName = employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim(),
            EmployeeNo = employee?.EmployeeNo ?? slip.EmployeeId.ToString(),
            // ISSUE-165: a regenerated PDF reads the slip's point-in-time snapshot so it matches the on-screen
            // detail; fall back to live resolution only for legacy slips whose snapshot is null.
            Department = slip.DepartmentSnapshot
                ?? (employee is not null ? departments.GetValueOrDefault(employee.DepartmentId) : null),
            Designation = slip.DesignationSnapshot
                ?? (employee is not null ? jobTitles.GetValueOrDefault(employee.JobTitleId) : null),
            DateOfJoining = employee?.DateOfJoining,
            // ISSUE-161/FR-2: masked (last-4) bank account in the employee section. Null when none on file → "-".
            MaskedBankAccount = string.IsNullOrWhiteSpace(employee?.BankAccountNumber)
                ? null
                : AccountMasking.MaskLast4(employee!.BankAccountNumber),
            Earnings = earnings,
            Deductions = deductions,
            GrossEarnings = slip.GrossEarnings,
            TotalDeductions = slip.TotalDeductions,
            NetSalary = slip.NetSalary,
            WorkingDays = slip.WorkingDays,
            PaidDays = slip.PaidDays,
            LopDays = slip.LopDays,
            ShowYtd = showYtd,
        };
    }

    /// <summary>Deduction + statutory components reduce net; earning + reimbursement contribute to gross.</summary>
    private static bool IsDeductionSide(string componentType) =>
        string.Equals(componentType, nameof(SalaryComponentType.Deduction), StringComparison.OrdinalIgnoreCase)
        || string.Equals(componentType, nameof(SalaryComponentType.Statutory), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ISSUE-158: resolves the tenant logo bytes from tenant-scoped storage for the payslip header. The stored
    /// <see cref="Tenant.LogoUrl"/> is the app-internal storage path ("/{tenantId}/branding/logo.png"), so the
    /// bytes are loaded via <see cref="IFileStorage"/> — NO outbound HTTP fetch is performed inside the renderer.
    /// Degrades gracefully to null on any failure (missing/blank URL, external URL, missing file, unreadable or
    /// non-decodable image) so the payslip renders without a logo rather than failing the slip.
    /// </summary>
    private async Task<byte[]?> ResolveLogoBytesAsync(Tenant tenant, CancellationToken ct)
    {
        var relativePath = ToStorageRelativePath(tenant.Id, tenant.LogoUrl);
        if (relativePath is null)
            return null;

        try
        {
            await using var stream = await _fileStorage.OpenReadAsync(tenant.Id, relativePath, ct);
            if (stream is null)
                return null;

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            if (bytes.Length == 0)
                return null;

            // Validate the bytes are a decodable image up front so a corrupt asset degrades to "no logo"
            // rather than throwing mid-render (which would fail the whole slip). Decoding once here also
            // fails fast before the per-slip loop re-uses the bytes.
            if (!IsRenderableImage(bytes))
            {
                _logger.LogWarning(
                    "Payslip logo bytes were not a decodable image; rendering without a logo. Tenant={TenantId}, Path={Path}",
                    tenant.Id, relativePath);
                return null;
            }

            return bytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Payslip logo could not be loaded; rendering without a logo. Tenant={TenantId}, Path={Path}",
                tenant.Id, relativePath);
            return null;
        }
    }

    /// <summary>
    /// Maps the tenant <see cref="Tenant.LogoUrl"/> to an <see cref="IFileStorage"/> relative path. Uploads are
    /// stored as "/{tenantId}/{relativePath}" (see LocalFileStorage.UploadAsync), so the tenant prefix is
    /// stripped. Returns null for a blank URL or an absolute http(s) URL (which the renderer must NOT fetch).
    /// </summary>
    private static string? ToStorageRelativePath(Guid tenantId, string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl))
            return null;

        var url = logoUrl.Trim();
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null; // external asset — no outbound fetch from the renderer (ISSUE-158 constraint).

        var prefix = $"/{tenantId}/";
        if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            url = url[prefix.Length..];

        var relative = url.TrimStart('/');
        return string.IsNullOrWhiteSpace(relative) ? null : relative;
    }

    /// <summary>True when the bytes decode as an image QuestPDF can render (defensive graceful-degrade check).</summary>
    private static bool IsRenderableImage(byte[] bytes)
    {
        try
        {
            _ = QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// BR-4: tenant YTD-display flag. No per-tenant config surface exists yet, so this DEFAULTS OFF. The
    /// deferred config (a tenant payroll-settings flag) is noted in the module note; flipping it on is a
    /// one-line change here once the settings entity lands.
    /// </summary>
    private bool TenantYtdEnabled() => false;
}
