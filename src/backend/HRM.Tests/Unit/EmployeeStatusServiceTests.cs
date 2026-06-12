// ============================================================================
// US-CHR-009: Employee Status Management Unit Tests
// Tests state machine transitions (FR-2, BR-1), status change recording (FR-4),
// audit logging (NFR-5), side effects (FR-5, AC-3), idempotency (NFR-3),
// future-dated changes (BR-4), probation reminders (FR-6, AC-4, BR-6),
// invalid transitions (AC-5), and tenant isolation (NFR-2).
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeeStatusServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<EmployeeStatusService> _logger;

    public EmployeeStatusServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Permissions.Returns(new List<string> { "Employee.ChangeStatus", "Employee.Edit" });

        _logger = Substitute.For<ILogger<EmployeeStatusService>>();
    }

    private EmployeeStatusService CreateService()
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new EmployeeStatusService(dbContext, _tenantContext, _currentUser, _logger);
    }

    private AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private async Task<Guid> SeedEmployee(
        EmployeeStatus status = EmployeeStatus.Active,
        Guid? userId = null,
        Guid? tenantId = null,
        DateTime? dateOfJoining = null)
    {
        var tid = tenantId ?? _tenantId;

        ITenantContext ctx;
        if (tenantId.HasValue && tenantId.Value != _tenantId)
        {
            ctx = Substitute.For<ITenantContext>();
            ctx.TenantId.Returns(tid);
            ctx.IsResolved.Returns(true);
        }
        else
        {
            ctx = _tenantContext;
        }

        await using var db = TestDbContextFactory.Create(ctx, _dbName);

        // Ensure department and job title exist
        var deptId = BaseEntity.NewUuidV7();
        var jtId = BaseEntity.NewUuidV7();

        if (!await db.Departments.AnyAsync(d => d.TenantId == tid))
        {
            db.Departments.Add(new Department
            {
                Id = deptId,
                TenantId = tid,
                Name = $"Dept-{tid.ToString()[..8]}",
                Code = $"D-{tid.ToString()[..4]}",
                IsActive = true,
            });
        }
        else
        {
            deptId = (await db.Departments.FirstAsync(d => d.TenantId == tid)).Id;
        }

        if (!await db.JobTitles.AnyAsync(j => j.TenantId == tid))
        {
            db.JobTitles.Add(new JobTitle
            {
                Id = jtId,
                TenantId = tid,
                TitleName = $"Dev-{tid.ToString()[..8]}",
                IsActive = true,
            });
        }
        else
        {
            jtId = (await db.JobTitles.FirstAsync(j => j.TenantId == tid)).Id;
        }

        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId,
            TenantId = tid,
            EmployeeNo = $"EMP-{Random.Shared.Next(1000, 9999)}",
            FirstName = "Test",
            LastName = "Employee",
            Email = $"test-{empId.ToString()[..8]}@test.com",
            DateOfJoining = dateOfJoining ?? DateTime.UtcNow.AddDays(-30),
            DepartmentId = deptId,
            JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime,
            Status = status,
            IsActive = status is EmployeeStatus.Active or EmployeeStatus.Probation,
            UserId = userId,
        });

        await db.SaveChangesAsync();
        return empId;
    }

    private async Task<Guid> SeedUser(bool isActive = true)
    {
        await using var db = CreateDbContext();
        var userId = BaseEntity.NewUuidV7();
        db.Users.Add(new User
        {
            Id = userId,
            Email = $"user-{userId.ToString()[..8]}@test.com",
            IsActive = isActive,
            PasswordHash = "hashed",
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task SeedRefreshToken(Guid userId)
    {
        await using var db = CreateDbContext();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = userId,
            TenantId = _tenantId,
            TokenHash = "test-hash-" + Guid.NewGuid(),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        // InMemory database is disposed with the last DbContext instance
    }

    // ── State Machine Tests ──────────────────────────────────────

    [Fact]
    public void StateMachine_ActiveToSuspended_IsValid()
    {
        EmployeeStatusStateMachine.IsValidTransition(EmployeeStatus.Active, EmployeeStatus.Suspended)
            .Should().BeTrue();
    }

    [Fact]
    public void StateMachine_ActiveToTerminated_IsValid()
    {
        EmployeeStatusStateMachine.IsValidTransition(EmployeeStatus.Active, EmployeeStatus.Terminated)
            .Should().BeTrue();
    }

    [Fact]
    public void StateMachine_ActiveToInactive_IsValid()
    {
        EmployeeStatusStateMachine.IsValidTransition(EmployeeStatus.Active, EmployeeStatus.Inactive)
            .Should().BeTrue();
    }

    [Fact]
    public void StateMachine_ProbationToActive_IsValid()
    {
        EmployeeStatusStateMachine.IsValidTransition(EmployeeStatus.Probation, EmployeeStatus.Active)
            .Should().BeTrue();
    }

    [Fact]
    public void StateMachine_ProbationToTerminated_IsValid()
    {
        EmployeeStatusStateMachine.IsValidTransition(EmployeeStatus.Probation, EmployeeStatus.Terminated)
            .Should().BeTrue();
    }

    [Fact]
    public void StateMachine_SuspendedToActive_IsValid()
    {
        EmployeeStatusStateMachine.IsValidTransition(EmployeeStatus.Suspended, EmployeeStatus.Active)
            .Should().BeTrue();
    }

    [Fact]
    public void StateMachine_InactiveToActive_IsValid()
    {
        EmployeeStatusStateMachine.IsValidTransition(EmployeeStatus.Inactive, EmployeeStatus.Active)
            .Should().BeTrue();
    }

    [Fact]
    public void StateMachine_TerminatedHasNoOutbound()
    {
        EmployeeStatusStateMachine.GetValidTransitions(EmployeeStatus.Terminated)
            .Should().BeEmpty();
    }

    [Fact]
    public void StateMachine_TerminatedToProbation_IsInvalid()
    {
        EmployeeStatusStateMachine.IsValidTransition(EmployeeStatus.Terminated, EmployeeStatus.Probation)
            .Should().BeFalse();
    }

    [Fact]
    public void StateMachine_ActiveToProbation_IsInvalid()
    {
        EmployeeStatusStateMachine.IsValidTransition(EmployeeStatus.Active, EmployeeStatus.Probation)
            .Should().BeFalse();
    }

    [Fact]
    public void StateMachine_InvalidTransitionMessage_IsDescriptive()
    {
        var msg = EmployeeStatusStateMachine.GetInvalidTransitionMessage(EmployeeStatus.Terminated, EmployeeStatus.Probation);
        msg.Should().Contain("Terminated");
        msg.Should().Contain("Probation");
        msg.Should().Contain("Invalid status transition");
    }

    // ── Valid Transitions Query ──────────────────────────────────

    [Fact]
    public async Task GetValidTransitions_ReturnsCorrectTransitions()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);
        var service = CreateService();

        var result = await service.GetValidTransitionsAsync(empId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CurrentStatus.Should().Be("Active");
        result.Value.ValidTransitions.Should().Contain("Suspended");
        result.Value.ValidTransitions.Should().Contain("Terminated");
        result.Value.ValidTransitions.Should().Contain("Inactive");
        result.Value.ValidTransitions.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetValidTransitions_TerminatedEmployee_ReturnsEmpty()
    {
        var empId = await SeedEmployee(EmployeeStatus.Terminated);
        var service = CreateService();

        var result = await service.GetValidTransitionsAsync(empId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ValidTransitions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetValidTransitions_NonExistentEmployee_Returns404()
    {
        var service = CreateService();

        var result = await service.GetValidTransitionsAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Change Status Tests ──────────────────────────────────────

    [Fact]
    public async Task ChangeStatus_ValidTransition_UpdatesStatusAndRecordsHistory()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);
        var service = CreateService();
        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Suspended,
            Reason = "Policy violation under investigation",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        var result = await service.ChangeStatusAsync(empId, request, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PreviousStatus.Should().Be("Active");
        result.Value.NewStatus.Should().Be("Suspended");
        result.Value.IsFutureDated.Should().BeFalse();

        // Verify employee status updated in DB
        await using var db = CreateDbContext();
        var employee = await db.Employees.FirstAsync(e => e.Id == empId);
        employee.Status.Should().Be(EmployeeStatus.Suspended);
        employee.IsActive.Should().BeFalse();

        // Verify employment history entry written
        var history = await db.EmploymentHistories
            .Where(h => h.EmployeeId == empId && h.ChangeType == "status_change")
            .FirstOrDefaultAsync();
        history.Should().NotBeNull();
        history!.PreviousValue.Should().Be("Active");
        history.NewValue.Should().Be("Suspended");
        history.Reason.Should().Be("Policy violation under investigation");
    }

    [Fact]
    public async Task ChangeStatus_ValidTransition_WritesAuditLog()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);
        var service = CreateService();
        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Terminated,
            Reason = "End of contract",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        await service.ChangeStatusAsync(empId, request, null);

        await using var db = CreateDbContext();
        var audit = await db.EmployeeFieldAuditLogs
            .Where(a => a.EmployeeId == empId && a.Section == "StatusChange")
            .FirstOrDefaultAsync();
        audit.Should().NotBeNull();
        audit!.BeforeSnapshot.Should().Contain("Active");
        audit.AfterSnapshot.Should().Contain("Terminated");
    }

    [Fact]
    public async Task ChangeStatus_InvalidTransition_Returns400()
    {
        var empId = await SeedEmployee(EmployeeStatus.Terminated);
        var service = CreateService();
        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Probation,
            Reason = "Rehire attempt",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        var result = await service.ChangeStatusAsync(empId, request, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Terminated");
        result.Error.Should().Contain("Probation");
        result.Error.Should().Contain("Invalid status transition");
    }

    [Fact]
    public async Task ChangeStatus_NonExistentEmployee_Returns404()
    {
        var service = CreateService();
        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Suspended,
            Reason = "Test",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        var result = await service.ChangeStatusAsync(Guid.NewGuid(), request, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ChangeStatus_UnresolvedTenant_Returns400()
    {
        var unresolvedTenant = Substitute.For<ITenantContext>();
        unresolvedTenant.IsResolved.Returns(false);

        await using var db = TestDbContextFactory.Create(unresolvedTenant, _dbName);
        var service = new EmployeeStatusService(db, unresolvedTenant, _currentUser, _logger);

        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Suspended,
            Reason = "Test",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        var result = await service.ChangeStatusAsync(Guid.NewGuid(), request, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context");
    }

    // ── Side Effects Tests ───────────────────────────────────────

    [Fact]
    public async Task ChangeStatus_ToTerminated_DisablesUserLogin()
    {
        var userId = await SeedUser(isActive: true);
        await SeedRefreshToken(userId);
        var empId = await SeedEmployee(EmployeeStatus.Active, userId: userId);
        var service = CreateService();

        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Terminated,
            Reason = "Employment ended",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        var result = await service.ChangeStatusAsync(empId, request, null);
        result.IsSuccess.Should().BeTrue();

        // Verify user login is disabled
        await using var db = CreateDbContext();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.IsActive.Should().BeFalse();

        // Verify refresh tokens are revoked
        var tokens = await db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.UserId == userId)
            .ToListAsync();
        tokens.Should().AllSatisfy(t => t.RevokedAt.Should().NotBeNull());
    }

    [Fact]
    public async Task ChangeStatus_ToSuspended_DisablesUserLogin()
    {
        var userId = await SeedUser(isActive: true);
        var empId = await SeedEmployee(EmployeeStatus.Active, userId: userId);
        var service = CreateService();

        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Suspended,
            Reason = "Under investigation",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        var result = await service.ChangeStatusAsync(empId, request, null);
        result.IsSuccess.Should().BeTrue();

        await using var db = CreateDbContext();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeStatus_SuspendedToActive_ReEnablesUserLogin()
    {
        var userId = await SeedUser(isActive: false);
        var empId = await SeedEmployee(EmployeeStatus.Suspended, userId: userId);
        var service = CreateService();

        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Active,
            Reason = "Investigation cleared",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        var result = await service.ChangeStatusAsync(empId, request, null);
        result.IsSuccess.Should().BeTrue();

        await using var db = CreateDbContext();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ChangeStatus_TerminatedExcludedFromActiveHeadcount()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);
        var service = CreateService();

        // Terminate the employee
        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Terminated,
            Reason = "End of contract",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        await service.ChangeStatusAsync(empId, request, null);

        // Check that IsActive is false (excluded from active headcount queries)
        await using var db = CreateDbContext();
        var employee = await db.Employees.FirstAsync(e => e.Id == empId);
        employee.IsActive.Should().BeFalse();

        // Active headcount should not include this employee
        var activeCount = await db.Employees.CountAsync(e => e.IsActive);
        activeCount.Should().Be(0);
    }

    [Fact]
    public async Task ChangeStatus_NoLinkedUser_SideEffectsSkippedGracefully()
    {
        // Employee without a linked user account
        var empId = await SeedEmployee(EmployeeStatus.Active, userId: null);
        var service = CreateService();

        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Terminated,
            Reason = "No user test",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        // Should not throw
        var result = await service.ChangeStatusAsync(empId, request, null);
        result.IsSuccess.Should().BeTrue();
    }

    // ── Idempotency Tests ────────────────────────────────────────

    [Fact]
    public async Task ChangeStatus_SameIdempotencyKey_RecordsOnlyOneChange()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);
        var idempotencyKey = Guid.NewGuid().ToString();

        // First call
        var service1 = CreateService();
        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Suspended,
            Reason = "Idempotency test",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        var result1 = await service1.ChangeStatusAsync(empId, request, idempotencyKey);
        result1.IsSuccess.Should().BeTrue();

        // Second call with same key
        var service2 = CreateService();
        var result2 = await service2.ChangeStatusAsync(empId, request, idempotencyKey);
        result2.IsSuccess.Should().BeTrue();

        // Verify only one employment history entry was created
        await using var db = CreateDbContext();
        var historyCount = await db.EmploymentHistories
            .CountAsync(h => h.EmployeeId == empId && h.ChangeType == "status_change");
        historyCount.Should().Be(1);

        // Verify only one idempotency record exists
        var idempotencyCount = await db.IdempotencyRecords
            .CountAsync(r => r.IdempotencyKey == idempotencyKey);
        idempotencyCount.Should().Be(1);
    }

    [Fact]
    public async Task ChangeStatus_DifferentIdempotencyKeys_RecordsBothChanges()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);

        // First call: Active -> Suspended
        var service1 = CreateService();
        var request1 = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Suspended,
            Reason = "First change",
            EffectiveDate = DateTime.UtcNow.Date,
        };
        await service1.ChangeStatusAsync(empId, request1, Guid.NewGuid().ToString());

        // Second call: Suspended -> Active (different key, different transition)
        var service2 = CreateService();
        var request2 = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Active,
            Reason = "Second change",
            EffectiveDate = DateTime.UtcNow.Date,
        };
        await service2.ChangeStatusAsync(empId, request2, Guid.NewGuid().ToString());

        // Verify two employment history entries
        await using var db = CreateDbContext();
        var historyCount = await db.EmploymentHistories
            .CountAsync(h => h.EmployeeId == empId && h.ChangeType == "status_change");
        historyCount.Should().Be(2);
    }

    [Fact]
    public async Task ChangeStatus_NoIdempotencyKey_StillWorks()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);
        var service = CreateService();

        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Suspended,
            Reason = "No key test",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        var result = await service.ChangeStatusAsync(empId, request, null);
        result.IsSuccess.Should().BeTrue();

        // No idempotency record should be created
        await using var db = CreateDbContext();
        var count = await db.IdempotencyRecords.CountAsync();
        count.Should().Be(0);
    }

    // ── Future-Dated Tests ───────────────────────────────────────

    [Fact]
    public async Task ChangeStatus_FutureDate_StoresWithoutApplying()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);
        var service = CreateService();

        var futureDate = DateTime.UtcNow.Date.AddDays(5);
        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Suspended,
            Reason = "Future suspension",
            EffectiveDate = futureDate,
        };

        var result = await service.ChangeStatusAsync(empId, request, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsFutureDated.Should().BeTrue();
        result.Value.EffectiveDate.Should().Be(futureDate);

        // Employee status should NOT be changed yet
        await using var db = CreateDbContext();
        var employee = await db.Employees.FirstAsync(e => e.Id == empId);
        employee.Status.Should().Be(EmployeeStatus.Active);
        employee.IsActive.Should().BeTrue();

        // Future-dated change record should exist
        var pending = await db.FutureDatedStatusChanges
            .FirstOrDefaultAsync(f => f.EmployeeId == empId);
        pending.Should().NotBeNull();
        pending!.FromStatus.Should().Be(EmployeeStatus.Active);
        pending.ToStatus.Should().Be(EmployeeStatus.Suspended);
        pending.IsApplied.Should().BeFalse();
        pending.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyPendingChanges_AppliesDueChanges()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);

        // Manually insert a future-dated change that is now due
        {
            await using var db = CreateDbContext();
            db.FutureDatedStatusChanges.Add(new FutureDatedStatusChange
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = empId,
                FromStatus = EmployeeStatus.Active,
                ToStatus = EmployeeStatus.Suspended,
                Reason = "Scheduled suspension",
                EffectiveDate = DateTime.UtcNow.Date, // Due today
                RequestedByUserId = _currentUser.UserId,
                RequestedBy = _currentUser.Email,
            });
            await db.SaveChangesAsync();
        }

        // Run the background job method
        var service = CreateService();
        await service.ApplyPendingFutureDatedChangesAsync();

        // Verify employee status changed
        {
            await using var db = CreateDbContext();
            var employee = await db.Employees.FirstAsync(e => e.Id == empId);
            employee.Status.Should().Be(EmployeeStatus.Suspended);
            employee.IsActive.Should().BeFalse();

            // Verify the pending change is marked applied
            var pending = await db.FutureDatedStatusChanges
                .FirstAsync(f => f.EmployeeId == empId);
            pending.IsApplied.Should().BeTrue();
            pending.AppliedAt.Should().NotBeNull();

            // Verify employment history was written
            var history = await db.EmploymentHistories
                .FirstOrDefaultAsync(h => h.EmployeeId == empId && h.ChangeType == "status_change");
            history.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ApplyPendingChanges_SkipsFutureChanges()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);

        // Insert a change with a future effective date
        {
            await using var db = CreateDbContext();
            db.FutureDatedStatusChanges.Add(new FutureDatedStatusChange
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = empId,
                FromStatus = EmployeeStatus.Active,
                ToStatus = EmployeeStatus.Suspended,
                Reason = "Not yet due",
                EffectiveDate = DateTime.UtcNow.Date.AddDays(10),
                RequestedByUserId = _currentUser.UserId,
                RequestedBy = _currentUser.Email,
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        await service.ApplyPendingFutureDatedChangesAsync();

        // Employee status should not have changed
        {
            await using var db = CreateDbContext();
            var employee = await db.Employees.FirstAsync(e => e.Id == empId);
            employee.Status.Should().Be(EmployeeStatus.Active);

            var pending = await db.FutureDatedStatusChanges.FirstAsync(f => f.EmployeeId == empId);
            pending.IsApplied.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ApplyPendingChanges_CancelsIfTransitionNoLongerValid()
    {
        // Start as Active, schedule Active -> Suspended
        var empId = await SeedEmployee(EmployeeStatus.Active);

        {
            await using var db = CreateDbContext();
            db.FutureDatedStatusChanges.Add(new FutureDatedStatusChange
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = empId,
                FromStatus = EmployeeStatus.Active,
                ToStatus = EmployeeStatus.Suspended,
                Reason = "Scheduled suspension",
                EffectiveDate = DateTime.UtcNow.Date,
                RequestedByUserId = _currentUser.UserId,
                RequestedBy = _currentUser.Email,
            });
            await db.SaveChangesAsync();
        }

        // Manually change employee to Terminated before the job runs
        {
            await using var db = CreateDbContext();
            var emp = await db.Employees.FirstAsync(e => e.Id == empId);
            emp.Status = EmployeeStatus.Terminated;
            emp.IsActive = false;
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        await service.ApplyPendingFutureDatedChangesAsync();

        // Verify the pending change is cancelled (Terminated -> Suspended is invalid)
        {
            await using var db = CreateDbContext();
            var pending = await db.FutureDatedStatusChanges.FirstAsync(f => f.EmployeeId == empId);
            pending.IsCancelled.Should().BeTrue();
            pending.IsApplied.Should().BeFalse();
        }
    }

    // ── Probation Reminder Tests ─────────────────────────────────

    [Fact]
    public async Task CheckProbationEndDates_LogsReminderForApproachingEndDate()
    {
        // Employee on probation, joining date set so probation ends in 5 days
        var joiningDate = DateTime.UtcNow.Date.AddDays(-85); // 90 - 85 = 5 days until end
        await SeedEmployee(EmployeeStatus.Probation, dateOfJoining: joiningDate);

        var service = CreateService();
        await service.CheckProbationEndDatesAsync();

        // Verify the logger was called with PROBATION_REMINDER
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains("PROBATION_REMINDER")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task CheckProbationEndDates_DoesNotLogForDistantEndDate()
    {
        // Employee on probation, joining date set so probation ends in 30 days
        var joiningDate = DateTime.UtcNow.Date.AddDays(-60); // 90 - 60 = 30 days until end
        await SeedEmployee(EmployeeStatus.Probation, dateOfJoining: joiningDate);

        var service = CreateService();
        await service.CheckProbationEndDatesAsync();

        // Should NOT log a PROBATION_REMINDER warning
        _logger.DidNotReceive().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains("PROBATION_REMINDER")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // ── Tenant Isolation Tests ───────────────────────────────────

    [Fact]
    public async Task ChangeStatus_TenantIsolation_CannotChangeOtherTenantEmployee()
    {
        var otherTenantId = Guid.NewGuid();
        var empId = await SeedEmployee(EmployeeStatus.Active, tenantId: otherTenantId);

        // Service uses _tenantId (not otherTenantId)
        var service = CreateService();
        var request = new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Suspended,
            Reason = "Cross-tenant test",
            EffectiveDate = DateTime.UtcNow.Date,
        };

        var result = await service.ChangeStatusAsync(empId, request, null);

        // Should not find the employee (global query filter prevents cross-tenant access)
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Multiple Status Changes / Employment History ─────────────

    [Fact]
    public async Task MultipleStatusChanges_CreateMultipleHistoryEntries()
    {
        var empId = await SeedEmployee(EmployeeStatus.Active);

        // Change 1: Active -> Suspended
        var service1 = CreateService();
        await service1.ChangeStatusAsync(empId, new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Suspended,
            Reason = "First suspension",
            EffectiveDate = DateTime.UtcNow.Date,
        }, null);

        // Change 2: Suspended -> Active
        var service2 = CreateService();
        await service2.ChangeStatusAsync(empId, new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Active,
            Reason = "Reinstatement",
            EffectiveDate = DateTime.UtcNow.Date,
        }, null);

        // Change 3: Active -> Terminated
        var service3 = CreateService();
        await service3.ChangeStatusAsync(empId, new ChangeEmployeeStatusRequest
        {
            NewStatus = EmployeeStatus.Terminated,
            Reason = "End of contract",
            EffectiveDate = DateTime.UtcNow.Date,
        }, null);

        // Verify 3 history entries
        await using var db = CreateDbContext();
        var entries = await db.EmploymentHistories
            .Where(h => h.EmployeeId == empId && h.ChangeType == "status_change")
            .OrderBy(h => h.CreatedAt)
            .ToListAsync();

        entries.Should().HaveCount(3);
        entries[0].PreviousValue.Should().Be("Active");
        entries[0].NewValue.Should().Be("Suspended");
        entries[1].PreviousValue.Should().Be("Suspended");
        entries[1].NewValue.Should().Be("Active");
        entries[2].PreviousValue.Should().Be("Active");
        entries[2].NewValue.Should().Be("Terminated");
    }

    // ── Status State Machine domain tests ────────────────────────

    [Theory]
    [InlineData(EmployeeStatus.Probation, new[] { EmployeeStatus.Active, EmployeeStatus.Terminated })]
    [InlineData(EmployeeStatus.Active, new[] { EmployeeStatus.Suspended, EmployeeStatus.Terminated, EmployeeStatus.Inactive })]
    [InlineData(EmployeeStatus.Suspended, new[] { EmployeeStatus.Active, EmployeeStatus.Terminated })]
    [InlineData(EmployeeStatus.Inactive, new[] { EmployeeStatus.Active, EmployeeStatus.Terminated })]
    public void StateMachine_GetValidTransitions_MatchesExpected(EmployeeStatus from, EmployeeStatus[] expected)
    {
        var transitions = EmployeeStatusStateMachine.GetValidTransitions(from);
        transitions.Should().BeEquivalentTo(expected);
    }
}
