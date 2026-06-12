using HRM.Application.Common.Interfaces;
using HRM.Domain.Interfaces;
using HRM.Infrastructure.Caching;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure;

/// <summary>
/// Registers all infrastructure services: DbContext, repositories, JWT, auth, tenant context, and RBAC.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Tenant context (scoped per request)
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();

        // EF Core interceptors
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<TenantInterceptor>();

        // DbContext with PostgreSQL + snake_case naming
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention();

            // Add interceptors
            var tenantInterceptor = serviceProvider.GetRequiredService<TenantInterceptor>();
            var auditInterceptor = serviceProvider.GetRequiredService<AuditInterceptor>();
            options.AddInterceptors(tenantInterceptor, auditInterceptor);
        });

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());

        // TOTP service (singleton — no per-request state)
        services.AddSingleton<ITotpService, TotpService>();

        // Note: JwtService is registered in Program.cs alongside JWT authentication config.
        // Auth service
        services.AddScoped<IAuthService, AuthService>();

        // Lockout notification service (US-AUTH-010 FR-8)
        services.AddScoped<ILockoutNotificationService, LockoutNotificationService>();

        // RBAC service
        services.AddScoped<IRoleService, RoleService>();

        // Department service (US-CHR-004)
        services.AddScoped<IDepartmentService, DepartmentService>();

        // Job title service (US-CHR-005)
        services.AddScoped<IJobTitleService, JobTitleService>();

        // Employee service (US-CHR-001)
        services.AddScoped<IEmployeeService, EmployeeService>();

        // Employee directory service (US-CHR-003)
        services.AddScoped<IEmployeeDirectoryService, EmployeeDirectoryService>();

        // File storage (US-CHR-001 FR-6)
        // Dev: local filesystem; Prod: swap to Azure Blob / S3 / MinIO implementation.
        services.AddSingleton<IFileStorage>(sp =>
        {
            var basePath = configuration["FileStorage:BasePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
            var logger = sp.GetRequiredService<ILogger<LocalFileStorage>>();
            return new LocalFileStorage(basePath, logger);
        });

        // Virus scanner (US-CHR-001 NFR-3)
        // TODO(prod): Wire ClamAV or equivalent production scanner.
        services.AddSingleton<IVirusScanner, AllowWithLogVirusScanner>();

        // Permission cache (in-memory default; TODO: swap to Redis for production — see NFR-2)
        services.AddSingleton<IPermissionCache, InMemoryPermissionCache>();

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = configuration["Redis:InstanceName"] ?? "hrm:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // Permission-based authorization
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, TeamScopeAuthorizationHandler>();

        return services;
    }
}
