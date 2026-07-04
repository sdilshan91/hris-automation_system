// ============================================================================
// BUG-024 REGRESSION — Custom-field-definition audit-write cluster.
// CustomFieldService create/update/deactivate/reactivate/reorder MUST each write
// a queryable `audit_logs` (AuditLog) row (action CustomField.*), mirroring the
// RoleService/LeaveTypeService audit pattern.
//
// Pre-fix (git show HEAD:...CustomFieldService.cs) writes no AuditLog row -> these
// tests FAIL. Post-fix the rows are present -> they PASS.
//
// Verb-independent isolation: each op is asserted by the audit-row COUNT it adds
// for the field (create=1, +update=2, +reactivate=3) plus the row's fields, so the
// tests do not couple to the exact action verb string beyond the "CustomField"
// resource substring. AuditLog has no query filter -> TenantId asserted directly.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.CustomFields.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class CustomFieldAuditWriteTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly ILogger<CustomFieldService> _logger = Substitute.For<ILogger<CustomFieldService>>();

    public CustomFieldAuditWriteTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(_actorId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Permissions.Returns(new List<string>
        {
            "CustomField.View", "CustomField.Create", "CustomField.Edit", "CustomField.Deactivate",
        });
    }

    private CustomFieldService CreateService() =>
        new(TestDbContextFactory.Create(_tenantContext, _dbName), _tenantContext, _currentUser, _logger);

    private async Task SeedTenant()
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Subdomain = $"t-{_tenantId.ToString()[..8]}",
            Name = "Test Tenant",
            MaxCustomFields = null, // null -> service default limit
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<AuditLog>> CustomFieldAuditRows(Guid? resourceId = null)
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var rows = await db.AuditLogs.Where(a => a.TenantId == _tenantId).ToListAsync();
        var filtered = rows.Where(a => (a.Action ?? a.EventType ?? string.Empty).Contains("CustomField"));
        if (resourceId is not null)
            filtered = filtered.Where(a => a.ResourceId == resourceId.Value.ToString());
        return filtered.ToList();
    }

    private async Task<Guid> CreateField(string name)
    {
        var result = await CreateService().CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = name,
            FieldType = "text",
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value!.Id;
    }

    // ── BUG-024: create → CustomField.Created ───────────────────────────────

    [Fact]
    public async Task CreateCustomField_WritesAuditRow_BUG024()
    {
        await SeedTenant();
        var fieldId = await CreateField("Badge Number");

        var rows = await CustomFieldAuditRows(fieldId);
        rows.Should().HaveCount(1, "create must write exactly one CustomField audit row");

        var audit = rows[0];
        audit.ResourceId.Should().Be(fieldId.ToString());
        audit.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorId);
        audit.After.Should().NotBeNull("a create audit must capture an after-snapshot");
        audit.Before.Should().BeNull("a create audit has no before-snapshot");
    }

    // ── BUG-024: update → CustomField.Updated with before≠after ─────────────

    [Fact]
    public async Task UpdateCustomField_WritesAuditRowWithBeforeAfter_BUG024()
    {
        await SeedTenant();
        var fieldId = await CreateField("Old Field Name");

        var result = await CreateService().UpdateAsync(fieldId, new UpdateCustomFieldRequest
        {
            FieldName = "New Field Name",
        });
        result.IsSuccess.Should().BeTrue();

        var rows = await CustomFieldAuditRows(fieldId);
        rows.Should().HaveCount(2, "create + update = two CustomField audit rows");

        var update = rows.Single(a => a.Before is not null);
        update.ResourceId.Should().Be(fieldId.ToString());
        update.TenantId.Should().Be(_tenantId);
        update.UserId.Should().Be(_actorId);
        update.After.Should().NotBe(update.Before, "before/after must reflect the change");
        update.After!.Should().Contain("New Field Name");
        update.Before!.Should().Contain("Old Field Name");
    }

    // ── BUG-024: deactivate → CustomField.Deactivated ───────────────────────

    [Fact]
    public async Task DeactivateCustomField_WritesAuditRow_BUG024()
    {
        await SeedTenant();
        var fieldId = await CreateField("Deactivate Me");

        (await CreateService().DeactivateAsync(fieldId)).IsSuccess.Should().BeTrue();

        var rows = await CustomFieldAuditRows(fieldId);
        rows.Should().HaveCount(2, "create + deactivate = two CustomField audit rows");

        var deactivate = rows.Single(a => a.Before is not null);
        deactivate.ResourceId.Should().Be(fieldId.ToString());
        deactivate.UserId.Should().Be(_actorId);
        deactivate.Before.Should().NotBe(deactivate.After);
    }

    // ── BUG-024: reactivate → CustomField.Reactivated ───────────────────────

    [Fact]
    public async Task ReactivateCustomField_WritesAuditRow_BUG024()
    {
        await SeedTenant();
        var fieldId = await CreateField("Toggle Me");

        (await CreateService().DeactivateAsync(fieldId)).IsSuccess.Should().BeTrue();
        (await CreateService().ReactivateAsync(fieldId)).IsSuccess.Should().BeTrue();

        var rows = await CustomFieldAuditRows(fieldId);
        // create + deactivate + reactivate: reactivate must add the 3rd row.
        rows.Should().HaveCount(3, "reactivate must write its own CustomField audit row");
        rows.Should().OnlyContain(a => a.ResourceId == fieldId.ToString() && a.UserId == _actorId);
    }

    // ── BUG-024: reorder → CustomField.Reordered (no single ResourceId) ─────

    [Fact]
    public async Task ReorderCustomFields_WritesAuditRow_BUG024()
    {
        await SeedTenant();
        var f1 = await CreateField("Field One");
        var f2 = await CreateField("Field Two");

        var result = await CreateService().ReorderAsync("employee", new List<Guid> { f2, f1 });
        result.IsSuccess.Should().BeTrue();

        // The reorder row targets no single field, so ResourceId is null.
        var all = await CustomFieldAuditRows();
        var reorder = all.Single(a => a.ResourceId is null);
        reorder.TenantId.Should().Be(_tenantId);
        reorder.UserId.Should().Be(_actorId);
        (reorder.Before is not null || reorder.After is not null || reorder.Detail is not null)
            .Should().BeTrue("the reorder audit must capture the ordering change");
    }
}
