// ============================================================================
// US-NTF-004 (BR-6): GDPR "right to be forgotten" — AuditAnonymizationService.
// Verifies a user's PII (IP, user-agent, actor email, and identifiers inside the
// before/after/detail JSON) is replaced with REDACTED-{id} IN PLACE, the row
// count is preserved (rows are not deleted), other users' rows are untouched, and
// the operation is idempotent. EF Core InMemory only.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AuditAnonymizationServiceTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _targetUser = Guid.NewGuid();
    private readonly Guid _otherUser = Guid.NewGuid();
    private const string TargetEmail = "forgetme@acme.com";
    private const string OtherEmail = "keepme@acme.com";
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ILogger<AuditAnonymizationService> _logger =
        Substitute.For<ILogger<AuditAnonymizationService>>();

    private AppDbContext Db()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(_tenant);
        ctx.IsResolved.Returns(true);
        return TestDbContextFactory.Create(ctx, _dbName);
    }

    private AuditAnonymizationService Service() => new(Db(), _logger);

    private async Task SeedAsync()
    {
        using var db = Db();
        db.Users.Add(new User { Id = _targetUser, Email = TargetEmail, DisplayName = "Forget Me" });
        db.Users.Add(new User { Id = _otherUser, Email = OtherEmail, DisplayName = "Keep Me" });

        // Two rows attributable to the target user (as actor) with PII in payload.
        db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenant,
            UserId = _targetUser,
            EventType = "Employee.Update",
            Action = "Employee.Update",
            ResourceType = "Employee",
            ResourceId = "emp-1",
            Before = $"{{\"email\":\"{TargetEmail}\"}}",
            After = $"{{\"email\":\"{TargetEmail}\"}}",
            IpAddress = "203.0.113.7",
            UserAgent = "Mozilla/5.0",
            ActorEmployeeNo = TargetEmail,
            CreatedAt = DateTime.UtcNow,
        });
        db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenant,
            UserId = _targetUser,
            EventType = "login_success",
            Action = "login_success",
            IpAddress = "203.0.113.7",
            UserAgent = "Mozilla/5.0",
            CreatedAt = DateTime.UtcNow,
        });

        // One row for ANOTHER user — must be untouched.
        db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenant,
            UserId = _otherUser,
            EventType = "Employee.Update",
            Action = "Employee.Update",
            IpAddress = "198.51.100.2",
            UserAgent = "Chrome",
            ActorEmployeeNo = OtherEmail,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Anonymize_redacts_target_user_pii_in_place()
    {
        await SeedAsync();
        var token = $"REDACTED-{_targetUser}";

        var changed = await Service().AnonymizeUserAsync(_targetUser);

        changed.Should().Be(2);

        using var read = Db();
        var rows = await read.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.UserId == _targetUser)
            .ToListAsync();

        rows.Should().HaveCount(2); // BR-1: rows preserved, not deleted.
        rows.Should().OnlyContain(r => r.IpAddress == token);
        rows.Should().OnlyContain(r => r.UserAgent == token);

        var emailRow = rows.Single(r => r.Action == "Employee.Update");
        emailRow.ActorEmployeeNo.Should().Be(token);
        emailRow.Before.Should().NotContain(TargetEmail);
        emailRow.After.Should().NotContain(TargetEmail);
        emailRow.Before.Should().Contain(token);
    }

    [Fact]
    public async Task Anonymize_does_not_touch_other_users_rows()
    {
        await SeedAsync();

        await Service().AnonymizeUserAsync(_targetUser);

        using var read = Db();
        var other = await read.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.UserId == _otherUser);

        other.IpAddress.Should().Be("198.51.100.2");
        other.UserAgent.Should().Be("Chrome");
        other.ActorEmployeeNo.Should().Be(OtherEmail);
    }

    [Fact]
    public async Task Anonymize_is_idempotent()
    {
        await SeedAsync();

        await Service().AnonymizeUserAsync(_targetUser);
        var secondPass = await Service().AnonymizeUserAsync(_targetUser);

        secondPass.Should().Be(0); // already redacted → no further changes.
    }

    [Fact]
    public async Task Anonymize_empty_user_is_noop()
    {
        await SeedAsync();
        (await Service().AnonymizeUserAsync(Guid.Empty)).Should().Be(0);
    }
}
