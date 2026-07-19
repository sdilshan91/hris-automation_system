using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRM.Infrastructure.Persistence.Interceptors;

/// <summary>
/// ISSUE-285(a): EF Core SaveChanges interceptor that keeps <see cref="Employee.BirthMonthDay"/> in sync with
/// <see cref="Employee.DateOfBirth"/> (month*100 + day, or null). Runs on every Added/Modified Employee so the
/// denormalized key never drifts on create, profile-edit, or bulk import — and, unlike a Postgres
/// <c>GENERATED ALWAYS</c> column, it also populates on the EF InMemory provider, keeping the widget testable
/// on both providers. Dependency-free (operates purely on the change tracker), so it is trivially constructible
/// in tests. Mirrors the existing <see cref="AuditInterceptor"/> / <see cref="TenantInterceptor"/> pattern.
/// </summary>
public sealed class EmployeeBirthMonthDayInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<Employee>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                var derived = ToMonthDayKey(entry.Entity.DateOfBirth);
                if (entry.Entity.BirthMonthDay != derived)
                    entry.Entity.BirthMonthDay = derived;
            }
        }
    }

    /// <summary>
    /// Recurring birthday key = month*100 + day (e.g. Mar 5 → 305); <c>null</c> when the date is unset. Static so
    /// the value stays identical to the <c>UPDATE … EXTRACT(MONTH …)*100 + EXTRACT(DAY …)</c> backfill in the
    /// migration and the in-SQL window keys the widget compares against.
    /// </summary>
    public static int? ToMonthDayKey(DateTime? date)
        => date is { } d ? d.Month * 100 + d.Day : null;
}
