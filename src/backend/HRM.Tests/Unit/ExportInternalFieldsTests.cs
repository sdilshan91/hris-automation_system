// ============================================================================
// ISSUE-014 (US-ADM-010 FR-3) — per-entity export CSVs must NOT dump internal EF/audit
// columns (TenantId, CreatedBy/UpdatedBy, CreatedAt/UpdatedAt, IsDeleted, RowVersion).
// The reflection-mode serializer now excludes ExportInternalFields in addition to the
// auth-secret deny-list, so the header row matches the business schema. The primary key
// (Id) is deliberately RETAINED as a cross-file join key.
//
// Pure unit tests over the matcher AND over CsvSerializer.ExportableProperties(typeof(Employee))
// so the actual emitted header column set is asserted.
// ============================================================================

using FluentAssertions;
using HRM.Application.Features.DataExport;
using HRM.Domain.Entities;

namespace HRM.Tests.Unit;

public sealed class ExportInternalFieldsTests
{
    [Theory]
    [InlineData("TenantId")]
    [InlineData("tenant_id")]
    [InlineData("CreatedAt")]
    [InlineData("CreatedBy")]
    [InlineData("UpdatedAt")]
    [InlineData("UpdatedBy")]
    [InlineData("IsDeleted")]
    [InlineData("RowVersion")]
    [InlineData("Version")]
    public void InternalInfraColumns_AreExcluded(string property)
        => ExportInternalFields.IsInternal(property).Should().BeTrue();

    [Theory]
    [InlineData("Id")]                  // join key — RETAINED
    [InlineData("EmployeeNo")]
    [InlineData("FirstName")]
    [InlineData("Email")]
    [InlineData("BankAccountNumber")]
    [InlineData("DepartmentId")]
    public void BusinessColumns_AreNotExcluded(string property)
        => ExportInternalFields.IsInternal(property).Should().BeFalse();

    [Fact]
    public void ExportableProperties_ForEmployee_ExcludesInternalColumns_KeepsBusinessColumns()
    {
        var headers = CsvSerializer.ExportableProperties(typeof(Employee))
            .Select(p => p.Name)
            .ToList();

        // ISSUE-014: the internal persistence/audit plumbing must NOT be in the exported header.
        headers.Should().NotContain("TenantId");
        headers.Should().NotContain("CreatedBy");
        headers.Should().NotContain("UpdatedBy");
        headers.Should().NotContain("CreatedAt");
        headers.Should().NotContain("UpdatedAt");
        headers.Should().NotContain("IsDeleted");
        headers.Should().NotContain("RowVersion");

        // The tenant's real business fields (incl. PII per FR-8) and the Id join key remain.
        headers.Should().Contain("Id");
        headers.Should().Contain("EmployeeNo");
        headers.Should().Contain("FirstName");
        headers.Should().Contain("Email");
        headers.Should().Contain("BankAccountNumber");
    }
}
