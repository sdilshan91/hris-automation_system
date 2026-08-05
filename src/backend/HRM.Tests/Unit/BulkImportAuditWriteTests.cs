// ============================================================================
// BUG-022 REGRESSION — Bulk-employee-import audit-write.
// A completed bulk import MUST write one queryable `audit_logs` (AuditLog) row for
// the import job (action Employee.BulkImported) with the outcome counts in Detail,
// mirroring the RoleService/LeaveTypeService audit pattern.
//
// Pre-fix (git show HEAD:...BulkEmployeeImportService.cs) writes no AuditLog row
// (only a BulkImportJob tracking record + an ILogger line) -> this test FAILS.
// Post-fix the row is present -> it PASSES.
//
// The synchronous (<=500 rows) path still creates a BulkImportJob whose id is
// surfaced as result.JobId, so the audit row is keyed on that job. Harness mirrors
// BulkEmployeeImportServiceTests. AuditLog has no query filter -> TenantId direct.
// ============================================================================

using System.Text;
using Microsoft.AspNetCore.DataProtection;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class BulkImportAuditWriteTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly ILogger<BulkEmployeeImportService> _logger =
        Substitute.For<ILogger<BulkEmployeeImportService>>();

    public BulkImportAuditWriteTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.Subdomain.Returns("test");
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(_actorId);
        _currentUser.IsAuthenticated.Returns(true);
    }

    private BulkEmployeeImportService CreateService() =>
        new(TestDbContextFactory.Create(_tenantContext, _dbName), _tenantContext, _currentUser, _logger,
            DataProtectionProvider.Create(nameof(BulkImportAuditWriteTests)));

    private async Task SeedTenantAndReferenceData()
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "test", Name = "Test Tenant", Status = TenantStatus.Active,
        });
        db.Departments.Add(new Department
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TitleName = "Software Engineer", IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    private static Stream Csv(params string[] rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("first_name,last_name,email,phone,date_of_birth,gender,date_of_joining,department_name,job_title_name,employment_type,location_name,status");
        foreach (var r in rows) sb.AppendLine(r);
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    // ── BUG-022: successful import → Employee.BulkImported audit row ────────

    [Fact]
    public async Task BulkImport_WritesAuditRowWithCounts_BUG022()
    {
        await SeedTenantAndReferenceData();

        var stream = Csv(
            "John,Doe,john@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,",
            "Jane,Smith,jane@test.com,,,,2026-02-01,Engineering,Software Engineer,Part-Time,,",
            "Alex,Brown,alex@test.com,,,,2026-03-01,Engineering,Software Engineer,Contract,,");

        var result = await CreateService().ImportAsync(stream, "import.csv", stream.Length, false);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Success.Should().Be(3);
        var jobId = result.Value.JobId!.Value;

        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var rows = await db.AuditLogs.Where(a => a.TenantId == _tenantId).ToListAsync();

        var audit = rows.SingleOrDefault(a =>
            (a.Action ?? a.EventType ?? string.Empty).Contains("Import"));
        audit.Should().NotBeNull("a completed bulk import must write an Employee.BulkImported audit row");

        audit!.ResourceId.Should().Be(jobId.ToString(), "the audit row must reference the import job");
        audit.TenantId.Should().Be(_tenantId, "the audit row must be tenant-scoped");
        audit.UserId.Should().Be(_actorId, "the audit row must be attributed to the acting user");

        // AC: counts recorded in Detail (or the after-snapshot) — tie the row to the real outcome.
        var payload = (audit.Detail ?? string.Empty) + (audit.After ?? string.Empty);
        payload.Should().Contain("3", "the import outcome counts must be captured in the audit row");
    }
}
