using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using HRM.Api.Filters;
using HRM.Api.Middleware;
using HRM.Application.Common.Behaviors;
using HRM.Infrastructure;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Polly;
using Polly.Extensions.Http;
using Serilog;

// ===== Serilog Bootstrap =====
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting HRM API");

    var builder = WebApplication.CreateBuilder(args);

    // ===== Serilog =====
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "HRM.Api");
    });

    // ===== Infrastructure (DbContext, Auth, JWT, TenantContext) =====
    builder.Services.AddInfrastructure(builder.Configuration);

    // ===== MediatR + Pipeline Behaviors =====
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(typeof(HRM.Application.Common.Behaviors.ValidationBehavior<,>).Assembly);
    });
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

    // ===== FluentValidation =====
    builder.Services.AddValidatorsFromAssembly(typeof(HRM.Application.Common.Behaviors.ValidationBehavior<,>).Assembly);

    // ===== JWT Authentication =====
    // Build a temporary service provider to get the JwtService for token validation parameters
    var jwtService = new JwtService(builder.Configuration);
    builder.Services.AddSingleton(jwtService);
    builder.Services.AddSingleton<HRM.Application.Common.Interfaces.IJwtService>(jwtService);

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = jwtService.GetTokenValidationParameters();
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Log.Warning("JWT authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

    builder.Services.AddAuthorization();

    // ===== Controllers =====
    builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ValidationFilter>();
    });

    builder.Services.AddHttpContextAccessor();

    // ===== Swagger =====
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "HRM SaaS API",
            Version = "v1",
            Description = "Multi-tenant Human Resource Management platform API"
        });

        // JWT Bearer auth in Swagger
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter your JWT access token"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ===== Hangfire =====
    builder.Services.AddHangfire(config =>
    {
        config.UsePostgreSqlStorage(options =>
        {
            options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")!);
        });
    });
    builder.Services.AddHangfireServer();

    // ===== Background Jobs =====
    builder.Services.AddScoped<HRM.Api.Jobs.TokenCleanupJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.SendLockoutNotificationJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ApplyFutureDatedStatusChangesJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ProbationReminderJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.BulkEmployeeImportJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.LeaveAccrualJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.HolidayRecurrenceJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ProcessLeaveYearEndJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ProcessCarryForwardExpiryJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ProcessAbsenteeismJob>();

    // ===== Polly (HTTP resilience for external service calls) =====
    builder.Services.AddHttpClient("ResilientClient")
        .AddPolicyHandler(GetRetryPolicy())
        .AddPolicyHandler(GetCircuitBreakerPolicy());

    // ===== CORS =====
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy
                .SetIsOriginAllowed(origin =>
                {
                    var uri = new Uri(origin);
                    var baseDomain = builder.Configuration["Platform:BaseDomain"] ?? "yourhrm.com";
                    return uri.Host.EndsWith($".{baseDomain}") ||
                           uri.Host == baseDomain ||
                           uri.Host == "localhost";
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    // ===== Build App =====
    var app = builder.Build();

    // ===== Middleware Pipeline =====
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "HRM API v1");
            options.RoutePrefix = "swagger";
        });
    }

    // Exception handling (outermost)
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // Tenant resolution (before auth)
    app.UseMiddleware<TenantResolutionMiddleware>();

    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();

    // Session activity tracking — debounced last_active_at update (US-AUTH-009 FR-4)
    app.UseMiddleware<SessionActivityMiddleware>();

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

    app.MapControllers();

    // Hangfire dashboard (dev only)
    if (app.Environment.IsDevelopment())
    {
        app.UseHangfireDashboard(builder.Configuration["Hangfire:DashboardPath"] ?? "/hangfire");
    }

    // Apply EF migrations and seed defaults on startup
    await DbInitializer.RunAsync(app.Services);

    // Register recurring jobs (uses IRecurringJobManager from the service-based API
    // because JobStorage.Current is only initialised after the Hangfire server starts)
    using (var scope = app.Services.CreateScope())
    {
        var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.TokenCleanupJob>(
            "cleanup-expired-tokens",
            job => job.RunAsync(),
            Cron.Daily);

        // US-CHR-008 FR-8 / BR-4: Document expiry notification job (daily at 09:00 UTC)
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.DocumentExpiryNotificationJob>(
            "document-expiry-notifications",
            job => job.RunAsync(),
            "0 9 * * *"); // 09:00 UTC daily

        // US-CHR-009 BR-4: Apply future-dated employee status changes (daily at 00:15 UTC)
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.ApplyFutureDatedStatusChangesJob>(
            "apply-future-dated-status-changes",
            job => job.RunAsync(),
            "15 0 * * *"); // 00:15 UTC daily

        // US-CHR-009 FR-6 / BR-6: Probation end date reminder (daily at 08:00 UTC)
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.ProbationReminderJob>(
            "probation-end-reminders",
            job => job.RunAsync(),
            "0 8 * * *"); // 08:00 UTC daily

        // US-LV-002 FR-5 / AC-5: Leave entitlement accrual processing (daily at 00:30 UTC)
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.LeaveAccrualJob>(
            "leave-entitlement-accruals",
            job => job.RunAsync(),
            "30 0 * * *"); // 00:30 UTC daily

        // US-LV-007 FR-3 / BR-5: Recurring-holiday next-year generation (1 Dec, ~30 days before
        // year-end). Idempotent, so a daily-or-later cadence in December is safe.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.HolidayRecurrenceJob>(
            "holiday-recurrence-generation",
            job => job.RunAsync(),
            "0 1 1 12 *"); // 01:00 UTC on 1 December

        // US-LV-008 FR-2 / AC-1: Year-end leave carry-forward + forfeiture (1 January, processes
        // the just-closed year). Idempotent, so a daily-or-later cadence in early January is safe.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.ProcessLeaveYearEndJob>(
            "leave-year-end-carry-forward",
            job => job.RunAsync(),
            "0 2 1 1 *"); // 02:00 UTC on 1 January

        // US-LV-008 FR-3 / AC-3: Monthly carry-forward expiry sweep (1st of each month).
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.ProcessCarryForwardExpiryJob>(
            "leave-carry-forward-expiry",
            job => job.RunAsync(),
            "0 3 1 * *"); // 03:00 UTC on the 1st of every month

        // US-LV-011 FR-2 / AC-2: Daily absenteeism auto-LOP reconciliation. Generates LOP entries for
        // unaccounted absences in the previous month. NoOp until the attendance module lands (the
        // IAttendanceProvider seam returns no absences), but wired/idempotent/tenant-safe.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.ProcessAbsenteeismJob>(
            "leave-absenteeism-lop",
            job => job.RunAsync(),
            "0 4 * * *"); // 04:00 UTC daily
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// ===== Polly Policies =====
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
}

