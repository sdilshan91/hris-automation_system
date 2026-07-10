// ============================================================================
// US-ADM-011c (Parts 3/4/5/7) FR-11/FR-10/FR-12 — wiring Attendance-regularization, Overtime, and Offer
// through the approval-workflow runtime, plus the instance read APIs + inFlightCount.
//
//   • Attendance: submitting a regularization against an Active Attendance definition creates an instance;
//     approving via the runtime applies the attendance-log side-effect + Approved status. No definition →
//     legacy (WorkflowInstanceId null, AC-11).
//   • Overtime: submitting a pre-approval against an Active Overtime definition creates an instance;
//     approving → APPROVED + payroll-ready. Legacy fallback.
//   • Offer: generating against an Active Offer definition creates an instance; SendAsync is blocked 409
//     offer_approval_pending until the instance is Approved. Legacy → send freely.
//   • Read API: instance-detail returns the ordered step chain; the admin instance-list pages by lineage;
//     inFlightCount reflects live instances.
//
// HARNESS = real Postgres via Testcontainers (the runtime advance runs inside the Npgsql retry-safe strategy
// + FOR UPDATE row lock — InMemory cannot host it). Each fact uses its own tenant to stay isolated.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Workflows.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class WorkflowEntityWiringPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var tc = new MutableTenantContext { TenantId = Guid.NewGuid() };
        await using var db = Db(tc, User(Guid.NewGuid()));
        await db.Database.MigrateAsync();
    }

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

    private static ICurrentUser User(Guid userId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("u@acme.com");
        u.Permissions.Returns(new List<string>());
        return u;
    }

    private AppDbContext Db(ITenantContext tc, ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(user))
            .Options, tc);

    private WorkflowRuntimeService Runtime(AppDbContext db, ITenantContext tc, ICurrentUser user) =>
        new(db, tc, user, NullLogger<WorkflowRuntimeService>.Instance);

    /// <summary>Seeds a tenant + a requester employee reporting to a manager employee (both with User rows), and
    /// optionally an Active workflow definition of <paramref name="entityType"/> with a single NamedUser step whose
    /// approver is the manager. Returns the ids.</summary>
    private async Task<(Guid tenantId, Guid reqUser, Guid mgrUser, Guid reqEmp, Guid mgrEmp, Guid lineageId)>
        SeedTeamAsync(WorkflowEntityType? entityType)
    {
        var tenantId = Guid.NewGuid();
        var reqUser = Guid.NewGuid();
        var mgrUser = Guid.NewGuid();
        var tc = new MutableTenantContext { TenantId = tenantId };
        await using var db = Db(tc, User(reqUser));

        db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = "acme", Name = "Acme" });
        db.Set<User>().Add(new User { Id = reqUser, Email = "req@acme.com", IsActive = true });
        db.Set<User>().Add(new User { Id = mgrUser, Email = "mgr@acme.com", IsActive = true });

        var deptId = BaseEntity.NewUuidV7();
        var jobId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Ops", Code = "OPS", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = tenantId, TitleName = "Agent", IsActive = true });

        var mgrEmp = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = mgrEmp, TenantId = tenantId, UserId = mgrUser, EmployeeNo = "EMP-M",
            FirstName = "Mia", LastName = "Manager", Email = "mgr@acme.com",
            DateOfJoining = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DepartmentId = deptId, JobTitleId = jobId, EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true,
        });
        var reqEmp = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = reqEmp, TenantId = tenantId, UserId = reqUser, EmployeeNo = "EMP-R",
            FirstName = "Ray", LastName = "Requester", Email = "req@acme.com",
            DateOfJoining = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DepartmentId = deptId, JobTitleId = jobId, EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true, ReportsToEmployeeId = mgrEmp,
        });

        var lineageId = Guid.Empty;
        if (entityType is { } et)
        {
            var defId = BaseEntity.NewUuidV7();
            lineageId = defId;
            db.WorkflowDefinitions.Add(new WorkflowDefinition
            {
                Id = defId, TenantId = tenantId, Name = $"{et} Approval",
                EntityType = et, LineageId = defId, Version = 1,
                Status = WorkflowStatus.Active, IsActive = true,
            });
            db.WorkflowSteps.Add(new WorkflowStep
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, WorkflowDefinitionId = defId,
                StepOrder = 1, ApproverType = WorkflowApproverType.NamedUser, ApproverIdentifier = mgrUser,
                IsParallel = false, SlaHours = 24,
            });
        }

        await db.SaveChangesAsync();
        return (tenantId, reqUser, mgrUser, reqEmp, mgrEmp, lineageId);
    }

    private AttendanceService AttendanceSvc(AppDbContext db, ITenantContext tc, ICurrentUser cu) =>
        new(db, tc, cu,
            new OvertimeService(db, tc, cu, NullLogger<OvertimeService>.Instance),
            new ShiftService(db, tc, cu, NullLogger<ShiftService>.Instance),
            NullLogger<AttendanceService>.Instance,
            Runtime(db, tc, cu));

    private RegularizationApprovalService RegApprovalSvc(AppDbContext db, ITenantContext tc, ICurrentUser cu) =>
        new(db, tc, cu, new ShiftService(db, tc, cu, NullLogger<ShiftService>.Instance),
            NullLogger<RegularizationApprovalService>.Instance, Runtime(db, tc, cu));

    // ── Attendance wiring (Part 3) ─────────────────────────────────────────────

    [Fact]
    public async Task Attendance_ActiveDefinition_SubmitCreatesInstance_ApproveAppliesLog()
    {
        var (tenantId, reqUser, mgrUser, _, _, _) = await SeedTeamAsync(WorkflowEntityType.Attendance);
        var tc = new MutableTenantContext { TenantId = tenantId };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid regId;
        await using (var db = Db(tc, User(reqUser)))
        {
            var res = await AttendanceSvc(db, tc, User(reqUser)).SubmitRegularizationAsync(new SubmitRegularizationRequest
            {
                Date = today, RegularizationType = RegularizationType.MissedBoth,
                RequestedClockIn = "09:00", RequestedClockOut = "17:00", Reason = "Forgot to punch in and out.",
            });
            res.IsSuccess.Should().BeTrue(res.Error);
            regId = res.Value!.Id;
        }

        await using (var verify = Db(tc, User(reqUser)))
        {
            var reg = await verify.AttendanceRegularizations.AsNoTracking().FirstAsync(r => r.Id == regId);
            reg.WorkflowInstanceId.Should().NotBeNull("an Active Attendance definition instantiates the runtime");
        }

        // Manager approves through the runtime → attendance-log applied + Approved.
        await using (var db = Db(tc, User(mgrUser)))
        {
            var dec = await RegApprovalSvc(db, tc, User(mgrUser)).ApproveAsync(regId, "ok");
            dec.IsSuccess.Should().BeTrue(dec.Error);
            dec.Value!.Status.Should().Be(RegularizationStatus.Approved);
        }

        await using var final = Db(tc, User(reqUser));
        var approved = await final.AttendanceRegularizations.AsNoTracking().FirstAsync(r => r.Id == regId);
        approved.Status.Should().Be(RegularizationStatus.Approved);
        approved.AttendanceLogId.Should().NotBeNull();
        (await final.AttendanceLogs.AsNoTracking().CountAsync(a => a.Id == approved.AttendanceLogId)).Should().Be(1);
    }

    [Fact]
    public async Task Attendance_NoDefinition_LegacyPath_WorkflowInstanceNull()
    {
        var (tenantId, reqUser, _, _, _, _) = await SeedTeamAsync(entityType: null);
        var tc = new MutableTenantContext { TenantId = tenantId };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid regId;
        await using (var db = Db(tc, User(reqUser)))
        {
            var res = await AttendanceSvc(db, tc, User(reqUser)).SubmitRegularizationAsync(new SubmitRegularizationRequest
            {
                Date = today, RegularizationType = RegularizationType.MissedBoth,
                RequestedClockIn = "09:00", RequestedClockOut = "17:00", Reason = "Legacy path, no workflow.",
            });
            res.IsSuccess.Should().BeTrue(res.Error);
            regId = res.Value!.Id;
        }

        await using var verify = Db(tc, User(reqUser));
        (await verify.AttendanceRegularizations.AsNoTracking().FirstAsync(r => r.Id == regId))
            .WorkflowInstanceId.Should().BeNull();
    }

    // ── Overtime wiring (Part 4) ───────────────────────────────────────────────

    [Fact]
    public async Task Overtime_ActiveDefinition_SubmitCreatesInstance_ApproveMarksPayrollReady()
    {
        var (tenantId, reqUser, mgrUser, _, _, _) = await SeedTeamAsync(WorkflowEntityType.Overtime);
        var tc = new MutableTenantContext { TenantId = tenantId };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid recordId;
        await using (var db = Db(tc, User(reqUser)))
        {
            var res = await new OvertimeService(db, tc, User(reqUser), NullLogger<OvertimeService>.Instance, Runtime(db, tc, User(reqUser)))
                .SubmitPreApprovalAsync(new OvertimePreApprovalRequest
                {
                    Date = today, ExpectedHours = 2m, Reason = "Finishing the sprint deliverables.",
                });
            res.IsSuccess.Should().BeTrue(res.Error);
            recordId = res.Value!.Id;
        }

        await using (var verify = Db(tc, User(reqUser)))
            (await verify.OvertimeRecords.AsNoTracking().FirstAsync(o => o.Id == recordId))
                .WorkflowInstanceId.Should().NotBeNull();

        await using (var db = Db(tc, User(mgrUser)))
        {
            var dec = await new OvertimeService(db, tc, User(mgrUser), NullLogger<OvertimeService>.Instance, Runtime(db, tc, User(mgrUser)))
                .ApproveAsync(recordId, null, "approved");
            dec.IsSuccess.Should().BeTrue(dec.Error);
            dec.Value!.Status.Should().Be(OvertimeStatus.Approved);
        }

        await using var final = Db(tc, User(reqUser));
        var rec = await final.OvertimeRecords.AsNoTracking().FirstAsync(o => o.Id == recordId);
        rec.Status.Should().Be(OvertimeStatus.Approved);
        rec.IsPayrollReady.Should().BeTrue();
    }

    [Fact]
    public async Task Overtime_NoDefinition_LegacyPath_WorkflowInstanceNull()
    {
        var (tenantId, reqUser, _, _, _, _) = await SeedTeamAsync(entityType: null);
        var tc = new MutableTenantContext { TenantId = tenantId };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid recordId;
        await using (var db = Db(tc, User(reqUser)))
        {
            var res = await new OvertimeService(db, tc, User(reqUser), NullLogger<OvertimeService>.Instance, Runtime(db, tc, User(reqUser)))
                .SubmitPreApprovalAsync(new OvertimePreApprovalRequest
                {
                    Date = today, ExpectedHours = 2m, Reason = "Legacy overtime, no workflow.",
                });
            res.IsSuccess.Should().BeTrue(res.Error);
            recordId = res.Value!.Id;
        }

        await using var verify = Db(tc, User(reqUser));
        (await verify.OvertimeRecords.AsNoTracking().FirstAsync(o => o.Id == recordId))
            .WorkflowInstanceId.Should().BeNull();
    }

    // ── Offer gate (Part 5) ────────────────────────────────────────────────────

    private async Task<(Guid tenantId, Guid apprUser, Guid applicantId)> SeedOfferAsync(bool withDefinition)
    {
        var tenantId = Guid.NewGuid();
        var apprUser = Guid.NewGuid();
        var tc = new MutableTenantContext { TenantId = tenantId };
        await using var db = Db(tc, User(apprUser));

        db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = "acme", Name = "Acme" });
        db.Set<User>().Add(new User { Id = apprUser, Email = "appr@acme.com", IsActive = true });

        var vacancyId = BaseEntity.NewUuidV7();
        db.Vacancies.Add(new Vacancy
        {
            Id = vacancyId, TenantId = tenantId, ReferenceNumber = "VAC-2026-0001",
            Title = "Backend Engineer", Status = VacancyStatus.Open, EmploymentType = EmploymentType.FullTime,
            Headcount = 1, FilledCount = 0, Description = "Build things.", IsDeleted = false,
        });
        var applicantId = BaseEntity.NewUuidV7();
        db.Applicants.Add(new Applicant
        {
            Id = applicantId, TenantId = tenantId, VacancyId = vacancyId,
            ApplicationReferenceNumber = "APP-2026-0001", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Phone = "+15551234567",
            ResumeStorageKey = $"recruitment/{vacancyId}/x/z.pdf", ResumeFileName = "resume.pdf",
            Stage = ApplicantStage.Offer, Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow, IsDeleted = false,
        });

        if (withDefinition)
        {
            var defId = BaseEntity.NewUuidV7();
            db.WorkflowDefinitions.Add(new WorkflowDefinition
            {
                Id = defId, TenantId = tenantId, Name = "Offer Approval",
                EntityType = WorkflowEntityType.Offer, LineageId = defId, Version = 1,
                Status = WorkflowStatus.Active, IsActive = true,
            });
            db.WorkflowSteps.Add(new WorkflowStep
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, WorkflowDefinitionId = defId,
                StepOrder = 1, ApproverType = WorkflowApproverType.NamedUser, ApproverIdentifier = apprUser,
                IsParallel = false, SlaHours = 24,
            });
        }

        await db.SaveChangesAsync();
        return (tenantId, apprUser, applicantId);
    }

    private OfferService OfferSvc(AppDbContext db, ITenantContext tc, ICurrentUser cu) =>
        new(db, tc, Substitute.For<IFileStorage>(), Substitute.For<IRecruitmentNotificationService>(),
            NullLogger<OfferService>.Instance, currentUser: cu, workflowRuntime: Runtime(db, tc, cu));

    private static GenerateOfferInput OfferInput(Guid applicantId) => new()
    {
        ApplicantId = applicantId, OfferedPosition = "Backend Engineer", SalaryAmount = 120000m,
        Currency = "USD", SalaryFrequency = SalaryFrequency.Annual,
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
    };

    [Fact]
    public async Task Offer_ActiveDefinition_SendBlockedUntilApproved()
    {
        var (tenantId, apprUser, applicantId) = await SeedOfferAsync(withDefinition: true);
        var tc = new MutableTenantContext { TenantId = tenantId };

        Guid offerId;
        await using (var db = Db(tc, User(apprUser)))
        {
            var gen = await OfferSvc(db, tc, User(apprUser)).GenerateAsync(OfferInput(applicantId));
            gen.IsSuccess.Should().BeTrue(gen.Error);
            offerId = gen.Value!.Id;
        }

        await using (var verify = Db(tc, User(apprUser)))
            (await verify.Offers.AsNoTracking().FirstAsync(o => o.Id == offerId))
                .WorkflowInstanceId.Should().NotBeNull();

        // Send is blocked while the instance is InProgress.
        await using (var db = Db(tc, User(apprUser)))
        {
            var send = await OfferSvc(db, tc, User(apprUser)).SendAsync(offerId);
            send.IsFailure.Should().BeTrue();
            send.StatusCode.Should().Be(409);
            send.ErrorCode.Should().Be("offer_approval_pending");
        }

        // The approver approves the instance.
        await using (var db = Db(tc, User(apprUser)))
        {
            var dec = await OfferSvc(db, tc, User(apprUser))
                .DecideApprovalAsync(offerId, WorkflowDecisionAction.Approve, null);
            dec.IsSuccess.Should().BeTrue(dec.Error);
            dec.Value!.Status.Should().Be("Approved");
        }

        // Now Send succeeds.
        await using (var db = Db(tc, User(apprUser)))
        {
            var send = await OfferSvc(db, tc, User(apprUser)).SendAsync(offerId);
            send.IsSuccess.Should().BeTrue(send.Error);
            send.Value!.Status.Should().Be(OfferStatus.Sent);
        }
    }

    [Fact]
    public async Task Offer_NoDefinition_SendsFreely()
    {
        var (tenantId, apprUser, applicantId) = await SeedOfferAsync(withDefinition: false);
        var tc = new MutableTenantContext { TenantId = tenantId };

        Guid offerId;
        await using (var db = Db(tc, User(apprUser)))
        {
            var gen = await OfferSvc(db, tc, User(apprUser)).GenerateAsync(OfferInput(applicantId));
            gen.IsSuccess.Should().BeTrue(gen.Error);
            offerId = gen.Value!.Id;
            gen.Value!.Status.Should().Be(OfferStatus.Draft);
        }

        await using (var verify = Db(tc, User(apprUser)))
            (await verify.Offers.AsNoTracking().FirstAsync(o => o.Id == offerId))
                .WorkflowInstanceId.Should().BeNull();

        await using (var db = Db(tc, User(apprUser)))
        {
            var send = await OfferSvc(db, tc, User(apprUser)).SendAsync(offerId);
            send.IsSuccess.Should().BeTrue(send.Error);
            send.Value!.Status.Should().Be(OfferStatus.Sent);
        }
    }

    // ── Read API + inFlightCount (Part 7) ──────────────────────────────────────

    [Fact]
    public async Task ReadApi_InstanceDetail_List_And_InFlightCount()
    {
        var (tenantId, _, apprUser, _, _, lineageId) = await SeedTeamAsync(WorkflowEntityType.Leave);
        var tc = new MutableTenantContext { TenantId = tenantId };

        var entity1 = Guid.NewGuid();
        var entity2 = Guid.NewGuid();

        // Two instances; approve the first to completion, leave the second in flight.
        await using (var db = Db(tc, User(apprUser)))
            await Runtime(db, tc, User(apprUser)).CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Leave, entity1, null, new Dictionary<string, object?> { ["days_requested"] = 2m });
        await using (var db = Db(tc, User(apprUser)))
            await Runtime(db, tc, User(apprUser)).CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Leave, entity2, null, new Dictionary<string, object?> { ["days_requested"] = 2m });

        await using (var db = Db(tc, User(apprUser)))
        {
            var dec = await Runtime(db, tc, User(apprUser))
                .DecideAsync(WorkflowEntityType.Leave, entity1, WorkflowDecisionAction.Approve, "ok");
            dec.IsSuccess.Should().BeTrue(dec.Error);
            dec.Value!.Outcome.Should().Be(WorkflowDecisionOutcome.InstanceApproved);
        }

        await using var db2 = Db(tc, User(apprUser));
        var svc = new WorkflowService(db2, tc, User(apprUser), NullLogger<WorkflowService>.Instance);

        var inst2 = await db2.WorkflowInstances.AsNoTracking().FirstAsync(i => i.EntityId == entity2);

        // Instance detail with ordered step chain.
        var detail = await svc.GetInstanceAsync(inst2.Id);
        detail.IsSuccess.Should().BeTrue(detail.Error);
        detail.Value!.Status.Should().Be(WorkflowInstanceStatus.InProgress.ToString());
        detail.Value!.Steps.Should().NotBeEmpty();
        detail.Value!.Steps.Should().BeInAscendingOrder(s => s.StepOrder);

        // Admin instance list for the lineage: both instances present.
        var list = await svc.ListInstancesAsync(lineageId, 1, 20);
        list.IsSuccess.Should().BeTrue(list.Error);
        list.Value!.TotalCount.Should().Be(2);

        // inFlightCount on the definition list = 1 (the still-InProgress instance).
        var defs = await svc.ListAsync();
        defs.IsSuccess.Should().BeTrue(defs.Error);
        defs.Value!.First(w => w.LineageId == lineageId).InFlightCount.Should().Be(1);
    }
}
