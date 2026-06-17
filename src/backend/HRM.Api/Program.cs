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
    // US-ADM-003 (AC-5/AC-6/FR-3/FR-6): block writes under a read-only impersonation session and block the
    // FR-6 destructive ops even under a full impersonation. Runs before the handler (and before the tenant
    // transaction) so a forbidden write never touches the database.
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ImpersonationReadOnlyBehavior<,>));
    // US-PLT-002: ambient SET LOCAL app.current_tenant for RLS. Registered last so it is
    // the innermost behavior (its transaction spans the handler). Inert until Rls:Enabled
    // (Phase-4 switch-on) and a no-op on non-relational providers / system context.
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(HRM.Infrastructure.Behaviors.TenantTransactionBehavior<,>));

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

    var signalRRedis = builder.Configuration.GetConnectionString("Redis")
        ?? builder.Configuration["Redis:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(signalRRedis))
    {
        signalRBuilder.AddStackExchangeRedis(signalRRedis, options =>
        {
            options.Configuration.ChannelPrefix =
                StackExchange.Redis.RedisChannel.Literal(builder.Configuration["Redis:InstanceName"] ?? "hrm:signalr:");
        });
        Log.Information("SignalR Redis backplane enabled.");
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

    // US-ADM-002 FR-5: the Hangfire monitoring seam for the System Admin dashboard's cross-tenant job-queue
    // view. Lives here alongside Hangfire; bound to IJobQueueMonitor so the Infrastructure monitoring service
    // does not hard-depend on a running Hangfire server (it degrades to an Available=false snapshot).
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IJobQueueMonitor, HRM.Api.Monitoring.HangfireJobQueueMonitor>();

    // ===== Background Jobs =====
    builder.Services.AddScoped<HRM.Api.Jobs.TokenCleanupJob>();
    builder.Services.AddScoped<HRM.Api.Jobs.SendLockoutNotificationJob>();
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

    // US-LV-012 FR-5: large leave-report exports run as a Hangfire background job. Bound to the
    // ILeaveReportExportJob interface so the Infrastructure report service can enqueue it by interface.
    builder.Services.AddScoped<HRM.Api.Jobs.LeaveReportExportJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.ILeaveReportExportJob, HRM.Api.Jobs.LeaveReportExportJob>();

    // US-ONB-002 NFR-3: onboarding notification-outbox dispatch worker (drains pending intent rows and
    // delivers via INotificationDispatcher). Bound to the interface so the assignment service can enqueue
    // it by interface. The dispatcher is log-only until the Notifications module (US-NTF-001/002) lands.
    builder.Services.AddScoped<HRM.Api.Jobs.OnboardingNotificationDispatchJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IOnboardingNotificationDispatchJob, HRM.Api.Jobs.OnboardingNotificationDispatchJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.INotificationDispatcher, HRM.Api.Notifications.LoggingNotificationDispatcher>();

    // US-ONB-003 FR-6/AC-5/BR-4: daily overdue-task sweep (writes overdue outbox rows; tenant-tz deferred, UTC).
    builder.Services.AddScoped<HRM.Api.Jobs.OnboardingOverdueSweepJob>();

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

    // US-PAY-003 FR-2/FR-3: tenant-aware payroll-run processing job + the Hangfire-backed scheduler seam
    // (bound to IPayrollRunJobScheduler so the Infrastructure PayrollRunService can enqueue by interface).
    // The job is enqueued by InitiatePayrollRun and restores the tenant context before computing.
    builder.Services.AddScoped<HRM.Api.Jobs.ProcessPayrollRunJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IPayrollRunJobScheduler, HRM.Api.Jobs.HangfirePayrollRunJobScheduler>();

    // US-PAY-004 FR-4: tenant-aware payslip-PDF generation job + the Hangfire-backed scheduler seam (bound to
    // IPayslipGenerationJobScheduler so the Infrastructure PayslipGenerationService can enqueue by interface).
    // The job is enqueued by GeneratePayslips and restores the tenant context before rendering.
    builder.Services.AddScoped<HRM.Api.Jobs.GeneratePayslipsJob>();
    builder.Services.AddScoped<HRM.Application.Common.Interfaces.IPayslipGenerationJobScheduler, HRM.Api.Jobs.HangfirePayslipGenerationJobScheduler>();

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

    // Health check endpoint
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
        // (DAILY/WEEKLY/MONTHLY) via LastRunAt. Email delivery is DEFERRED (US-NTF) — generation only.
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

