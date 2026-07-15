using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ATT-011 AC-3: resolves the effective <see cref="AttendanceSettings"/> for an employee —
/// <b>their Location's override → the tenant default → a lazily-created default row</b>.
///
/// <para><b>Resolution is ROW-LEVEL, not per-field</b> (US-ATT-011 AC-3: "the location override applies to
/// that Location's employees; where no override exists, the tenant-level attendance policy applies"). A
/// Location override row IS that location's complete policy; it does not inherit individual fields from the
/// tenant row. Callers therefore get exactly one row and never merge.</para>
///
/// <para>⚠ <b>Why this type exists.</b> Before CAL-4 there was exactly one settings row per tenant —
/// structurally, via a unique index on TenantId alone — and five call sites silently relied on it by reading
/// with an unpredicated <c>FirstOrDefaultAsync()</c>. Adding Location override rows breaks that invariant:
/// an unpredicated read now returns an ARBITRARY row, which would apply one branch's geofence/OT
/// multipliers to the entire tenant. Every employee-scoped read must come through here; every tenant-wide
/// sweep must filter <c>LocationId == null</c> explicitly. <b>Never reintroduce an unpredicated read.</b></para>
///
/// <para>Static, mirroring <see cref="ShiftScheduleResolver"/> — the caller supplies the
/// <see cref="AppDbContext"/>, so this needs no DI registration and every existing call site keeps its
/// constructor. All queries run under the EF global tenant filter, which is what makes a cross-tenant
/// override silently fail to resolve rather than leak (TC-ATT-ISO-015).</para>
/// </summary>
internal static class AttendancePolicyResolver
{
    /// <summary>
    /// The effective policy for <paramref name="employeeId"/>. Never returns null: when the tenant has no
    /// settings row at all, one is CREATED (tenant-default, <c>LocationId = null</c>) with safe defaults —
    /// preserving the pre-CAL-4 lazy-create behaviour of the call sites this replaces.
    ///
    /// <para>The lazy-create only ever creates the TENANT-DEFAULT row. It must never create a Location
    /// override as a side effect: an override is a deliberate admin act, and auto-creating one would
    /// silently pin that branch to code defaults.</para>
    /// </summary>
    public static async Task<AttendanceSettings> ResolveForEmployeeAsync(
        AppDbContext db, Guid employeeId, CancellationToken ct)
    {
        var locationId = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.LocationId)
            .FirstOrDefaultAsync(ct);

        return await ResolveForLocationAsync(db, locationId, ct);
    }

    /// <summary>
    /// The effective policy for a given location (null = the tenant default). Split out so a caller that
    /// already knows the location does not re-query the employee.
    /// </summary>
    public static async Task<AttendanceSettings> ResolveForLocationAsync(
        AppDbContext db, Guid? locationId, CancellationToken ct)
    {
        // Tier 1: the Location's override, when the employee has a location AND that location has one.
        if (locationId is not null)
        {
            var over = await db.AttendanceSettings
                .FirstOrDefaultAsync(s => s.LocationId == locationId, ct);
            if (over is not null)
                return over;
        }

        // Tier 2: the tenant default — the row whose LocationId is null. Filtering explicitly is the whole
        // point: an unpredicated read here could return a Location override and apply it tenant-wide.
        return await GetOrCreateTenantDefaultAsync(db, ct);
    }

    /// <summary>
    /// The tenant-default row (<c>LocationId == null</c>), created with safe defaults when absent. Use this
    /// directly for TENANT-WIDE sweeps that deliberately do not resolve per employee (e.g. the auto-clock-out
    /// job, the monthly summary) — never an unpredicated <c>FirstOrDefaultAsync()</c>.
    /// </summary>
    public static async Task<AttendanceSettings> GetOrCreateTenantDefaultAsync(
        AppDbContext db, CancellationToken ct)
    {
        var settings = await db.AttendanceSettings
            .FirstOrDefaultAsync(s => s.LocationId == null, ct);
        if (settings is not null)
            return settings;

        settings = new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(),
            LocationId = null,   // the TENANT default — never auto-create a Location override
        };
        db.AttendanceSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    /// <summary>
    /// The tenant-default row without creating one (<c>null</c> when absent). For read-only paths that must
    /// not write as a side effect — the attendance-status endpoint and the auto-clock-out job both document
    /// that requirement explicitly.
    /// </summary>
    public static Task<AttendanceSettings?> GetTenantDefaultOrNullAsync(
        AppDbContext db, CancellationToken ct)
        => db.AttendanceSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LocationId == null, ct);

    // ── Batch resolution (for loops over every employee in the tenant) ──────────────────────────────────

    /// <summary>
    /// Loads the tenant's WHOLE policy set — the default row and every Location override — in ONE query, for
    /// callers that must resolve a policy per employee inside a loop (the payroll run walks every employee in
    /// the tenant, so <see cref="ResolveForEmployeeAsync"/> there would be an N+1). Pair with
    /// <see cref="For"/>, which applies the SAME row-level precedence in memory.
    ///
    /// <para>Read-only: unlike <see cref="ResolveForEmployeeAsync"/> this never lazily creates the default
    /// row — a payroll run must not write policy as a side effect. A tenant with no rows yields an empty map
    /// and <see cref="For"/> returns null, which the caller reads as "code defaults".</para>
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, AttendanceSettings>> LoadAllAsync(
        AppDbContext db, CancellationToken ct)
    {
        var rows = await db.AttendanceSettings.AsNoTracking().ToListAsync(ct);
        return rows.ToDictionary(s => Key(s.LocationId), s => s);
    }

    /// <summary>
    /// The effective policy for <paramref name="locationId"/> out of a <see cref="LoadAllAsync"/> map —
    /// the Location's override → the tenant default → null (no rows at all). Mirrors
    /// <see cref="ResolveForLocationAsync"/>'s precedence exactly, minus the lazy create.
    /// </summary>
    public static AttendanceSettings? For(
        IReadOnlyDictionary<Guid, AttendanceSettings> byLocation, Guid? locationId)
    {
        if (locationId is not null && byLocation.TryGetValue(locationId.Value, out var over))
            return over;
        return byLocation.TryGetValue(NoLocation, out var tenantDefault) ? tenantDefault : null;
    }

    /// <summary>
    /// The tenant-default row's key in a <see cref="LoadAllAsync"/> map. A nullable key cannot satisfy
    /// Dictionary's <c>notnull</c> constraint (CS8714), so the tenant default — whose LocationId IS null — is
    /// keyed by <see cref="Guid.Empty"/>. Real location ids are UUIDv7 and are never Guid.Empty. Mirrors
    /// <c>PayrollCalendarResolver.NoLocation</c> so both batched policy maps key the same way.
    /// </summary>
    private static readonly Guid NoLocation = Guid.Empty;

    private static Guid Key(Guid? locationId) => locationId ?? NoLocation;
}
