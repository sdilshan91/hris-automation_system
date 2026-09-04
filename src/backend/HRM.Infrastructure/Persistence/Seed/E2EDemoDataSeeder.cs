using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Persistence.Seed;

/// <summary>
/// DEV/TEST-ONLY demo data for the <c>e2e</c> tenant: an org chart, an IN-PROGRESS appraisal cycle with
/// phases + participants, a 360-feedback round (submitted AND pending), and two offboarding instances.
///
/// <para><b>Why this exists.</b> The E2E tenant used to be seeded with exactly ONE employee, ZERO appraisal
/// cycles and ZERO offboarding instances. That is enough to log in and enough for employee-self-service, but
/// it is not enough to <i>look at</i> the Performance or Offboarding modules: a list screen, an org tree, a
/// manager's "my team" view, the 360 results screen and the clearance checklist all render their empty state,
/// so a reviewer cannot distinguish "feature works, no data" from "feature is broken". A QA iteration was
/// blocked on exactly that. The fix is data, not code, so it lives here rather than in a migration.</para>
///
/// <para><b>Environment gate.</b> This class is reachable ONLY from
/// <c>DbInitializer.SeedE2EDevTenantAsync</c>, which <c>RunAsync</c> calls behind
/// <see cref="Microsoft.Extensions.Hosting.IHostEnvironment.IsDevelopment"/>. There is no other call site —
/// do not add one. Nothing here may ever appear in Staging or Production.</para>
///
/// <para><b>Idempotency.</b> The seeder runs on EVERY app start, so every block below independently reads the
/// rows it owns and inserts only what is missing, keyed on a natural key that matches the table's unique
/// index (employee_no, cycle name, (cycle, phase), (cycle, employee), (cycle, reviewee, reviewer),
/// (employee) for offboarding). There is deliberately NO single outer "already seeded?" gate: one would make
/// a partially-seeded database — the normal result of a crash mid-seed, or of adding a new block to this file
/// against an existing dev DB — permanently unrepairable.</para>
///
/// <para><b>Tenant scope.</b> Every row is stamped with the caller's <paramref name="tenantId"/> explicitly.
/// <c>TenantInterceptor</c> only fills a <see cref="System.Guid.Empty"/> tenant id and never overwrites one,
/// so these values survive to the database as written; reads use <c>IgnoreQueryFilters()</c> because the
/// seeder runs with no ambient tenant context.</para>
/// </summary>
internal static class E2EDemoDataSeeder
{
    /// <summary>Employee number of the owner employee <c>DbInitializer</c> seeds; the org chart's root.</summary>
    internal const string OwnerEmployeeNo = "E2E-0001";

    /// <summary>Name of the demo appraisal cycle — the natural key its idempotency guard reads.</summary>
    internal const string DemoCycleName = "E2E FY26 Annual Review";

    /// <summary>Template name stamped on both demo offboarding instances (no template row is required).</summary>
    internal const string DemoOffboardingTemplateName = "E2E Standard Exit Checklist";

    private const string DeptOps = "OPS";
    private const string DeptEngineering = "ENG";
    private const string DeptPeople = "PPL";

    private const string TitleAdministrator = "Administrator";
    private const string TitleEngineeringManager = "Engineering Manager";
    private const string TitleSoftwareEngineer = "Software Engineer";
    private const string TitleOperationsSpecialist = "Operations Specialist";
    private const string TitleHrSpecialist = "HR Specialist";

    /// <summary>The employee who is mid-notice-period — carries the IN-PROGRESS offboarding instance.</summary>
    private const string ResigningEmployeeNo = "E2E-0009";

    /// <summary>The terminated employee — carries the COMPLETED offboarding instance.</summary>
    private const string TerminatedEmployeeNo = "E2E-0010";

    /// <summary>The 360 reviewee whose feedback is fully SUBMITTED (drives the results/composite screens).</summary>
    private const string Reviewee360SubmittedNo = "E2E-0002";

