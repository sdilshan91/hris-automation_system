using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire recurring job that auto-generates Loss-of-Pay (LOP) entries for unaccounted absences
/// (US-LV-011 FR-2, AC-2). For each active/trial tenant it sets the tenant context and asks
/// <see cref="ILopService.GenerateAbsenteeismLopAsync"/> for each active employee over the previous
/// month's window.
///
/// SEAM: <see cref="IAttendanceProvider"/> is currently the <c>NoOpAttendanceProvider</c> (no
/// attendance module yet, US-ATT-*), so this job is wired, idempotent, and tenant-safe but generates
/// nothing until a real attendance provider lands. Mirrors how <see cref="HolidayRecurrenceJob"/> and
/// <see cref="LeaveAccrualJob"/> are structured.
/// </summary>
public sealed class ProcessAbsenteeismJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    // Page size for the per-tenant employee scan (mirrors LeaveAccrualJob batching, NFR-1).
    private const int EmployeePageSize = 500;

    public ProcessAbsenteeismJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ProcessAbsenteeismJob");

        // Reconcile the previous calendar month (AC-2: monthly attendance reconciliation).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var to = firstOfThisMonth.AddDays(-1);
        var from = new DateOnly(to.Year, to.Month, 1);

        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync();
        }

        Log.Information("ProcessAbsenteeismJob: Processing {TenantCount} tenants for {From}..{To}",
            tenantIds.Count, from, to);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                // Restore the tenant context for this scope so global query filters apply.
                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
                {
                    mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);
                }

                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var lopService = scope.ServiceProvider.GetRequiredService<ILopService>();

                int generated = 0;
                int page = 0;
                while (true)
                {
                    var employeeIds = await dbContext.Employees
                        .Where(e => e.Status != EmployeeStatus.Terminated)
                        .OrderBy(e => e.Id)
                        .Skip(page * EmployeePageSize)
                        .Take(EmployeePageSize)
                        .Select(e => e.Id)
                        .ToListAsync();
                    if (employeeIds.Count == 0)
                        break;

                    foreach (var employeeId in employeeIds)
                    {
                        var result = await lopService.GenerateAbsenteeismLopAsync(employeeId, from, to);
                        if (result.IsSuccess)
                            generated += result.Value;
                    }

                    if (employeeIds.Count < EmployeePageSize)
                        break;
                    page++;
                }

                Log.Information("ProcessAbsenteeismJob: Generated {Count} LOP entr(ies) for tenant {TenantId}",
                    generated, tenantId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ProcessAbsenteeismJob: Failed to process tenant {TenantId}", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed ProcessAbsenteeismJob");
    }
}
