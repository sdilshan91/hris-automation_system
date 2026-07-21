using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using HRM.Api.Auth;
using HRM.Api.Filters;
using HRM.Api.Middleware;
using HRM.Api.Observability;
using HRM.Api.Redis;
using HRM.Application.Common.Behaviors;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using System.Threading.RateLimiting;

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

    // ===== Shared Redis multiplexer (Redis command-spans, plan §5) =====
    // Build ONE IConnectionMultiplexer (Redis-gated, fail-open) BEFORE AddInfrastructure + AddSignalR so both
    // IDistributedCache and the SignalR backplane route through the same instance — which lets OTel's
    // AddRedisInstrumentation (ObservabilityExtensions) emit command-spans for both and consolidates the pools.
    // Returns null when no Redis connection string is configured (the shipped default) → in-memory fallbacks.
    var sharedRedis = builder.Services.AddSharedRedisMultiplexer(builder.Configuration);
    if (sharedRedis is not null)
    {
        Log.Information("Shared Redis multiplexer registered (IDistributedCache + SignalR backplane + OTel instrumentation).");
    }

    // ===== Infrastructure (DbContext, Auth, JWT, TenantContext) =====
    builder.Services.AddInfrastructure(builder.Configuration);

    // ===== Observability (P3: OpenTelemetry traces + metrics) =====
    // Endpoint-gated + safe-by-default: OTLP export when OpenTelemetry:OtlpEndpoint (or the standard
    // OTEL_EXPORTER_OTLP_ENDPOINT env var) is set; Console exporter only when blank, so the app runs with
    // no collector. See HRM.Api/Observability/ObservabilityExtensions.cs + docs/Architecture/observability-otel-grafana-plan.md.
    builder.Services.AddObservability(builder.Configuration, builder.Environment);

    // ===== Health Checks (P3 infra probes: /health/live, /health/ready — mapped below) =====
    // Liveness = process is up (self check, no external deps, tag "live"). Readiness = downstream deps
    // reachable before we take traffic (tag "ready"): PostgreSQL always; Redis ONLY when configured, so a
    // blank Redis connection string never fails readiness. Probes are anonymous (see mapping note below).
    var healthChecks = builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("API process is running."), tags: ["live"]);

    healthChecks.AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgres",
        tags: ["ready"]);

    var healthRedisConn = builder.Configuration.GetConnectionString("Redis")
        ?? builder.Configuration["Redis:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(healthRedisConn))
    {
        // P3 Redis caching (Scope C): Redis is a SOFT readiness dependency. The cache layer degrades
        // gracefully to the DB on a Redis outage (see DistributedCache*/module caches), so a Redis
        // failure must NOT 503 the readiness probe and pull the instance out of rotation. We report it
        // as Degraded on failure — the default MapHealthChecks status map returns 200 for Degraded (only
        // Unhealthy → 503), so /health/ready surfaces the Redis problem without failing readiness.
        // Postgres stays a HARD dependency (default failureStatus Unhealthy → 503).
        healthChecks.AddRedis(
            healthRedisConn, name: "redis", failureStatus: HealthStatus.Degraded, tags: ["ready"]);
    }

    // ===== MediatR + Pipeline Behaviors =====
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(typeof(HRM.Application.Common.Behaviors.ValidationBehavior<,>).Assembly);
    });
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    // US-ADM-003 (AC-5/AC-6/FR-3/FR-6): block writes under a read-only impersonation session and block the
    // FR-6 destructive ops even under a full impersonation. Runs before the handler (and before the tenant
    // transaction) so a forbidden write never touches the database.
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ImpersonationReadOnlyBehavior<,>));
    // US-PLT-002 (ISSUE-277): the RLS app.current_tenant GUC is now set at connection-open by
    // TenantGucConnectionInterceptor (a single tx-less set_config), NOT a per-request transaction. The former
    // TenantTransactionBehavior was retired because its request-wide tx broke under EnableRetryOnFailure and
    // nested with handlers that open their own transaction.

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
        // BUG-001: keep JWT claim names 1:1 with the token. With the default MapInboundClaims=true, the
        // handler remaps the "roles" claim to ClaimTypes.Role, so ICurrentUser.Roles (which reads the
        // literal "roles" claim) saw nothing — while "permissions" (non-standard, unmapped) worked. That
        // silently broke role-based gates like the System Support read-only impersonation check.
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Log.Warning("JWT authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            },
            // US-NTF-001 AC-1/FR-1: SignalR sends the JWT via the query string (?access_token=…) because
            // browsers cannot set the Authorization header on a WebSocket handshake. Read it for the
            // notifications hub path only, so normal HTTP endpoints continue to require the header.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs/notifications"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            // P3-2: real session revocation. After signature/lifetime validation, check the per-(tenant,user)
            // "revoked-before" cutoff in the ITokenDenylist (Redis-backed when configured, no-op otherwise) and
            // reject (401) any access token issued before a revoke — so offboarding / admin force-logout /
            // impersonation-end invalidate already-issued access tokens instead of leaving them valid to expiry.
            // MUST FAIL OPEN: any Redis/parse error or missing key → token accepted, so a Redis blip can never
            // lock everyone out.
            OnTokenValidated = async context =>
            {
                var denylist = context.HttpContext.RequestServices.GetService<ITokenDenylist>();
                if (await SessionRevocationCheck.IsRevokedAsync(
                        context.Principal, denylist, context.HttpContext.RequestAborted))
                {
                    Log.Information("Rejected access token: session revoked (issued before revocation cutoff).");
                    context.Fail("Session revoked");
                }
            }
        };
    });

    builder.Services.AddAuthorization();

    // ===== Controllers =====
    builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ValidationFilter>();
    })
    .AddJsonOptions(options =>
    {
        // US-PLT-003: serialize enums as their string names (PascalCase) instead of integers,
        // matching what the Angular frontend consumes. Deserialization is case-insensitive.
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

    builder.Services.AddHttpContextAccessor();

    // DF-25: IMemoryCache is NOT registered by default here — the EF second-level cache only provides it on
    // its memory-provider branch, which is SKIPPED whenever ConnectionStrings:Redis is set (base appsettings).
    // Register it unconditionally so SessionActivityMiddleware's per-tenant idle-timeout memoization resolves
    // in every environment (a Redis-configured Docker/staging/prod would otherwise 500 every authed request).
    builder.Services.AddMemoryCache();

    // ===== SignalR (US-NTF-001: real-time in-app notifications) =====
    // The notification hub is mapped at /hubs/notifications below. The Redis backplane (FR-10, multi-instance
    // scale-out) is OPTIONAL: it is only added when a Redis connection string is configured. Without it the
    // default in-memory backplane is used, so the app starts and all tests pass WITHOUT Redis running.
    var signalRBuilder = builder.Services.AddSignalR(options =>
    {
        // US-PLT-003 parity: serialize enums as strings over SignalR too, matching the controller JSON config.
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    });
    signalRBuilder.AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

    if (sharedRedis is not null)
    {
        // Route the backplane onto the SHARED, OTel-instrumented multiplexer (built above) instead of letting
        // AddStackExchangeRedis open its own connection — so SignalR's Redis commands are covered by the same
        // AddRedisInstrumentation and share the single connection pool. ConnectionFactory is
        // Func<TextWriter, Task<IConnectionMultiplexer>>; we return the already-connected shared instance.
        signalRBuilder.AddStackExchangeRedis(options =>
        {
            options.ConnectionFactory = _ => Task.FromResult(sharedRedis);
            options.Configuration.ChannelPrefix =
                StackExchange.Redis.RedisChannel.Literal(builder.Configuration["Redis:InstanceName"] ?? "hrm:signalr:");
        });
        Log.Information("SignalR Redis backplane enabled (shared multiplexer).");
    }
    else
    {
        Log.Information("SignalR using in-memory backplane (no Redis connection string configured).");
    }

    // US-NTF-001 FR-3/FR-4: the persist-then-push notification dispatcher. Lives here (not Infrastructure)
    // because it needs IHubContext<NotificationHub>. Other modules call INotificationService to raise a
    // real-time notification; the read/mark side (INotificationReadService) is registered in Infrastructure.
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.INotificationService,
        HRM.Api.Notifications.SignalRNotificationService>();

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

        // Disambiguate same-named DTOs across feature modules (e.g. Onboarding.TrendPointDto
        // vs Attendance.TrendPointDto) — the default bare type name collides and 500s
        // swagger.json. Keeps generic naming readable. See SwaggerSchemaIdGenerator.
        options.CustomSchemaIds(HRM.Api.Swagger.SwaggerSchemaIdGenerator.GetSchemaId);

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
    // RLS increment 2b: Hangfire bootstraps its OWN schema (DDL) and its tables carry no tenant policy, so under
    // RLS the runtime hrm_app role can neither own that schema nor bypass the policies. Point its storage at the
    // PRIVILEGED (hrm_owner) connection when one is configured; fall back to DefaultConnection when
    // PrivilegedConnection is blank (the committed default) so nothing changes until the increment-3 flip.
    var hangfireConnectionString =
        !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("PrivilegedConnection"))
            ? builder.Configuration.GetConnectionString("PrivilegedConnection")!
            : builder.Configuration.GetConnectionString("DefaultConnection")!;
    builder.Services.AddHangfire(config =>
    {
        config.UsePostgreSqlStorage(options =>
        {
            options.UseNpgsqlConnection(hangfireConnectionString);
        });
    });
    // The Hangfire background SERVER (job dispatcher + polling) can be disabled via config. The
    // storage and client API stay registered so enqueue/schedule calls still work; only the worker
    // loop is suppressed. Integration tests set Hangfire:DisableServer=true to avoid the polling
    // noise and to keep the server from racing the test DB lifecycle.
    if (!builder.Configuration.GetValue<bool>("Hangfire:DisableServer"))
    {
        builder.Services.AddHangfireServer();
    }

    // US-ADM-002 FR-5: the Hangfire monitoring seam for the System Admin dashboard's cross-tenant job-queue
    // view. Lives here alongside Hangfire; bound to IJobQueueMonitor so the Infrastructure monitoring service
    // does not hard-depend on a running Hangfire server (it degrades to an Available=false snapshot).
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IJobQueueMonitor, HRM.Api.Monitoring.HangfireJobQueueMonitor>();

    // ===== Background Jobs =====
    builder.Services.AddScoped<HRM.Api.Jobs.TokenCleanupJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ApplyFutureDatedStatusChangesJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ProbationReminderJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.AuditLogPurgeJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.BulkEmployeeImportJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.LeaveAccrualJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.HolidayRecurrenceJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ProcessLeaveYearEndJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ProcessCarryForwardExpiryJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ProcessAbsenteeismJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.AutoClockOutJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.SelfAssessmentReminderJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.ReviewSignoffAutoCloseJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.PipReminderJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.StaleGoalNudgeJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.DocumentExpiryNotificationJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.Feedback360ReminderJob>();

    // US-LV-012 FR-5: large leave-report exports run as a Hangfire background job. Bound to the
    // ILeaveReportExportJob interface so the Infrastructure report service can enqueue it by interface.
    builder.Services.AddScoped<HRM.Api.Jobs.LeaveReportExportJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.ILeaveReportExportJob, HRM.Api.Jobs.LeaveReportExportJob>();

    // US-ONB-002 NFR-3: onboarding notification-outbox dispatch worker (drains pending intent rows and
    // delivers via INotificationDispatcher). Bound to the interface so the assignment service can enqueue
    // it by interface. The dispatcher is log-only until the Notifications module (US-NTF-001/002) lands.
    builder.Services.AddScoped<HRM.Api.Jobs.OnboardingNotificationDispatchJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IOnboardingNotificationDispatchJob, HRM.Api.Jobs.OnboardingNotificationDispatchJob>();

    // US-NTF-006: real notification delivery infrastructure. RealNotificationDispatcher replaces the log-only
    // dispatcher: in-app via INotificationService (SignalR), email via the preference gate + template render +
    // the Hangfire SendEmailJob. The email leg still degrades to LogOnlyEmailSender when SMTP is unconfigured, so
    // this is safe with no SMTP server. (LoggingNotificationDispatcher remains in the tree as a fallback.)
    builder.Services.AddScoped<HRM.Api.Jobs.SendEmailJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.INotificationDispatcher, HRM.Api.Notifications.RealNotificationDispatcher>();

    // US-ONB-003 FR-6/AC-5/BR-4: daily overdue-task sweep (writes overdue outbox rows; tenant-tz deferred, UTC).
    builder.Services.AddScoped<HRM.Api.Jobs.OnboardingOverdueSweepJob>();

    // US-ADM-011b (AC-5/FR-8/NFR-3): per-tenant SLA-escalation sweep for overdue approval steps (every 5 min).
    builder.Services.AddScoped<HRM.Api.Jobs.WorkflowSlaEscalationJob>();

    // US-PAY-008 FR-3 (ISSUE-173): per-tenant SLA-escalation sweep for overdue PAYROLL approvals (every 5 min).
    builder.Services.AddScoped<HRM.Api.Jobs.PayrollApprovalSlaEscalationJob>();

    // US-ATT-007: monthly attendance summary jobs (daily refresh + monthly finalize) and the large-export
    // background job (bound to the interface so the Infrastructure service can enqueue it by interface).
    builder.Services.AddScoped<HRM.Api.Jobs.MonthlySummaryDailyJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.MonthlySummaryMonthlyJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.AttendanceSummaryExportJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IAttendanceSummaryExportJob, HRM.Api.Jobs.AttendanceSummaryExportJob>();

    // US-ATT-010 FR-8: scheduled attendance-report generation job (recurring). Email delivery deferred.
    builder.Services.AddScoped<HRM.Api.Jobs.ScheduledReportJob>();

    // US-REC-005 FR-4/BR-6: tenant-aware pre-interview reminder job + the Hangfire-backed scheduler seam
    // (bound to IInterviewReminderScheduler so the Infrastructure InterviewService can enqueue/cancel by
    // interface). Reminders are scheduled at create, swapped on reschedule, deleted on cancel.
    builder.Services.AddScoped<HRM.Api.Jobs.InterviewReminderJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IInterviewReminderScheduler, HRM.Api.Jobs.HangfireInterviewReminderScheduler>();

    // US-REC-007 FR-7/AC-4: tenant-aware offer-expiry job + the Hangfire-backed scheduler seam (bound to
    // IOfferExpiryScheduler so the Infrastructure OfferService can enqueue/cancel by interface). The expiry
    // job is scheduled when an offer is sent, cancelled on response/withdraw.
    builder.Services.AddScoped<HRM.Api.Jobs.OfferExpiryJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IOfferExpiryScheduler, HRM.Api.Jobs.HangfireOfferExpiryScheduler>();
    // ISSUE-262: the "N days before expiry" reminder counterpart (FR-7/AC-4) — the reminder is scheduled
    // when an offer is sent and cancelled on response/withdraw/supersede.
    builder.Services.AddScoped<HRM.Api.Jobs.OfferExpiryReminderJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IOfferExpiryReminderScheduler, HRM.Api.Jobs.HangfireOfferExpiryReminderScheduler>();

    // US-PRF-004 FR-5/AC-2/AC-5: tenant-aware cycle phase-transition job + the Hangfire-backed scheduler seam
    // (bound to ICyclePhaseScheduler so the Infrastructure AppraisalCycleService can schedule/cancel by
    // interface). A daily recurring job per cycle is (re)scheduled on activate/edit, cancelled on terminal
    // states. BUG-063: without this binding the optional scheduler param resolved to null and silently no-op'd.
    builder.Services.AddScoped<HRM.Api.Jobs.CyclePhaseTransitionJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.ICyclePhaseScheduler, HRM.Api.Jobs.HangfireCyclePhaseScheduler>();

    // US-PAY-003 FR-2/FR-3: tenant-aware payroll-run processing job + the Hangfire-backed scheduler seam
    // (bound to IPayrollRunJobScheduler so the Infrastructure PayrollRunService can enqueue by interface).
    // The job is enqueued by InitiatePayrollRun and restores the tenant context before computing.
    builder.Services.AddScoped<HRM.Api.Jobs.ProcessPayrollRunJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IPayrollRunJobScheduler, HRM.Api.Jobs.HangfirePayrollRunJobScheduler>();

    // BUG-118 (US-LV-002 AC-5): tenant-aware entitlement-recalc job + its Hangfire scheduler seam (bound to
    // ILeaveEntitlementRecalcJobScheduler so LeaveEntitlementService.UpdateRuleAsync can enqueue by interface).
    builder.Services.AddScoped<HRM.Api.Jobs.RecalcLeaveEntitlementsJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.ILeaveEntitlementRecalcJobScheduler, HRM.Api.Jobs.HangfireLeaveEntitlementRecalcScheduler>();
    // DF-4 (BUG-118): durability backstop for the best-effort post-commit recalc enqueue in UpdateRuleAsync —
    // a daily sweep that re-runs the idempotent recalc across active tenants so a dropped enqueue self-heals.
    builder.Services.AddScoped<HRM.Api.Jobs.LeaveEntitlementReconcileJob>();

    // US-PAY-004 FR-4: tenant-aware payslip-PDF generation job + the Hangfire-backed scheduler seam (bound to
    // IPayslipGenerationJobScheduler so the Infrastructure PayslipGenerationService can enqueue by interface).
    // The job is enqueued by GeneratePayslips and restores the tenant context before rendering.
    builder.Services.AddScoped<HRM.Api.Jobs.GeneratePayslipsJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IPayslipGenerationJobScheduler, HRM.Api.Jobs.HangfirePayslipGenerationJobScheduler>();
    // US-PAY-004 FR-8 (DF-31): single-slip payslip retry job (enqueued by the scheduler's employeeId overload).
    builder.Services.AddScoped<HRM.Api.Jobs.RetryPayslipJob>();

    // US-PAY-011 FR-1/FR-8: tenant-aware bulk payslip-email distribution job + the Hangfire-backed scheduler
    // seam (bound to IPayslipDistributionJobScheduler so the Infrastructure PayslipDistributionService can
    // enqueue by interface). The job restores the tenant context, then runs the per-employee send loop.
    builder.Services.AddScoped<HRM.Api.Jobs.SendPayslipEmailsJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IPayslipDistributionJobScheduler, HRM.Api.Jobs.HangfirePayslipDistributionJobScheduler>();

    // US-ADM-004 FR-2/FR-3/FR-6: tenant data-deletion + reminder jobs + the Hangfire-backed scheduler seam
    // (bound to ITenantDeletionScheduler so the Infrastructure lifecycle service can schedule/cancel by
    // interface). The deletion job is scheduled at TerminationScheduledAt; reminders at 14d/7d/1d before;
    // Restore de-queues them. Jobs restore the tenant context before running and are idempotent.
    builder.Services.AddScoped<HRM.Api.Jobs.TenantDeletionJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.TenantTerminationReminderJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.ITenantDeletionScheduler, HRM.Api.Jobs.HangfireTenantDeletionScheduler>();

    // US-ADM-010 AC-1/AC-2/FR-7: tenant data-export generation job + the Hangfire-backed scheduler seam (bound to
    // IExportJobScheduler so the Infrastructure export service can enqueue by interface), and the hourly
    // export-retention cleanup job (expires bundles past their 72h window + deletes their files).
    builder.Services.AddScoped<HRM.Api.Jobs.DataExportGenerationJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IExportJobScheduler, HRM.Api.Jobs.HangfireExportJobScheduler>();
    builder.Services.AddScoped<HRM.Api.Jobs.ExportCleanupJob>();

    // US-RPT-004 (FR-5/FR-8/BR-3): generic HR/leave/attendance report-export job (renders + stores + notifies for
    // large >= 1000-row exports), the Hangfire-backed scheduler seam (bound to IHrReportExportJobScheduler so the
    // Infrastructure export service can enqueue by interface), and the daily retention-cleanup job (expires report
    // exports past their 7-day window + deletes their files).
    builder.Services.AddScoped<HRM.Api.Jobs.HrReportExportJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IHrReportExportJob, HRM.Api.Jobs.HrReportExportJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IHrReportExportJobScheduler, HRM.Api.Jobs.HangfireHrReportExportJobScheduler>();
    builder.Services.AddScoped<HRM.Api.Jobs.HrReportExportCleanupJob>();

    // ISSUE-178 PR2: async large payroll-report export job (renders + stores + notifies for large >= 1000-row
    // exports), the Hangfire-backed scheduler seam (bound to IPayrollReportExportJobScheduler so the Infrastructure
    // export service can enqueue by interface), and the daily retention-cleanup job (expires payroll-report
    // exports past their 7-day window + deletes their files).
    builder.Services.AddScoped<HRM.Api.Jobs.PayrollReportExportJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IPayrollReportExportJob, HRM.Api.Jobs.PayrollReportExportJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IPayrollReportExportJobScheduler, HRM.Api.Jobs.HangfirePayrollReportExportJobScheduler>();
    builder.Services.AddScoped<HRM.Api.Jobs.PayrollReportExportCleanupJob>();

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

    // ===== Rate Limiting (ISSUE-052 / ISSUE-102) =====
    // Framework-native fixed-window limiter (no extra package, no Redis). Two named policies guard the
    // unauthenticated abuse vectors: password-reset email-bombing/enumeration and public application spam.
    // Partitioned by CLIENT IP. NOTE: the store is in-memory, so limits are PER API INSTANCE — acceptable
    // for this threat model (a coarse anti-abuse throttle, not a distributed quota). Move to a Redis-backed
    // partitioned limiter if/when multi-instance precision is required.
    // RateLimiting:Disabled (default false) lets the integration-test host opt out of the auth-login limiter
    // only — the shared "HttpApi" test collection logs in from one IP (TestServer partition "unknown") across
    // ~12 classes and would otherwise trip 10/min. forgot-password/public-application stay ON so their behavioral
    // 429 tests (RateLimitClusterApiTests) still pass. Production leaves the flag false (all limiters on).
    var rateLimitDisabled = builder.Configuration.GetValue<bool>("RateLimiting:Disabled");
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // ISSUE-052 (US-AUTH-004 FR-9): throttle POST /api/v1/auth/forgot-password. FR-9 specifies "max
        // 5/email/hour"; per-email would require reading the request body here, so the practical primary
        // defense is 5/hour/IP. TODO(FR-9 follow-up): add a per-email counter (body-inspected) on top of this.
        options.AddPolicy("forgot-password", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ResolveClientIp(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));

        // ISSUE-102 (US-REC-002): throttle the anonymous public application-submit endpoint. 10/hour/IP is a
        // defensible anti-spam / anti-bot ceiling for a legitimate applicant (who submits a handful of apps),
        // while blunting rapid unauthenticated write + disk-write DoS.
        options.AddPolicy("public-application", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ResolveClientIp(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));

        // US-AUTH-001/005 NFR-4: throttle the unauthenticated credential-check endpoints (login +
        // MFA-challenge). Credential-stuffing is otherwise only blunted by the per-account lockout counter,
        // which does nothing against attacks that spray one attempt each across many accounts from one host.
        // 10/minute/IP is generous for a human (mistyped password, MFA retry) while capping automated spray.
        options.AddPolicy("auth-login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ResolveClientIp(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitDisabled ? int.MaxValue : 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));
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

    // BUG-003: reject a request whose JWT tenant differs from the tenant resolved from the host/subdomain
    // (the spoofable X-Tenant-Subdomain dev header / subdomain). After auth (needs ICurrentUser), before
    // any tenant-scoped controller work. Does not touch resolution; a matching token+subdomain passes.
    app.UseMiddleware<TenantAccessGuardMiddleware>();

    // US-ADM-003 (AC-3/NFR-2): session-based revocation + expiry for impersonation tokens. After auth (needs the
    // resolved ICurrentUser), before controllers — rejects 401 once the session is ended/expired and best-effort
    // counts mutating actions. No-op for non-impersonated traffic.
    app.UseMiddleware<ImpersonationEnforcementMiddleware>();

    // US-ADM-004 (AC-1/AC-2/BR-6): enforce a resolved tenant's lifecycle status. Suspended ⇒ HTTP 451 for
    // tenant users (Tenant Owner/Admin exempt for the read-only notice + export); Terminating ⇒ read-only
    // (writes 403). System/admin context is exempt. After auth (needs ICurrentUser roles), before controllers.
    app.UseMiddleware<TenantStatusEnforcementMiddleware>();

    // Session activity tracking — debounced last_active_at update (US-AUTH-009 FR-4)
    app.UseMiddleware<SessionActivityMiddleware>();

    // Rate limiting (ISSUE-052 / ISSUE-102): after routing + auth so the matched endpoint's
    // [EnableRateLimiting("...")] metadata is resolved, before the endpoints execute.
    app.UseRateLimiter();

    // ===== Health check endpoints (P3 infra probes) =====
    // These are INFRA probes: anonymous by design. They bypass tenant resolution (TenantResolutionMiddleware
    // skips any "/health*" path) and carry no [Authorize] metadata, so UseAuthorization and the post-auth
    // tenant/impersonation/status middlewares all pass anonymous /health requests straight through — no JWT,
    // no resolved tenant required. Predicate-filtered by tag so live/ready expose the right dependency set.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live"),
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
    });

    // Back-compat: keep the original lightweight /health as a liveness alias so existing callers don't break.
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

    app.MapControllers();

    // US-NTF-001 AC-1: real-time in-app notification hub. JWT-authenticated; the token arrives via the
    // ?access_token= query string (handled in JwtBearerEvents.OnMessageReceived above).
    app.MapHub<HRM.Api.Hubs.NotificationHub>("/hubs/notifications");

    // Hangfire dashboard (dev only)
    if (app.Environment.IsDevelopment())
    {
        app.UseHangfireDashboard(builder.Configuration["Hangfire:DashboardPath"] ?? "/hangfire");
    }

    // Apply pending EF migrations and seed defaults on startup.
    // In Development, failures are logged but do NOT abort startup — the API still comes up (health
    // endpoints stay reachable) instead of crash-looping while you fix the DB. Outside Development we
    // fail fast: a failed migration may leave an outdated schema, and serving traffic against it can
    // return wrong data, so the app refuses to start and surfaces the error.
    try
    {
        await DbInitializer.RunAsync(app.Services);
    }
    catch (Exception ex) when (app.Environment.IsDevelopment())
    {
        Log.Error(ex, "Database initialization (migrations/seed) failed; the application will continue to "
            + "start because the environment is Development. The schema may be outdated — investigate "
            + "before relying on data operations.");
    }

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

        // US-ADM-008 FR-6 / BR-5: Audit-log retention purge — deletes audit rows older than each tenant's
        // plan-governed AuditLogRetentionDays window and logs the purge (daily at 04:00 UTC).
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.AuditLogPurgeJob>(
            "audit-log-retention-purge",
            job => job.RunAsync(),
            "0 4 * * *"); // 04:00 UTC daily

        // US-ADM-010 FR-7: hourly export-retention cleanup — expires export bundles past their 72h download
        // window and deletes their files (cross-tenant; runs in system context).
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.ExportCleanupJob>(
            "data-export-cleanup",
            job => job.RunAsync(),
            Cron.Hourly);

        // US-RPT-004 BR-3: daily report-export retention cleanup — expires report exports past their 7-day
        // download window and deletes their files (cross-tenant; runs in system context).
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.HrReportExportCleanupJob>(
            "hr-report-export-cleanup",
            job => job.RunAsync(),
            Cron.Daily);

        // ISSUE-178 PR2: daily payroll-report-export retention cleanup — expires payroll-report exports past their
        // 7-day download window and deletes their files (cross-tenant; runs in system context).
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.PayrollReportExportCleanupJob>(
            "payroll-report-export-cleanup",
            job => job.RunAsync(),
            Cron.Daily);

        // US-LV-002 FR-5 / AC-5: Leave entitlement accrual processing (daily at 00:30 UTC)
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.LeaveAccrualJob>(
            "leave-entitlement-accruals",
            job => job.RunAsync(),
            "30 0 * * *"); // 00:30 UTC daily

        // DF-4 (BUG-118): durability backstop for the best-effort post-commit recalc enqueue in
        // LeaveEntitlementService.UpdateRuleAsync. If Hangfire storage is unavailable in the enqueue window the
        // recalc is lost and a leave type's employees never converge to the edited rule. This daily sweep
        // re-runs the (idempotent) recalc across active tenants so a dropped enqueue self-heals — the
        // convergence bound for a lost enqueue is this cadence. Offset from the accrual cron above to avoid
        // overlap.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.LeaveEntitlementReconcileJob>(
            "leave-entitlement-reconcile",
            job => job.RunAsync(CancellationToken.None),
            "30 2 * * *"); // 02:30 UTC daily

        // US-LV-007 FR-3 / BR-5: Recurring-holiday next-year generation (1 Dec, ~30 days before
        // year-end). Idempotent, so a daily-or-later cadence in December is safe.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.HolidayRecurrenceJob>(
            "holiday-recurrence-generation",
            job => job.RunAsync(),
            "0 1 1 12 *"); // 01:00 UTC on 1 December

        // US-LV-008 FR-2 / AC-1: Year-end leave carry-forward + forfeiture.
        //
        // ISSUE-305: was "0 2 1 1 *" (1 January only). A tenant's leave year need not start in January — an
        // Apr-Mar tenant closes on 31 March — so a fixed 1-Jan cron could NEVER run their year-end, whatever
        // the job computed internally. It now runs DAILY and the job itself decides which tenants are in their
        // own year-end window (ProcessLeaveYearEndJob.ClosingLeaveYearIfDue). Safe because the per-pair work is
        // idempotent: on the ~358 days no tenant is due, the job selects nothing and exits.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.ProcessLeaveYearEndJob>(
            "leave-year-end-carry-forward",
            job => job.RunAsync(),
            "0 2 * * *"); // 02:00 UTC daily — the job filters to each tenant's own leave-year boundary

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

        // US-ATT-002 BR-5: auto-close attendance records left open past end-of-day (UTC). Safety net
        // for missed manual clock-outs; closes the prior day's open punches and flags them ANOMALY for
        // regularization. Idempotent, so a daily cadence shortly after midnight UTC is safe.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.AutoClockOutJob>(
            "attendance-auto-clock-out",
            job => job.RunAsync(),
            "5 0 * * *"); // 00:05 UTC daily

        // US-ATT-007 FR-1: daily refresh of the monthly attendance summary for the previous day's month
        // (recomputes the still-incomplete current month so the HR view stays fresh). 01:00 UTC daily.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.MonthlySummaryDailyJob>(
            "attendance-monthly-summary-daily",
            job => job.RunAsync(),
            "0 1 * * *"); // 01:00 UTC daily

        // US-ATT-007 FR-2: monthly finalize of the previous month's attendance summary, on the 1st.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.MonthlySummaryMonthlyJob>(
            "attendance-monthly-summary-finalize",
            job => job.RunAsync(),
            "30 1 1 * *"); // 01:30 UTC on the 1st of every month

        // US-ATT-010 FR-8: scheduled attendance-report generation. Runs hourly so configs with various
        // delivery times are picked up close to their scheduled hour; the job itself de-dupes per period
        // (DAILY/WEEKLY/MONTHLY) via LastRunAt. US-NTF-006 Phase 7: recipients are emailed a scheduled_report_ready
        // notification with the download locator once generated.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.ScheduledReportJob>(
            "attendance-scheduled-reports",
            job => job.RunAsync(),
            "0 * * * *"); // top of every hour, UTC

        // US-PRF-002 FR-7 / AC-5: daily self-assessment deadline reminders (default 7/3/1 days before the
        // self-assessment window closes). Idempotent per day + tenant-safe; dispatches via the log-only
        // performance notification seam until US-NTF lands a real in-app/email channel.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.SelfAssessmentReminderJob>(
            "performance-self-assessment-reminders",
            job => job.RunAsync(),
            "0 7 * * *"); // 07:00 UTC daily

        // US-PRF-005 AC-5 / FR-8: daily 360-degree reviewer reminders for assignments still Pending while
        // the cycle's feedback window is open. Idempotent per run + tenant-safe; dispatches via the same
        // log-only performance notification seam until US-NTF.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.Feedback360ReminderJob>(
            "performance-360-reviewer-reminders",
            job => job.RunAsync(),
            "0 8 * * *"); // 08:00 UTC daily

        // US-PRF-006 BR-3: daily auto-close of reviews the employee never signed within the cycle's
        // tenant-configurable window (default 7 days) → No Response + HR notified. Idempotent + tenant-safe;
        // dispatches via the same log-only performance notification seam until US-NTF.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.ReviewSignoffAutoCloseJob>(
            "performance-review-signoff-auto-close",
            job => job.RunAsync(),
            "0 9 * * *"); // 09:00 UTC daily

        // US-PRF-008 FR-3/BR-4: daily PIP sweep — checkpoint reminders (3 days before each), end-date
        // reminders, overdue-checkpoint alerts, and the "Not Acknowledged" flag for PIPs unacknowledged within
        // 5 business days. Idempotent + tenant-safe; dispatches via the log-only performance seam until US-NTF.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.PipReminderJob>(
            "performance-pip-reminders",
            job => job.RunAsync(),
            "0 10 * * *"); // 10:00 UTC daily

        // US-PRF-009 AC-5/FR-6/BR-4: daily stale-goal sweep — nudges employees whose active goals have gone
        // without a progress update beyond the tenant-configurable interval (Tenant.StaleGoalNudgeDays, 0 disables)
        // and flags those goals "Needs Attention" for the manager dashboard. Idempotent + tenant-safe; dispatches
        // via the log-only performance seam until US-NTF (real-time/SignalR deferred to US-NTF-001).
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.StaleGoalNudgeJob>(
            "performance-stale-goal-nudges",
            job => job.RunAsync(),
            "0 11 * * *"); // 11:00 UTC daily

        // US-ONB-003 FR-6/AC-5/BR-4: daily onboarding overdue-task sweep — detects past-due, not-completed
        // tasks across active tenants and writes overdue notification-outbox rows to employee + HR + manager,
        // then enqueues the dispatch worker. Idempotent per task per UTC day. NOTE: tenant-timezone scheduling
        // (BR-4 default 09:00 tenant-local) is NOT built — runs on a single UTC cron, compares UTC dates.
        // US-ADM-011b (AC-5/FR-8/NFR-3): SLA-escalation sweep — escalates overdue approval steps across active
        // tenants every 5 minutes. Idempotent (conditional CAS per breached step). Runs on a single UTC cron.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.WorkflowSlaEscalationJob>(
            "workflow-sla-escalation",
            job => job.RunAsync(CancellationToken.None),
            "*/5 * * * *"); // every 5 minutes

        // US-PAY-008 FR-3 (ISSUE-173): PAYROLL approval SLA-escalation sweep — escalates overdue payroll approvals
        // across active tenants every 5 minutes. Idempotent (conditional CAS per breached run). Single UTC cron.
        recurringJobs.AddOrUpdate<HRM.Api.Jobs.PayrollApprovalSlaEscalationJob>(
            "payroll-approval-sla-escalation",
            job => job.RunAsync(CancellationToken.None),
            "*/5 * * * *"); // every 5 minutes

        recurringJobs.AddOrUpdate<HRM.Api.Jobs.OnboardingOverdueSweepJob>(
            "onboarding-overdue-task-sweep",
            job => job.RunAsync(CancellationToken.None),
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

// ===== Rate-limit partition key =====
// Resolves the client IP for rate-limit partitioning. No ForwardedHeaders/UseForwardedHeaders middleware is
// configured in this app, so Connection.RemoteIpAddress is the direct socket peer (correct behind no proxy).
// If a reverse proxy is introduced later, add ForwardedHeaders middleware so this reflects the real client.
// Fall back to a single shared bucket when the IP is unavailable, so the limit still applies (fail-closed).
static string ResolveClientIp(HttpContext httpContext)
    => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

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

// Exposed so WebApplicationFactory<Program> (HRM.Tests integration HTTP harness) can boot this app.
public partial class Program { }