    /// <summary>The 360 reviewee whose reviewers are still PENDING (drives the "give feedback" screens).</summary>
    private const string Reviewee360PendingNo = "E2E-0003";

    private sealed record DemoDepartment(string Code, string Name);

    private sealed record DemoEmployee(
        string EmployeeNo,
        string FirstName,
        string LastName,
        string DepartmentCode,
        string JobTitle,
        string? ManagerEmployeeNo,
        EmployeeStatus Status,
        EmploymentType EmploymentType,
        decimal Fte,
        Gender Gender,
        int BirthYear,
        int BirthMonth,
        int BirthDay,
        int JoinedDaysAgo,
        bool IsActive);

    private static readonly DemoDepartment[] Departments =
    [
        new(DeptOps, "Operations"),
        new(DeptEngineering, "Engineering"),
        new(DeptPeople, "People Operations"),
    ];

    private static readonly string[] JobTitles =
    [
        TitleAdministrator,
        TitleEngineeringManager,
        TitleSoftwareEngineer,
        TitleOperationsSpecialist,
        TitleHrSpecialist,
    ];

    /// <summary>
    /// The demo org chart. Surnames are deliberately fictional-company names (Northwind/Fabrikam/Contoso/…)
    /// so no row can be mistaken for a real person's record. E2E-0001 is NOT listed: it is seeded by
    /// <c>DbInitializer</c> (it must be linked to the owner user) and is passed in as the tree root.
    /// </summary>
    private static readonly DemoEmployee[] Employees =
    [
        // Engineering manager — reports to the owner, and has five direct reports of their own so a
        // manager's "my team" view and the org tree both render a non-trivial subtree.
        new("E2E-0002", "Maya", "Northwind", DeptEngineering, TitleEngineeringManager, OwnerEmployeeNo,
            EmployeeStatus.Active, EmploymentType.FullTime, 1.00m, Gender.Female, 1986, 3, 14, 1500, true),

        new("E2E-0003", "Arun", "Fabrikam", DeptEngineering, TitleSoftwareEngineer, "E2E-0002",
            EmployeeStatus.Active, EmploymentType.FullTime, 1.00m, Gender.Male, 1992, 7, 2, 900, true),
        new("E2E-0004", "Lena", "Contoso", DeptEngineering, TitleSoftwareEngineer, "E2E-0002",
            EmployeeStatus.Active, EmploymentType.FullTime, 1.00m, Gender.Female, 1994, 11, 23, 640, true),
        new("E2E-0005", "Diego", "Litware", DeptEngineering, TitleSoftwareEngineer, "E2E-0002",
            EmployeeStatus.Probation, EmploymentType.FullTime, 1.00m, Gender.Male, 1998, 1, 9, 45, true),
        // Part-time so FTE-sensitive screens (leave proration, FTE-scaled overtime) show a non-1.00 case.
        new("E2E-0006", "Priya", "Tailspin", DeptEngineering, TitleSoftwareEngineer, "E2E-0002",
            EmployeeStatus.Active, EmploymentType.PartTime, 0.50m, Gender.Female, 1990, 5, 30, 1100, true),

        new("E2E-0007", "Noor", "Adventure", DeptOps, TitleOperationsSpecialist, OwnerEmployeeNo,
            EmployeeStatus.Active, EmploymentType.FullTime, 1.00m, Gender.NonBinary, 1991, 9, 17, 1250, true),
        new("E2E-0008", "Kai", "Wingtip", DeptPeople, TitleHrSpecialist, OwnerEmployeeNo,
            EmployeeStatus.Active, EmploymentType.FullTime, 1.00m, Gender.PreferNotToSay, 1989, 12, 5, 1800, true),

        // Mid-notice-period: still Active (working out notice) with an IN-PROGRESS offboarding, which is the
        // state the clearance checklist is actually meant to be operated in.
        new(ResigningEmployeeNo, "Sam", "Proseware", DeptEngineering, TitleSoftwareEngineer, "E2E-0002",
            EmployeeStatus.Active, EmploymentType.FullTime, 1.00m, Gender.Male, 1993, 6, 21, 780, true),

        // Fully exited: Terminated + inactive, with a COMPLETED offboarding, so the offboarding list has
        // both an open and a closed row and the employee list has a non-Active member.
        new(TerminatedEmployeeNo, "Iris", "Woodgrove", DeptOps, TitleOperationsSpecialist, OwnerEmployeeNo,
            EmployeeStatus.Terminated, EmploymentType.Contract, 1.00m, Gender.Female, 1987, 2, 11, 1400, false),
    ];

