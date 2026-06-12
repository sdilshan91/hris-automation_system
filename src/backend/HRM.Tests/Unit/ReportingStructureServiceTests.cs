// ============================================================================
// US-CHR-011: Employee Reporting Structure (Manager Assignment) Unit Tests.
// Tests: assign manager sets FK (AC-2); direct reports includes assigned employee (FR-5);
// circular chain A->B->C then C->A rejected (AC-3); self-assignment rejected (BR-7);
// inactive manager rejected (BR-3); bulk assign with per-employee results (AC-5);
// tenant isolation on direct reports (NFR-3); deep chain cycle detection (NFR-2);
// employment history before/after entries (FR-6); unassign manager (FR-8).
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

public sealed class ReportingStructureServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ReportingStructureService> _logger;

    public ReportingStructureServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());

        _logger = Substitute.For<ILogger<ReportingStructureService>>();
    }

    private ReportingStructureService CreateService(ITenantContext? ctx = null)
    {
        var dbContext = TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);
        return new ReportingStructureService(dbContext, ctx ?? _tenantContext, _currentUser, _logger);
    }

    private AppDbContext CreateDbContext(ITenantContext? ctx = null)
    {
        return TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);
    }

    private Employee CreateEmployee(string firstName, string lastName, EmployeeStatus status = EmployeeStatus.Active, Guid? id = null)
    {
        return new Employee
        {
            Id = id ?? BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeNo = $"EMP-{Random.Shared.Next(1000, 9999)}",
            FirstName = firstName,
            LastName = lastName,
            Email = $"{firstName.ToLower()}.{lastName.ToLower()}@test.com",
            DateOfJoining = DateTime.UtcNow.AddDays(-30),
            DepartmentId = Guid.NewGuid(),
            JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime,
            Status = status,
            IsActive = status is EmployeeStatus.Active or EmployeeStatus.Probation,
            IsDeleted = false,
        };
    }

    [Fact]
    public async Task AssignManager_SetsFK_And_ManagerDirectReportsIncludesEmployee()
    {
        // Arrange
        var manager = CreateEmployee("Manager", "One");
        var employee = CreateEmployee("Employee", "One");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(manager, employee);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.AssignManagerAsync(employee.Id, new AssignManagerRequest
        {
            ManagerEmployeeId = manager.Id,
            Reason = "Initial assignment",
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeId.Should().Be(employee.Id);
        result.Value.NewManagerId.Should().Be(manager.Id);
        result.Value.NewManagerName.Should().Be("Manager One");

        // Verify FK is set in the database
        await using (var db = CreateDbContext())
        {
            var updated = await db.Employees.FirstAsync(e => e.Id == employee.Id);
            updated.ReportsToEmployeeId.Should().Be(manager.Id);
        }

        // Verify manager's direct reports include the employee using a fresh service
        var service2 = CreateService();
        var directReportsResult = await service2.GetDirectReportsAsync(manager.Id);
        directReportsResult.IsSuccess.Should().BeTrue();
        directReportsResult.Value!.DirectReports.Should().ContainSingle(r => r.Id == employee.Id);
    }

    [Fact]
    public async Task AssignManager_CircularChain_A_B_C_ThenC_ToA_Rejected()
    {
        // Arrange: Create A -> B -> C chain
        var empA = CreateEmployee("Alice", "A");
        var empB = CreateEmployee("Bob", "B");
        var empC = CreateEmployee("Charlie", "C");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(empA, empB, empC);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // A reports to B
        var r1 = await service.AssignManagerAsync(empA.Id, new AssignManagerRequest { ManagerEmployeeId = empB.Id });
        r1.IsSuccess.Should().BeTrue();

        // B reports to C
        var r2 = await service.AssignManagerAsync(empB.Id, new AssignManagerRequest { ManagerEmployeeId = empC.Id });
        r2.IsSuccess.Should().BeTrue();

        // Act: C reports to A (would create cycle C -> A -> B -> C)
        var r3 = await service.AssignManagerAsync(empC.Id, new AssignManagerRequest { ManagerEmployeeId = empA.Id });

        // Assert
        r3.IsFailure.Should().BeTrue();
        r3.Error.Should().Contain("Circular reporting chain detected");
        r3.Error.Should().Contain("Charlie C");
        r3.Error.Should().Contain("Alice A");
    }

    [Fact]
    public async Task AssignManager_SelfAssignment_Rejected()
    {
        var employee = CreateEmployee("Self", "Reporter");

        await using (var db = CreateDbContext())
        {
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.AssignManagerAsync(employee.Id, new AssignManagerRequest
        {
            ManagerEmployeeId = employee.Id,
        });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("cannot be assigned as their own manager");
    }

    [Fact]
    public async Task AssignManager_InactiveManager_Rejected()
    {
        var inactiveManager = CreateEmployee("Inactive", "Manager", EmployeeStatus.Terminated);
        var employee = CreateEmployee("Employee", "One");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(inactiveManager, employee);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.AssignManagerAsync(employee.Id, new AssignManagerRequest
        {
            ManagerEmployeeId = inactiveManager.Id,
        });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Only active employees can be assigned as managers");
    }

    [Fact]
    public async Task AssignManager_SuspendedManager_Rejected()
    {
        var suspendedManager = CreateEmployee("Suspended", "Manager", EmployeeStatus.Suspended);
        var employee = CreateEmployee("Employee", "Two");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(suspendedManager, employee);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.AssignManagerAsync(employee.Id, new AssignManagerRequest
        {
            ManagerEmployeeId = suspendedManager.Id,
        });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Only active employees can be assigned as managers");
    }

    [Fact]
    public async Task AssignManager_RecordsEmploymentHistory_WithBeforeAfter()
    {
        var manager = CreateEmployee("Manager", "M");
        var employee = CreateEmployee("Employee", "E");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(manager, employee);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: First assignment
        await service.AssignManagerAsync(employee.Id, new AssignManagerRequest
        {
            ManagerEmployeeId = manager.Id,
            Reason = "Initial assignment",
        });

        // Assert: Employment history entry exists
        await using (var db = CreateDbContext())
        {
            var history = await db.EmploymentHistories
                .Where(h => h.EmployeeId == employee.Id && h.ChangeType == "manager_change")
                .ToListAsync();

            history.Should().HaveCount(1);
            history[0].PreviousValue.Should().Be("Not Assigned");
            history[0].NewValue.Should().Be("Manager M");
            history[0].PreviousReferenceId.Should().BeNull();
            history[0].NewReferenceId.Should().Be(manager.Id);
            history[0].Reason.Should().Be("Initial assignment");
        }
    }

    [Fact]
    public async Task AssignManager_Reassign_RecordsTwoHistoryEntries()
    {
        var manager1 = CreateEmployee("Manager", "One");
        var manager2 = CreateEmployee("Manager", "Two");
        var employee = CreateEmployee("Employee", "E");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(manager1, manager2, employee);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: First assignment
        await service.AssignManagerAsync(employee.Id, new AssignManagerRequest
        {
            ManagerEmployeeId = manager1.Id,
        });

        // Reassign
        await service.AssignManagerAsync(employee.Id, new AssignManagerRequest
        {
            ManagerEmployeeId = manager2.Id,
            Reason = "Team restructure",
        });

        // Assert: 2 employment history entries
        await using (var db = CreateDbContext())
        {
            var history = await db.EmploymentHistories
                .Where(h => h.EmployeeId == employee.Id && h.ChangeType == "manager_change")
                .OrderBy(h => h.CreatedAt)
                .ToListAsync();

            history.Should().HaveCount(2);

            // Second entry should have manager1 as previous
            history[1].PreviousValue.Should().Be("Manager One");
            history[1].NewValue.Should().Be("Manager Two");
            history[1].PreviousReferenceId.Should().Be(manager1.Id);
            history[1].NewReferenceId.Should().Be(manager2.Id);
        }
    }

    [Fact]
    public async Task UnassignManager_SetsFK_ToNull()
    {
        var manager = CreateEmployee("Manager", "M");
        var employee = CreateEmployee("Employee", "E");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(manager, employee);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Assign first
        await service.AssignManagerAsync(employee.Id, new AssignManagerRequest { ManagerEmployeeId = manager.Id });

        // Act: Unassign
        var result = await service.AssignManagerAsync(employee.Id, new AssignManagerRequest
        {
            ManagerEmployeeId = null,
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.NewManagerId.Should().BeNull();

        await using (var db = CreateDbContext())
        {
            var updated = await db.Employees.FirstAsync(e => e.Id == employee.Id);
            updated.ReportsToEmployeeId.Should().BeNull();
        }
    }

    [Fact]
    public async Task BulkAssignManager_AssignsMultiple_WithPerEmployeeResults()
    {
        var manager = CreateEmployee("Manager", "Bulk");
        var emp1 = CreateEmployee("Emp", "One");
        var emp2 = CreateEmployee("Emp", "Two");
        var emp3 = CreateEmployee("Emp", "Three");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(manager, emp1, emp2, emp3);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.BulkAssignManagerAsync(new BulkAssignManagerRequest
        {
            EmployeeIds = [emp1.Id, emp2.Id, emp3.Id],
            ManagerEmployeeId = manager.Id,
            Reason = "Bulk assignment",
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalRequested.Should().Be(3);
        result.Value.SuccessCount.Should().Be(3);
        result.Value.FailureCount.Should().Be(0);
        result.Value.Results.Should().HaveCount(3);
        result.Value.Results.Should().OnlyContain(r => r.Success);

        // Verify all have the manager set
        await using (var db = CreateDbContext())
        {
            var employees = await db.Employees
                .Where(e => e.ReportsToEmployeeId == manager.Id)
                .ToListAsync();
            employees.Should().HaveCount(3);
        }

        // Verify individual audit entries
        await using (var db = CreateDbContext())
        {
            var history = await db.EmploymentHistories
                .Where(h => h.ChangeType == "manager_change")
                .ToListAsync();
            history.Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task BulkAssignManager_WithSelfAssignment_ReportsPerEmployeeFailure()
    {
        var manager = CreateEmployee("Manager", "B");
        var validEmp = CreateEmployee("Valid", "Emp");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(manager, validEmp);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: Include the manager itself in the list (should fail for self-assignment)
        var result = await service.BulkAssignManagerAsync(new BulkAssignManagerRequest
        {
            EmployeeIds = [validEmp.Id, manager.Id],
            ManagerEmployeeId = manager.Id,
        });

        // Assert
        result.IsSuccess.Should().BeTrue(); // bulk itself succeeds, individual items report failure
        result.Value!.SuccessCount.Should().Be(1);
        result.Value.FailureCount.Should().Be(1);

        var failedItem = result.Value.Results.First(r => !r.Success);
        failedItem.EmployeeId.Should().Be(manager.Id);
        failedItem.Error.Should().Contain("cannot be assigned as their own manager");
    }

    [Fact]
    public async Task DirectReports_TenantIsolation()
    {
        var otherTenantId = Guid.NewGuid();
        var otherTenantContext = Substitute.For<ITenantContext>();
        otherTenantContext.TenantId.Returns(otherTenantId);
        otherTenantContext.IsResolved.Returns(true);
        otherTenantContext.IsSystemContext.Returns(false);

        var manager = CreateEmployee("Manager", "T1");
        var emp1 = CreateEmployee("Emp", "T1");
        emp1.ReportsToEmployeeId = manager.Id;

        // Employee in another tenant with same manager ID (should NOT appear)
        var otherTenantEmp = CreateEmployee("Other", "Tenant");
        otherTenantEmp.TenantId = otherTenantId;
        otherTenantEmp.ReportsToEmployeeId = manager.Id;

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(manager, emp1);
            await db.SaveChangesAsync();
        }

        // Add other-tenant employee separately
        await using (var db = TestDbContextFactory.Create(otherTenantContext, _dbName))
        {
            db.Employees.Add(otherTenantEmp);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.GetDirectReportsAsync(manager.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DirectReports.Should().HaveCount(1);
        result.Value.DirectReports[0].Id.Should().Be(emp1.Id);
    }

    [Fact]
    public async Task DeepChainCycleDetection_10Levels_Detected()
    {
        // Create a 10-level deep chain: E0 -> E1 -> E2 -> ... -> E9
        var employees = Enumerable.Range(0, 10)
            .Select(i => CreateEmployee($"Emp{i}", $"Level{i}"))
            .ToList();

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(employees);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Build chain: E0 reports to E1, E1 to E2, etc.
        for (var i = 0; i < 9; i++)
        {
            var r = await service.AssignManagerAsync(employees[i].Id, new AssignManagerRequest
            {
                ManagerEmployeeId = employees[i + 1].Id,
            });
            r.IsSuccess.Should().BeTrue($"Assignment E{i} -> E{i + 1} should succeed");
        }

        // Act: E9 reports to E0 (would create cycle)
        var result = await service.AssignManagerAsync(employees[9].Id, new AssignManagerRequest
        {
            ManagerEmployeeId = employees[0].Id,
        });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Circular reporting chain detected");
    }

    [Fact]
    public async Task DirectReports_ReturnsEmployees_WhenFKSetBeforeSave()
    {
        // Test that GetDirectReportsAsync works when ReportsToEmployeeId is set before saving
        var manager = CreateEmployee("Manager", "Direct");
        var employee = CreateEmployee("Employee", "Direct");
        employee.ReportsToEmployeeId = manager.Id;

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(manager, employee);
            await db.SaveChangesAsync();
        }

        // Verify raw query on a fresh DbContext
        await using (var db2 = CreateDbContext())
        {
            var allEmps = await db2.Employees.ToListAsync();
            allEmps.Should().HaveCount(2, "both employees should be in the InMemory DB");

            var reportsTo = await db2.Employees
                .Where(e => e.ReportsToEmployeeId == manager.Id)
                .ToListAsync();
            reportsTo.Should().HaveCount(1, "one employee reports to the manager");
        }

        var service = CreateService();
        var result = await service.GetDirectReportsAsync(manager.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DirectReports.Should().ContainSingle(r => r.Id == employee.Id);
    }

    [Fact]
    public async Task AssignManager_NonexistentEmployee_Returns404()
    {
        var manager = CreateEmployee("Manager", "M");

        await using (var db = CreateDbContext())
        {
            db.Employees.Add(manager);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        var result = await service.AssignManagerAsync(Guid.NewGuid(), new AssignManagerRequest
        {
            ManagerEmployeeId = manager.Id,
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("Employee not found");
    }

    [Fact]
    public async Task AssignManager_NonexistentManager_Returns404()
    {
        var employee = CreateEmployee("Emp", "E");

        await using (var db = CreateDbContext())
        {
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        var result = await service.AssignManagerAsync(employee.Id, new AssignManagerRequest
        {
            ManagerEmployeeId = Guid.NewGuid(),
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("Manager employee not found");
    }

    [Fact]
    public async Task AssignManager_TenantNotResolved_Fails()
    {
        var unresolvedTenant = Substitute.For<ITenantContext>();
        unresolvedTenant.IsResolved.Returns(false);

        var service = CreateService(unresolvedTenant);

        var result = await service.AssignManagerAsync(Guid.NewGuid(), new AssignManagerRequest
        {
            ManagerEmployeeId = Guid.NewGuid(),
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    [Fact]
    public async Task AssignManager_ProbationManager_Allowed()
    {
        // BR-3: Probation employees are considered active and can be managers
        var probationManager = CreateEmployee("Probation", "Mgr", EmployeeStatus.Probation);
        var employee = CreateEmployee("Emp", "E");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(probationManager, employee);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        var result = await service.AssignManagerAsync(employee.Id, new AssignManagerRequest
        {
            ManagerEmployeeId = probationManager.Id,
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.NewManagerId.Should().Be(probationManager.Id);
    }

    [Fact]
    public async Task AssignManager_NoChange_WhenAlreadyAssigned()
    {
        var manager = CreateEmployee("Manager", "M");
        var employee = CreateEmployee("Employee", "E");

        await using (var db = CreateDbContext())
        {
            db.Employees.AddRange(manager, employee);
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Assign first
        await service.AssignManagerAsync(employee.Id, new AssignManagerRequest { ManagerEmployeeId = manager.Id });

        // Act: Assign same manager again
        var result = await service.AssignManagerAsync(employee.Id, new AssignManagerRequest { ManagerEmployeeId = manager.Id });

        // Assert: Success but no change
        result.IsSuccess.Should().BeTrue();
        result.Value!.Message.Should().Contain("No change");
    }

    public void Dispose()
    {
        // InMemory database is cleaned up automatically
    }
}
