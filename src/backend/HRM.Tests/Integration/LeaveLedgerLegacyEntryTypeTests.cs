// ============================================================================
// US-LV-002 / BUG-037 + BUG-086 regression: a leave_ledger row persisted with
// the legacy entry_type string "Accrued" (not a LedgerEntryType member) must
// still materialize — as Accrual — instead of throwing during EF materialization
// and 500-ing every balance / utilization read that touches it.
//
// PROVIDER: a real PostgreSQL Testcontainer (the value-conversion happens on
// read regardless of provider, but this reproduces the exact stored-string path).
//
// Pre-fix: EntryType used the default .HasConversion<string>(), whose read side
// does Enum.Parse and throws on "Accrued". Post-fix: a tolerant converter maps
// the "Accrued" alias to Accrual (unknown values still throw).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class LeaveLedgerLegacyEntryTypeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private AppDbContext CreateContext()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new TenantInterceptor(tenantContext),
                new AuditInterceptor(currentUser))
            .Options;

        return new AppDbContext(options, tenantContext);
    }

    [Fact]
    public async Task Read_LedgerRowWithLegacyAccruedString_MaterializesAsAccrual()
    {
        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();

            var deptId = Guid.NewGuid();
            var jobTitleId = Guid.NewGuid();
            var employeeId = Guid.NewGuid();
            var leaveTypeId = Guid.NewGuid();
            var ledgerId = Guid.NewGuid();

            setup.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme Corp" });
            setup.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true });
            setup.JobTitles.Add(new JobTitle { Id = jobTitleId, TenantId = _tenantId, TitleName = "Backend Engineer", IsActive = true });
            setup.Employees.Add(new Employee
            {
                Id = employeeId,
                TenantId = _tenantId,
                EmployeeNo = "EMP-0001",
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada@acme.com",
                DateOfJoining = DateTime.UtcNow.AddYears(-1),
                DepartmentId = deptId,
                JobTitleId = jobTitleId,
                EmploymentType = EmploymentType.FullTime,
                IsActive = true,
            });
            setup.LeaveTypes.Add(new LeaveType
            {
                Id = leaveTypeId,
                TenantId = _tenantId,
                Name = "Annual Leave",
                AnnualEntitlement = 20m,
                DisplayOrder = 1,
            });
            setup.Set<LeaveLedger>().Add(new LeaveLedger
            {
                Id = ledgerId,
                TenantId = _tenantId,
                EntryType = LedgerEntryType.Accrual,
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                LeaveYear = 2026,
                Amount = 20m,
                BalanceAfter = 20m,
                OccurredAt = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();

            // Simulate the legacy/bad row: overwrite the persisted enum name with the "Accrued" alias.
            var updated = await setup.Database.ExecuteSqlRawAsync(
                "UPDATE leave_ledger SET entry_type = 'Accrued' WHERE id = {0}", ledgerId);
            updated.Should().Be(1);
        }

        // Fresh context → forces materialization of the "Accrued" string from the DB.
        await using var read = CreateContext();

        var entry = await read.Set<LeaveLedger>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();

        // Pre-fix: this SingleAsync threw (Enum.Parse could not parse "Accrued").
        entry.EntryType.Should().Be(LedgerEntryType.Accrual);
    }
}