    /// <summary>
    /// Seeds (idempotently) the demo org chart, appraisal cycle, 360 round and offboarding instances for the
    /// DEV <c>e2e</c> tenant.
    /// </summary>
    /// <param name="db">The application DbContext. Reads use <c>IgnoreQueryFilters()</c> — no ambient tenant.</param>
    /// <param name="tenantId">The <c>e2e</c> tenant id. Stamped on every row this method writes.</param>
    /// <param name="ownerEmployeeId">Id of the <see cref="OwnerEmployeeNo"/> employee — the org tree's root.</param>
    internal static async Task SeedAsync(
        AppDbContext db,
        Guid tenantId,
        Guid ownerEmployeeId,
        ILogger logger,
        CancellationToken ct)
    {
        // One clock reading for the whole run so the cycle window, the 360 timestamps and the offboarding
        // dates are mutually consistent instead of drifting across a slow seed.
        var now = DateTime.UtcNow;

        var departmentIds = await EnsureDepartmentsAsync(db, tenantId, ct);
        var jobTitleIds = await EnsureJobTitlesAsync(db, tenantId, ct);
        var employeeIds = await EnsureEmployeesAsync(
            db, tenantId, ownerEmployeeId, departmentIds, jobTitleIds, now, logger, ct);

        var cycleId = await EnsureAppraisalCycleAsync(db, tenantId, now, logger, ct);
        await EnsureCyclePhasesAsync(db, tenantId, cycleId, now, ct);
        await EnsureCycleParticipantsAsync(db, tenantId, cycleId, ownerEmployeeId, employeeIds, ct);
        await Ensure360FeedbackAsync(db, tenantId, cycleId, ownerEmployeeId, employeeIds, now, logger, ct);
        await EnsureOffboardingAsync(db, tenantId, employeeIds, now, logger, ct);
    }

