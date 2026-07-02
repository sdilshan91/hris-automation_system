// ============================================================================
// BUG-001 verification (HTTP-level): the impersonation read-only gate is decided
// from the initiator's REAL JWT roles. The existing unit test mocked
// ICurrentUser.Roles, so it never exercised the token round-trip. This test logs
// in as a genuine System Support user and asserts the started session is
// read-only — proving the gate works end-to-end (the original finding tested with
// a System Admin, who correctly gets a FULL session).
// ============================================================================

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Tests.Integration.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

[Collection("HttpApi")]
public sealed class ImpersonationReadOnlyHttpTests
{
    private readonly ApiTestFactory _factory;
    public ImpersonationReadOnlyHttpTests(ApiTestFactory factory) => _factory = factory;

    private const string SupportPassword = "Support@123!";

    [Fact]
    public async Task Start_AsRealSystemSupportUser_ReturnsReadOnlySession()
    {
        var (supportEmail, targetUserId, targetTenantId) = await SeedAsync();

        var client = await _factory.CreateAuthedClientAsync("platform", supportEmail, SupportPassword);

        // DIAGNOSTIC: what roles does the operator's real token actually carry?
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        var roleClaims = string.Join("|", jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value));
        roleClaims.Should().Contain("System Support", $"token roles were: [{roleClaims}]");

        var resp = await client.PostAsJsonAsync("/api/v1/system/impersonation", new
        {
            targetUserId,
            targetTenantId,
            reason = "verify the read-only support gate",
        });

        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.Created, raw);

        using var doc = JsonDocument.Parse(raw);
        var isReadOnly = doc.RootElement.GetProperty("data").GetProperty("isReadOnly").GetBoolean();
        isReadOnly.Should().BeTrue("a genuine System Support initiator's session must be read-only (BUG-001)");
    }

    private async Task<(string SupportEmail, Guid TargetUserId, Guid TargetTenantId)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var platform = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Subdomain == "platform");
        var supportRole = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.TenantId == platform.Id && r.Name == "System Support");

        // System Support operator in the platform tenant, assigned the seeded System Support role.
        var supportEmail = $"support-{Guid.NewGuid():N}@platform.test";
        var support = new User
        {
            Id = Guid.NewGuid(), Email = supportEmail, IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(SupportPassword, workFactor: 12),
        };
        db.Users.Add(support);
        var supportMembership = new UserTenant
        {
            Id = Guid.NewGuid(), UserId = support.Id, TenantId = platform.Id, Status = UserTenantStatus.Active,
        };
        db.UserTenants.Add(supportMembership);
        db.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = supportMembership.Id, RoleId = supportRole.Id, AssignedAt = DateTime.UtcNow, AssignedBy = "test",
        });

        // A separate ACTIVE business tenant + a member to impersonate.
        var targetTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Subdomain = $"tgt{Guid.NewGuid():N}"[..14],
            Name = "Target Co", Status = TenantStatus.Active, PlanId = "default",
        };
        db.Tenants.Add(targetTenant);
        var target = new User { Id = Guid.NewGuid(), Email = $"target-{Guid.NewGuid():N}@t.test", IsActive = true };
        db.Users.Add(target);
        db.UserTenants.Add(new UserTenant
        {
            Id = Guid.NewGuid(), UserId = target.Id, TenantId = targetTenant.Id, Status = UserTenantStatus.Active,
        });

        await db.SaveChangesAsync();
        return (supportEmail, target.Id, targetTenant.Id);
    }
}
