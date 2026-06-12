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