    // ── Departments ────────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, Guid>> EnsureDepartmentsAsync(
        AppDbContext db, Guid tenantId, CancellationToken ct)
    {
        var codes = Departments.Select(d => d.Code).ToList();

        var existing = await db.Departments
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && codes.Contains(d.Code))
            .Select(d => new { d.Code, d.Id })
            .ToListAsync(ct);

        var map = existing.ToDictionary(x => x.Code, x => x.Id, StringComparer.Ordinal);

        foreach (var dept in Departments.Where(d => !map.ContainsKey(d.Code)))
        {
            var id = BaseEntity.NewUuidV7();
            db.Departments.Add(new Department
            {
                Id = id,
                TenantId = tenantId,
                Name = dept.Name,
                Code = dept.Code,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            map[dept.Code] = id;
        }

        await db.SaveChangesAsync(ct);
        return map;
    }

    // ── Job titles ─────────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, Guid>> EnsureJobTitlesAsync(
        AppDbContext db, Guid tenantId, CancellationToken ct)
    {
        var names = JobTitles.ToList();

        var existing = await db.JobTitles
            .IgnoreQueryFilters()
            .Where(j => j.TenantId == tenantId && names.Contains(j.TitleName))
            .Select(j => new { j.TitleName, j.Id })
            .ToListAsync(ct);

        var map = existing.ToDictionary(x => x.TitleName, x => x.Id, StringComparer.Ordinal);

        foreach (var title in JobTitles.Where(t => !map.ContainsKey(t)))
        {
            var id = BaseEntity.NewUuidV7();
            db.JobTitles.Add(new JobTitle
            {
                Id = id,
                TenantId = tenantId,
                TitleName = title,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            map[title] = id;
        }

        await db.SaveChangesAsync(ct);
        return map;
    }

    // ── Employees + reporting lines ────────────────────────────────────────────

    /// <summary>
    /// Inserts any missing demo employee and returns employee_no → id for the WHOLE demo org (including
    /// <see cref="OwnerEmployeeNo"/>), so the later blocks can reference employees by their stable number
    /// whether this run created them or a previous run did.
    /// </summary>
    private static async Task<Dictionary<string, Guid>> EnsureEmployeesAsync(
        AppDbContext db,
        Guid tenantId,
        Guid ownerEmployeeId,
        IReadOnlyDictionary<string, Guid> departmentIds,
        IReadOnlyDictionary<string, Guid> jobTitleIds,
        DateTime now,
        ILogger logger,
        CancellationToken ct)
    {
        var employeeNos = Employees.Select(e => e.EmployeeNo).ToList();

        var existing = await db.Employees
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && employeeNos.Contains(e.EmployeeNo))
            .Select(e => new { e.EmployeeNo, e.Id })
            .ToListAsync(ct);

        var map = existing.ToDictionary(x => x.EmployeeNo, x => x.Id, StringComparer.Ordinal);
        map[OwnerEmployeeNo] = ownerEmployeeId;

        // Pre-allocate ids for the missing rows FIRST, so a report inserted in the same pass as its manager
        // can point at that manager without a second SaveChanges round-trip.
        var missing = Employees.Where(e => !map.ContainsKey(e.EmployeeNo)).ToList();
        foreach (var spec in missing)
            map[spec.EmployeeNo] = BaseEntity.NewUuidV7();

        foreach (var spec in missing)
        {
            db.Employees.Add(new Employee
            {
                Id = map[spec.EmployeeNo],
                TenantId = tenantId,
                EmployeeNo = spec.EmployeeNo,
                FirstName = spec.FirstName,
                LastName = spec.LastName,
                Email = $"{spec.FirstName}.{spec.LastName}@e2e.test".ToLowerInvariant(),
                DateOfBirth = DateTime.SpecifyKind(
                    new DateTime(spec.BirthYear, spec.BirthMonth, spec.BirthDay), DateTimeKind.Utc),
                Gender = spec.Gender,
                DateOfJoining = now.AddDays(-spec.JoinedDaysAgo).Date,
                DepartmentId = departmentIds[spec.DepartmentCode],
                JobTitleId = jobTitleIds[spec.JobTitle],
                EmploymentType = spec.EmploymentType,
                Status = spec.Status,
                Fte = spec.Fte,
                ReportsToEmployeeId = spec.ManagerEmployeeNo is null ? null : map[spec.ManagerEmployeeNo],
                IsActive = spec.IsActive,
                CreatedAt = DateTime.UtcNow,
            });
        }

        if (missing.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Seeded {Count} DEV E2E demo employees for tenant {TenantId}", missing.Count, tenantId);
        }

        return map;
    }

    // ── Appraisal cycle ────────────────────────────────────────────────────────

    /// <summary>
    /// The cycle window is anchored to the seed instant so the cycle is <b>in progress</b> the moment the
    /// dev stack comes up: self-assessment is open NOW, goal-setting has closed, manager review is still
    /// ahead. A cycle whose windows had all elapsed would render as inertly as no cycle at all.
    /// </summary>
    private static async Task<Guid> EnsureAppraisalCycleAsync(
        AppDbContext db, Guid tenantId, DateTime now, ILogger logger, CancellationToken ct)
    {
        var existing = await db.AppraisalCycles
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Name == DemoCycleName)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct);

        if (existing != Guid.Empty)
            return existing;

        var cycle = new AppraisalCycle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            Name = DemoCycleName,
            Type = CycleType.Annual,
            Status = AppraisalCycleStatus.Active,
            StartDate = now.AddDays(-60),
            EndDate = now.AddDays(60),
            GoalSettingStart = now.AddDays(-60),
            GoalSettingEnd = now.AddDays(-31),
            SelfAssessmentStart = now.AddDays(-30),
            SelfAssessmentEnd = now.AddDays(14),
            ManagerReviewStart = now.AddDays(15),
            ManagerReviewEnd = now.AddDays(35),
            RatingScaleMax = 5,
            SelfWeightPercent = 30,
            Is360Enabled = true,
            IsCalibrationEnabled = true,
            IsAnonymousFeedback = false,
            ParticipantScope = ParticipantScopeType.AllEmployees,
            CreatedAt = DateTime.UtcNow,
        };

        db.AppraisalCycles.Add(cycle);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded DEV E2E appraisal cycle {Cycle} for tenant {TenantId}",
            DemoCycleName, tenantId);

        return cycle.Id;
    }

    private static async Task EnsureCyclePhasesAsync(
        AppDbContext db, Guid tenantId, Guid cycleId, DateTime now, CancellationToken ct)
    {
        (CyclePhaseType Type, int Sequence, DateTime Start, DateTime End)[] phases =
        [
            (CyclePhaseType.GoalSetting, 1, now.AddDays(-60), now.AddDays(-31)),
            (CyclePhaseType.SelfAssessment, 2, now.AddDays(-30), now.AddDays(14)),
            (CyclePhaseType.ManagerReview, 3, now.AddDays(15), now.AddDays(35)),
            (CyclePhaseType.Calibration, 4, now.AddDays(36), now.AddDays(45)),
            (CyclePhaseType.Publish, 5, now.AddDays(46), now.AddDays(60)),
        ];

        var existing = await db.CyclePhases
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.CycleId == cycleId)
            .Select(p => p.PhaseType)
            .ToListAsync(ct);

        var added = false;
        foreach (var phase in phases.Where(p => !existing.Contains(p.Type)))
        {
            db.CyclePhases.Add(new CyclePhase
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                CycleId = cycleId,
                PhaseType = phase.Type,
                Sequence = phase.Sequence,
                StartDate = phase.Start,
                EndDate = phase.End,
                CreatedAt = DateTime.UtcNow,
            });
            added = true;
        }

        if (added)
            await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Enrols the whole demo org EXCEPT the terminated employee. A cycle with zero participants renders
    /// identically to no cycle at all, which is the failure mode this seeder exists to remove.
    /// </summary>
    private static async Task EnsureCycleParticipantsAsync(
        AppDbContext db,
        Guid tenantId,
        Guid cycleId,
        Guid ownerEmployeeId,
        IReadOnlyDictionary<string, Guid> employeeIds,
        CancellationToken ct)
    {
        var participantIds = new List<Guid> { ownerEmployeeId };
        participantIds.AddRange(Employees
            .Where(e => e.EmployeeNo != TerminatedEmployeeNo)
            .Select(e => employeeIds[e.EmployeeNo]));

        var existing = await db.CycleParticipants
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.CycleId == cycleId)
            .Select(p => p.EmployeeId)
            .ToListAsync(ct);

        var added = false;
        foreach (var employeeId in participantIds.Distinct().Where(id => !existing.Contains(id)))
        {
            db.CycleParticipants.Add(new CycleParticipant
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                CycleId = cycleId,
                EmployeeId = employeeId,
                CreatedAt = DateTime.UtcNow,
            });
            added = true;
        }

        if (added)
            await db.SaveChangesAsync(ct);
    }

    // ── 360 feedback ───────────────────────────────────────────────────────────

    private static readonly (string Key, int Rating, string Comment)[] SubmittedCompetencies =
    [
        ("communication", 4, "Clear written updates; keeps stakeholders ahead of surprises."),
        ("collaboration", 5, "Actively unblocks other squads before being asked."),
        ("delivery", 4, "Consistently lands committed scope within the sprint."),
    ];

    /// <summary>
    /// Seeds TWO shapes of 360 round on purpose:
    /// <list type="bullet">
    ///   <item><b>E2E-0002</b> — every reviewer Completed, each with a submitted <c>Feedback360</c> and items,
    ///     across all four <see cref="ReviewerCategory"/> values. This is what makes the results / composite-score
    ///     screens show real numbers instead of an empty state.</item>
    ///   <item><b>E2E-0003</b> — reviewers still Pending with no submissions, so the "feedback requested of me"
    ///     and reviewer-progress screens are also exercisable.</item>
    /// </list>
    /// Guarded on (cycle, reviewee, reviewer), matching the unique index on both tables.
    /// </summary>
    private static async Task Ensure360FeedbackAsync(
        AppDbContext db,
        Guid tenantId,
        Guid cycleId,
        Guid ownerEmployeeId,
        IReadOnlyDictionary<string, Guid> employeeIds,
        DateTime now,
        ILogger logger,
        CancellationToken ct)
    {
        var submittedReviewee = employeeIds[Reviewee360SubmittedNo];
        var pendingReviewee = employeeIds[Reviewee360PendingNo];

        // (reviewee, reviewer, category, isSubmitted)
        (Guid Reviewee, Guid Reviewer, ReviewerCategory Category, bool Submitted)[] plan =
        [
            // E2E-0002, fully submitted: self + manager + two peers + two direct reports.
            (submittedReviewee, submittedReviewee, ReviewerCategory.Self, true),
            (submittedReviewee, ownerEmployeeId, ReviewerCategory.Manager, true),
            (submittedReviewee, employeeIds["E2E-0007"], ReviewerCategory.Peer, true),
            (submittedReviewee, employeeIds["E2E-0008"], ReviewerCategory.Peer, true),
            (submittedReviewee, employeeIds["E2E-0003"], ReviewerCategory.DirectReport, true),
            (submittedReviewee, employeeIds["E2E-0004"], ReviewerCategory.DirectReport, true),

            // E2E-0003, still awaiting everyone.
            (pendingReviewee, pendingReviewee, ReviewerCategory.Self, false),
            (pendingReviewee, employeeIds["E2E-0002"], ReviewerCategory.Manager, false),
            (pendingReviewee, employeeIds["E2E-0004"], ReviewerCategory.Peer, false),
            (pendingReviewee, employeeIds["E2E-0005"], ReviewerCategory.Peer, false),
        ];

        var revieweeIds = new[] { submittedReviewee, pendingReviewee };

        var existingAssignments = (await db.ReviewerAssignments
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId
                && a.CycleId == cycleId
                && revieweeIds.Contains(a.RevieweeEmployeeId))
            .Select(a => new { a.RevieweeEmployeeId, a.ReviewerEmployeeId })
            .ToListAsync(ct))
            .Select(a => (a.RevieweeEmployeeId, a.ReviewerEmployeeId))
            .ToHashSet();

        var submittedAt = now.AddDays(-3);
        var assignmentsAdded = 0;

        foreach (var entry in plan)
        {
            if (existingAssignments.Contains((entry.Reviewee, entry.Reviewer)))
                continue;

            db.ReviewerAssignments.Add(new ReviewerAssignment
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                CycleId = cycleId,
                RevieweeEmployeeId = entry.Reviewee,
                ReviewerEmployeeId = entry.Reviewer,
                Category = entry.Category,
                Status = entry.Submitted ? ReviewerAssignmentStatus.Completed : ReviewerAssignmentStatus.Pending,
                NotifiedAt = now.AddDays(-10),
                CompletedAt = entry.Submitted ? submittedAt : null,
                CreatedAt = DateTime.UtcNow,
            });
            assignmentsAdded++;
        }

        if (assignmentsAdded > 0)
            await db.SaveChangesAsync(ct);

        // Submissions are guarded SEPARATELY from the assignments above: if a previous run inserted the
        // assignments and then failed, this block must still be able to fill in the missing feedback.
        var existingFeedback = (await db.Feedback360s
            .IgnoreQueryFilters()
            .Where(f => f.TenantId == tenantId
                && f.CycleId == cycleId
                && revieweeIds.Contains(f.RevieweeEmployeeId))
            .Select(f => new { f.RevieweeEmployeeId, f.ReviewerEmployeeId })
            .ToListAsync(ct))
            .Select(f => (f.RevieweeEmployeeId, f.ReviewerEmployeeId))
            .ToHashSet();

        var feedbackAdded = 0;
        foreach (var entry in plan.Where(p => p.Submitted))
        {
            if (existingFeedback.Contains((entry.Reviewee, entry.Reviewer)))
                continue;

            var feedback = new Feedback360
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                CycleId = cycleId,
                RevieweeEmployeeId = entry.Reviewee,
                ReviewerEmployeeId = entry.Reviewer,
                Category = entry.Category,
                IsAnonymous = false,
                OverallComment = $"Synthetic {entry.Category} feedback seeded for the DEV E2E tenant.",
                SubmittedAt = submittedAt,
                CreatedAt = DateTime.UtcNow,
            };

            foreach (var competency in SubmittedCompetencies)
            {
                feedback.Items.Add(new Feedback360Item
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = tenantId,
                    Feedback360Id = feedback.Id,
                    CompetencyKey = competency.Key,
                    Rating = competency.Rating,
                    Comment = competency.Comment,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            db.Feedback360s.Add(feedback);
            feedbackAdded++;
        }

        if (feedbackAdded > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Seeded {Assignments} DEV E2E 360 reviewer assignments and {Feedback} submissions for tenant {TenantId}",
                assignmentsAdded, feedbackAdded, tenantId);
        }
    }

    // ── Offboarding ────────────────────────────────────────────────────────────

    private sealed record DemoOffboardingTask(
        ClearanceCategory Category,
        string Title,
        OnboardingResponsibleRole ResponsibleRole,
        int DueInDays,
        bool IsMandatory,
        int SortOrder,
        OnboardingTaskStatus Status,
        ClearanceStatus? Clearance);

    private static readonly DemoOffboardingTask[] InProgressTasks =
    [
        new(ClearanceCategory.Manager, "Handover of active work items", OnboardingResponsibleRole.Manager,
            7, true, 1, OnboardingTaskStatus.Completed, HRM.Domain.Enums.ClearanceStatus.Approved),
        new(ClearanceCategory.IT, "Revoke system access and collect laptop", OnboardingResponsibleRole.IT,
            21, true, 2, OnboardingTaskStatus.InProgress, null),
        new(ClearanceCategory.Finance, "Settle outstanding expense claims", OnboardingResponsibleRole.HR,
            21, true, 3, OnboardingTaskStatus.Pending, null),
        new(ClearanceCategory.Admin, "Return access card and desk keys", OnboardingResponsibleRole.HR,
            21, true, 4, OnboardingTaskStatus.Pending, null),
        new(ClearanceCategory.HR, "Conduct exit interview", OnboardingResponsibleRole.HR,
            18, true, 5, OnboardingTaskStatus.Pending, null),
        new(ClearanceCategory.Employee, "Acknowledge final settlement statement", OnboardingResponsibleRole.Employee,
            21, false, 6, OnboardingTaskStatus.Pending, null),
    ];

    private static readonly DemoOffboardingTask[] CompletedTasks =
    [
        new(ClearanceCategory.Manager, "Handover of active work items", OnboardingResponsibleRole.Manager,
            -35, true, 1, OnboardingTaskStatus.Completed, HRM.Domain.Enums.ClearanceStatus.Approved),
        new(ClearanceCategory.IT, "Revoke system access and collect laptop", OnboardingResponsibleRole.IT,
            -30, true, 2, OnboardingTaskStatus.Completed, HRM.Domain.Enums.ClearanceStatus.Approved),
        new(ClearanceCategory.Finance, "Settle outstanding expense claims", OnboardingResponsibleRole.HR,
            -30, true, 3, OnboardingTaskStatus.Completed, HRM.Domain.Enums.ClearanceStatus.PendingIssues),
        new(ClearanceCategory.HR, "Conduct exit interview", OnboardingResponsibleRole.HR,
            -32, true, 4, OnboardingTaskStatus.Completed, HRM.Domain.Enums.ClearanceStatus.Approved),
    ];

    /// <summary>
    /// One IN-PROGRESS instance (mid notice period, mixed task statuses — the state the clearance checklist
    /// is operated in) and one COMPLETED instance (a closed exit), so the offboarding list, the detail view
    /// and the status filter all have something to show. Guarded per employee.
    /// </summary>
    private static async Task EnsureOffboardingAsync(
        AppDbContext db,
        Guid tenantId,
        IReadOnlyDictionary<string, Guid> employeeIds,
        DateTime now,
        ILogger logger,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);

        (string EmployeeNo, OffboardingReason Reason, OffboardingStatus Status, int LastWorkingDayOffset,
            string Notes, DemoOffboardingTask[] Tasks)[] plan =
        [
            (ResigningEmployeeNo, OffboardingReason.Resignation, OffboardingStatus.InProgress, 21,
                "Resigned to join another company; serving notice.", InProgressTasks),
            (TerminatedEmployeeNo, OffboardingReason.Termination, OffboardingStatus.Completed, -30,
                "Contract terminated; clearance closed.", CompletedTasks),
        ];

        var targetEmployeeIds = plan.Select(p => employeeIds[p.EmployeeNo]).ToList();

        var existing = await db.OffboardingInstances
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId && targetEmployeeIds.Contains(o.EmployeeId))
            .Select(o => o.EmployeeId)
            .ToListAsync(ct);

        var added = 0;
        foreach (var entry in plan)
        {
            var employeeId = employeeIds[entry.EmployeeNo];
            if (existing.Contains(employeeId))
                continue;

            var instance = new OffboardingInstance
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                TemplateId = null,
                TemplateName = DemoOffboardingTemplateName,
                LastWorkingDay = today.AddDays(entry.LastWorkingDayOffset),
                Reason = entry.Reason,
                Notes = entry.Notes,
                Status = entry.Status,
                CompletedAt = entry.Status == OffboardingStatus.Completed ? now.AddDays(-28) : null,
                CreatedAt = DateTime.UtcNow,
            };

            foreach (var task in entry.Tasks)
            {
                instance.Tasks.Add(new OffboardingTaskInstance
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = tenantId,
                    OffboardingInstanceId = instance.Id,
                    ClearanceCategory = task.Category,
                    Title = task.Title,
                    ResponsibleRole = task.ResponsibleRole,
                    DueDate = today.AddDays(task.DueInDays),
                    Status = task.Status,
                    IsMandatory = task.IsMandatory,
                    SortOrder = task.SortOrder,
                    ClearanceStatus = task.Clearance,
                    CompletedAt = task.Status == OnboardingTaskStatus.Completed ? now.AddDays(-5) : null,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            db.OffboardingInstances.Add(instance);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Seeded {Count} DEV E2E offboarding instances for tenant {TenantId}", added, tenantId);
        }
    }
}
