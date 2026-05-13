using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Persistence;

public static class DbInitializer
{
    private const string DefaultAdminEmail = "admin@hrm.local";
    private const string DefaultAdminPassword = "Admin@123!";
    private const string DefaultTenantSubdomain = "platform";
    private const string DefaultTenantName = "HRM Platform Admin";
    private const string SystemAdminRoleName = "SystemAdmin";

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        logger.LogInformation("Applying EF Core migrations to {Database}", dbContext.Database.GetDbConnection().Database);
        await dbContext.Database.MigrateAsync(cancellationToken);

        await SeedAsync(dbContext, logger, cancellationToken);
    }

    private static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Subdomain == DefaultTenantSubdomain, ct);

        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = BaseEntity.NewUuidV7(),
                Subdomain = DefaultTenantSubdomain,
                Name = DefaultTenantName,
                Status = TenantStatus.Active,
                ContactEmail = DefaultAdminEmail,
                CreatedAt = DateTime.UtcNow,
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded default admin tenant {Subdomain}", tenant.Subdomain);
        }

        var role = await db.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Name == SystemAdminRoleName, ct);

        if (role is null)
        {
            role = new Role
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenant.Id,
                Name = SystemAdminRoleName,
                Description = "Platform administrator with full access",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Role} role for tenant {Subdomain}", role.Name, tenant.Subdomain);
        }

        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == DefaultAdminEmail, ct);

        if (user is null)
        {
            user = new User
            {
                Id = BaseEntity.NewUuidV7(),
                Email = DefaultAdminEmail,
                DisplayName = "Platform Administrator",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultAdminPassword, workFactor: 12),
                IsActive = true,
                PasswordChangedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded default admin user {Email}", user.Email);
        }

        var userTenant = await db.UserTenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ut => ut.UserId == user.Id && ut.TenantId == tenant.Id, ct);

        if (userTenant is null)
        {
            userTenant = new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = user.Id,
                TenantId = tenant.Id,
                Status = UserTenantStatus.Active,
                CreatedAt = DateTime.UtcNow,
            };
            db.UserTenants.Add(userTenant);
            await db.SaveChangesAsync(ct);
        }

        var roleAssigned = await db.UserTenantRoles
            .IgnoreQueryFilters()
            .AnyAsync(utr => utr.UserTenantId == userTenant.Id && utr.RoleId == role.Id, ct);

        if (!roleAssigned)
        {
            db.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = userTenant.Id,
                RoleId = role.Id,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = "system-seed",
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Assigned {Role} to admin user", SystemAdminRoleName);
        }
    }
}
