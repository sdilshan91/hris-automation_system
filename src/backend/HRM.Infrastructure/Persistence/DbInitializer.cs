using HRM.Domain.Authorization;
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
        var defaultEnabledModules = PermissionCatalog.ByModule.Keys.OrderBy(module => module).ToList();
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
                PlanId = "default",
                EnabledModules = defaultEnabledModules,
                ContactEmail = DefaultAdminEmail,
                CreatedAt = DateTime.UtcNow,
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded default admin tenant {Subdomain}", tenant.Subdomain);
        }
        else if (string.IsNullOrWhiteSpace(tenant.PlanId) || tenant.EnabledModules.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(tenant.PlanId))
            {
                tenant.PlanId = "default";
            }

            if (tenant.EnabledModules.Count == 0)
            {
                tenant.EnabledModules = defaultEnabledModules;
            }

            tenant.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Updated default admin tenant metadata for {Subdomain}", tenant.Subdomain);
        }

        // Seed SystemAdmin role (platform-level) with all permissions
        var systemAdminRole = await db.Roles
            .IgnoreQueryFilters()
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Name == SystemAdminRoleName, ct);

        if (systemAdminRole is null)
        {
            systemAdminRole = new Role
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenant.Id,
                Name = SystemAdminRoleName,
                Description = "Platform administrator with full access",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
            };

            // SystemAdmin gets all permissions from the catalog
            foreach (var perm in PermissionCatalog.AllPermissions)
            {
                systemAdminRole.RolePermissions.Add(new RolePermission
                {
                    RoleId = systemAdminRole.Id,
                    Permission = perm,
                });
            }

            db.Roles.Add(systemAdminRole);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Role} role with {Count} permissions for tenant {Subdomain}",
                systemAdminRole.Name, PermissionCatalog.AllPermissions.Count, tenant.Subdomain);
        }

        // Seed built-in tenant roles with their default permissions (FR-2)
        await SeedBuiltInTenantRolesAsync(db, tenant.Id, logger, ct);

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
            .AnyAsync(utr => utr.UserTenantId == userTenant.Id && utr.RoleId == systemAdminRole.Id, ct);

        if (!roleAssigned)
        {
            db.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = userTenant.Id,
                RoleId = systemAdminRole.Id,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = "system-seed",
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Assigned {Role} to admin user", SystemAdminRoleName);
        }
    }

    /// <summary>
    /// Seeds built-in tenant roles (Tenant Owner, Tenant Admin, HR Manager, HR Officer,
    /// Manager, Employee, Recruiter, Auditor) with their default permissions.
    /// These roles are marked as is_built_in and cannot be edited/deleted by tenants.
    /// Called during tenant provisioning.
    /// </summary>
    private static async Task SeedBuiltInTenantRolesAsync(
        AppDbContext db, Guid tenantId, ILogger logger, CancellationToken ct)
    {
        foreach (var roleName in PermissionCatalog.BuiltInRoles.All)
        {
            var exists = await db.Roles
                .IgnoreQueryFilters()
                .AnyAsync(r => r.TenantId == tenantId && r.Name == roleName, ct);

            if (exists)
                continue;

            var role = new Role
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = roleName,
                Description = $"Built-in {roleName} role",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
            };

            // Assign default permissions from the catalog
            var defaultPerms = PermissionCatalog.DefaultPermissionsFor(roleName);
            foreach (var perm in defaultPerms)
            {
                role.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    Permission = perm,
                });
            }

            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded built-in role {Role} with {Count} permissions for tenant {TenantId}",
                roleName, defaultPerms.Count, tenantId);
        }
    }
}
