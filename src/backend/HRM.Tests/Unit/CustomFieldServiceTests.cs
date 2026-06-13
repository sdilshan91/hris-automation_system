// ============================================================================
// US-CHR-012: Custom Fields per Tenant — Unit Tests
// Tests: create definition with usage count (AC-1, AC-2), field_name uniqueness
// per tenant+entity (BR-1), plan-limit block (AC-4, FR-6, BR-4), type validation
// (number rejects "abc", required rejects missing, dropdown rejects out-of-options)
// (FR-5, AC-3), store and retrieve employee custom_fields values, deactivate
// hides but preserves JSONB / reactivate restores (FR-7, AC-5, BR-3), reorder
// (FR-8), tenant isolation, slugify, and removed-option-in-use guard (BR-6).
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.CustomFields.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class CustomFieldServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CustomFieldService> _logger;

    public CustomFieldServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Permissions.Returns(new List<string> { "CustomField.View", "CustomField.Create", "CustomField.Edit", "CustomField.Deactivate" });

        _logger = Substitute.For<ILogger<CustomFieldService>>();
    }

    private CustomFieldService CreateService(ITenantContext? ctx = null)
    {
        var dbContext = TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);
        return new CustomFieldService(dbContext, ctx ?? _tenantContext, _currentUser, _logger);
    }

    private Infrastructure.Persistence.AppDbContext CreateDbContext(ITenantContext? ctx = null)
    {
        return TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);
    }

    private async Task SeedTenant(Guid? tenantId = null, int? maxCustomFields = null)
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

        await using var db = CreateDbContext(ctx);
        db.Tenants.Add(new Tenant
        {
            Id = tid,
            Subdomain = $"test-{tid.ToString()[..8]}",
            Name = "Test Tenant",
            MaxCustomFields = maxCustomFields,
        });
        await db.SaveChangesAsync();
    }

    private async Task<(Guid DeptId, Guid JtId)> SeedDepartmentAndJobTitle(Guid? tenantId = null)
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

        await using var db = CreateDbContext(ctx);
        var dept = new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            Name = "Engineering",
            Code = "ENG",
            IsActive = true,
        };
        var jt = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            TitleName = "Developer",
            IsActive = true,
        };
        db.Departments.Add(dept);
        db.JobTitles.Add(jt);
        await db.SaveChangesAsync();
        return (dept.Id, jt.Id);
    }

    public void Dispose() { }

    // ── Create tests ─────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CreatesDefinition_WithSlugifiedKey()
    {
        await SeedTenant();
        var svc = CreateService();

        var result = await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "T-Shirt Size",
            FieldType = "dropdown",
            Options = "[\"S\",\"M\",\"L\",\"XL\"]",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.FieldName.Should().Be("T-Shirt Size");
        result.Value.FieldKey.Should().Be("tshirt_size");
        result.Value.FieldType.Should().Be("dropdown");
        result.Value.Options.Should().Be("[\"S\",\"M\",\"L\",\"XL\"]");
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_UsesExplicitFieldKey_WhenProvided()
    {
        await SeedTenant();
        var svc = CreateService();

        var result = await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Project Code",
            FieldKey = "project_code",
            FieldType = "text",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.FieldKey.Should().Be("project_code");
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateFieldName_SameTenantAndEntity()
    {
        await SeedTenant();
        var svc = CreateService();

        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Badge Number",
            FieldType = "text",
        });

        var svc2 = CreateService();
        var result = await svc2.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Badge Number",
            FieldType = "text",
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateAsync_BlocksWhenPlanLimitReached()
    {
        // Set max to 2
        await SeedTenant(maxCustomFields: 2);
        var svc = CreateService();

        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee", FieldName = "Field1", FieldType = "text",
        });
        var svc2 = CreateService();
        await svc2.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee", FieldName = "Field2", FieldType = "text",
        });

        var svc3 = CreateService();
        var result = await svc3.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee", FieldName = "Field3", FieldType = "text",
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("maximum number of custom fields (2)");
        result.Error.Should().Contain("Upgrade");
    }

    [Fact]
    public async Task CreateAsync_UsesDefaultLimit_WhenTenantMaxCustomFieldsIsNull()
    {
        await SeedTenant(maxCustomFields: null); // null -> uses default (20)
        var svc = CreateService();

        // Create one field should succeed
        var result = await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee", FieldName = "Field1", FieldType = "text",
        });

        result.IsSuccess.Should().BeTrue();
    }

    // ── Validation tests ─────────────────────────────────────────

    [Fact]
    public async Task ValidateCustomFieldValues_RejectsNonNumericForNumberField()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Age",
            FieldType = "number",
            IsRequired = false,
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"age\": \"abc\"}");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("must be a numeric value");
    }

    [Fact]
    public async Task ValidateCustomFieldValues_AcceptsValidNumber()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Age",
            FieldType = "number",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"age\": 25}");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCustomFieldValues_RejectsMissingRequiredField()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Badge Number",
            FieldType = "text",
            IsRequired = true,
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{}");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("'Badge Number' is required");
    }

    [Fact]
    public async Task ValidateCustomFieldValues_RejectsDropdownOutOfOptions()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "T-Shirt Size",
            FieldType = "dropdown",
            Options = "[\"S\",\"M\",\"L\",\"XL\"]",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"tshirt_size\": \"XXL\"}");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("invalid value 'XXL'");
    }

    [Fact]
    public async Task ValidateCustomFieldValues_AcceptsValidDropdownValue()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "T-Shirt Size",
            FieldType = "dropdown",
            Options = "[\"S\",\"M\",\"L\",\"XL\"]",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"tshirt_size\": \"L\"}");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCustomFieldValues_RejectsInvalidMultiSelect()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Skills",
            FieldType = "multi_select",
            Options = "[\"Java\",\"C#\",\"Python\"]",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"skills\": [\"Java\", \"Ruby\"]}");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("invalid option 'Ruby'");
    }

    [Fact]
    public async Task ValidateCustomFieldValues_AcceptsValidCheckbox()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Union Member",
            FieldType = "checkbox",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"union_member\": true}");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCustomFieldValues_RejectsNonBoolForCheckbox()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Union Member",
            FieldType = "checkbox",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"union_member\": \"yes\"}");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("must be a boolean value");
    }

    [Fact]
    public async Task ValidateCustomFieldValues_RejectsInvalidEmail()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Personal Email",
            FieldType = "email",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"personal_email\": \"not-an-email\"}");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("invalid email format");
    }

    [Fact]
    public async Task ValidateCustomFieldValues_RejectsInvalidUrl()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Portfolio",
            FieldType = "url",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"portfolio\": \"not-a-url\"}");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("invalid URL format");
    }

    [Fact]
    public async Task ValidateCustomFieldValues_AcceptsValidDate()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Start Date",
            FieldType = "date",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"start_date\": \"2024-01-15\"}");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCustomFieldValues_RejectsInvalidDate()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Start Date",
            FieldType = "date",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"start_date\": \"not-a-date\"}");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("invalid date format");
    }

    [Fact]
    public async Task ValidateCustomFieldValues_AcceptsValidPhone()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Emergency Phone",
            FieldType = "phone",
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"emergency_phone\": \"+1 (555) 123-4567\"}");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCustomFieldValues_AcceptsNullForOptionalField()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Nickname",
            FieldType = "text",
            IsRequired = false,
        });

        var svc2 = CreateService();
        var result = await svc2.ValidateCustomFieldValuesAsync(
            "employee", "{\"nickname\": null}");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCustomFieldValues_RejectsInvalidJson()
    {
        await SeedTenant();
        var svc = CreateService();

        var result = await svc.ValidateCustomFieldValuesAsync(
            "employee", "not-json");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("valid JSON object");
    }

    // ── Store and retrieve custom field values ─────────────────────

    [Fact]
    public async Task EmployeeCustomFields_StoreAndRetrieve_ViaJsonb()
    {
        await SeedTenant();
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();

        // Create a custom field definition first
        var cfSvc = CreateService();
        await cfSvc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "T-Shirt Size",
            FieldType = "dropdown",
            Options = "[\"S\",\"M\",\"L\",\"XL\"]",
        });

        // Create an employee with custom field values
        var customFieldsJson = "{\"tshirt_size\": \"L\"}";
        await using var db = CreateDbContext();
        var employee = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeNo = "EMP-0001",
            FirstName = "John",
            LastName = "Doe",
            Email = "john@test.com",
            DateOfJoining = DateTime.UtcNow,
            DepartmentId = deptId,
            JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
            CustomFields = customFieldsJson,
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        // Retrieve and verify
        await using var db2 = CreateDbContext();
        var loaded = await db2.Employees.FindAsync(employee.Id);
        loaded.Should().NotBeNull();
        loaded!.CustomFields.Should().Be(customFieldsJson);

        using var doc = JsonDocument.Parse(loaded.CustomFields!);
        doc.RootElement.GetProperty("tshirt_size").GetString().Should().Be("L");
    }

    // ── Deactivate / Reactivate tests ───────────────────────────

    [Fact]
    public async Task DeactivateAsync_HidesFieldButPreservesData()
    {
        await SeedTenant();
        var svc = CreateService();
        var created = await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Badge Number",
            FieldType = "text",
        });

        var svc2 = CreateService();
        var result = await svc2.DeactivateAsync(created.Value!.Id);

        result.IsSuccess.Should().BeTrue();

        // Verify the field is now inactive
        var svc3 = CreateService();
        var fetched = await svc3.GetByIdAsync(created.Value.Id);
        fetched.IsSuccess.Should().BeTrue();
        fetched.Value!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ReactivateAsync_RestoresVisibility()
    {
        await SeedTenant();
        var svc = CreateService();
        var created = await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Badge Number",
            FieldType = "text",
        });

        var svc2 = CreateService();
        await svc2.DeactivateAsync(created.Value!.Id);

        var svc3 = CreateService();
        var result = await svc3.ReactivateAsync(created.Value.Id);
        result.IsSuccess.Should().BeTrue();

        var svc4 = CreateService();
        var fetched = await svc4.GetByIdAsync(created.Value.Id);
        fetched.IsSuccess.Should().BeTrue();
        fetched.Value!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DeactivateAsync_RejectsAlreadyInactive()
    {
        await SeedTenant();
        var svc = CreateService();
        var created = await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Badge Number",
            FieldType = "text",
        });

        var svc2 = CreateService();
        await svc2.DeactivateAsync(created.Value!.Id);

        var svc3 = CreateService();
        var result = await svc3.DeactivateAsync(created.Value.Id);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already deactivated");
    }

    [Fact]
    public async Task DeactivateAsync_PreservesJsonbDataOnEmployees()
    {
        await SeedTenant();
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();

        // Create custom field and employee with value
        var cfSvc = CreateService();
        var created = await cfSvc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "T-Shirt Size",
            FieldType = "dropdown",
            Options = "[\"S\",\"M\",\"L\"]",
        });

        await using var db = CreateDbContext();
        db.Employees.Add(new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeNo = "EMP-0001",
            FirstName = "John",
            LastName = "Doe",
            Email = "john@test.com",
            DateOfJoining = DateTime.UtcNow,
            DepartmentId = deptId,
            JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
            CustomFields = "{\"tshirt_size\": \"L\"}",
        });
        await db.SaveChangesAsync();

        // Deactivate the field
        var svc2 = CreateService();
        await svc2.DeactivateAsync(created.Value!.Id);

        // Employee's JSONB data should still be there
        await using var db2 = CreateDbContext();
        var emp = await db2.Employees.FirstAsync();
        emp.CustomFields.Should().Contain("tshirt_size");
        emp.CustomFields.Should().Contain("L");
    }

    // ── Reorder tests ───────────────────────────────────────────

    [Fact]
    public async Task ReorderAsync_UpdatesDisplayOrder()
    {
        await SeedTenant();
        var svc = CreateService();

        var f1 = await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee", FieldName = "Field1", FieldType = "text",
        });
        var svc2 = CreateService();
        var f2 = await svc2.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee", FieldName = "Field2", FieldType = "text",
        });
        var svc3 = CreateService();
        var f3 = await svc3.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee", FieldName = "Field3", FieldType = "text",
        });

        // Reorder: 3, 1, 2
        var svc4 = CreateService();
        var result = await svc4.ReorderAsync("employee", new List<Guid>
        {
            f3.Value!.Id, f1.Value!.Id, f2.Value!.Id
        });

        result.IsSuccess.Should().BeTrue();

        // Verify order
        var svc5 = CreateService();
        var allResult = await svc5.GetAllAsync("employee");
        allResult.IsSuccess.Should().BeTrue();
        var fields = allResult.Value![0].Fields;
        fields[0].FieldName.Should().Be("Field3");
        fields[0].DisplayOrder.Should().Be(1);
        fields[1].FieldName.Should().Be("Field1");
        fields[1].DisplayOrder.Should().Be(2);
        fields[2].FieldName.Should().Be("Field2");
        fields[2].DisplayOrder.Should().Be(3);
    }

    [Fact]
    public async Task ReorderAsync_RejectsWrongCount()
    {
        await SeedTenant();
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee", FieldName = "Field1", FieldType = "text",
        });

        var svc2 = CreateService();
        var result = await svc2.ReorderAsync("employee", new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("number of field IDs must match");
    }

    // ── Tenant isolation tests ──────────────────────────────────

    [Fact]
    public async Task TenantIsolation_FieldsNotVisibleAcrossTenants()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await SeedTenant(tenantAId);
        await SeedTenant(tenantBId);

        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantAId);
        ctxA.IsResolved.Returns(true);

        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantBId);
        ctxB.IsResolved.Returns(true);

        // Create field in tenant A
        var svcA = CreateService(ctxA);
        var resultA = await svcA.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Tenant A Field",
            FieldType = "text",
        });
        resultA.IsSuccess.Should().BeTrue();

        // Tenant B should not see it
        var svcB = CreateService(ctxB);
        var allB = await svcB.GetAllAsync("employee");
        allB.IsSuccess.Should().BeTrue();
        // No groups for tenant B (no fields exist)
        allB.Value!.Count.Should().Be(0);
    }

    [Fact]
    public async Task TenantIsolation_SameFieldNameAllowedInDifferentTenants()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await SeedTenant(tenantAId);
        await SeedTenant(tenantBId);

        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantAId);
        ctxA.IsResolved.Returns(true);

        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantBId);
        ctxB.IsResolved.Returns(true);

        var svcA = CreateService(ctxA);
        var rA = await svcA.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Badge Number",
            FieldType = "text",
        });
        rA.IsSuccess.Should().BeTrue();

        var svcB = CreateService(ctxB);
        var rB = await svcB.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Badge Number",
            FieldType = "text",
        });
        rB.IsSuccess.Should().BeTrue();
    }

    // ── GetAll with usage count tests ────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsUsageCount()
    {
        await SeedTenant();
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();

        // Create custom field
        var svc = CreateService();
        await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "T-Shirt Size",
            FieldType = "dropdown",
            Options = "[\"S\",\"M\",\"L\",\"XL\"]",
        });

        // Create employees with and without the custom field value
        await using var db = CreateDbContext();
        db.Employees.Add(new Employee
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeNo = "EMP-0001",
            FirstName = "A", LastName = "A", Email = "a@test.com",
            DateOfJoining = DateTime.UtcNow, DepartmentId = deptId, JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active,
            IsActive = true, CustomFields = "{\"tshirt_size\": \"L\"}",
        });
        db.Employees.Add(new Employee
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeNo = "EMP-0002",
            FirstName = "B", LastName = "B", Email = "b@test.com",
            DateOfJoining = DateTime.UtcNow, DepartmentId = deptId, JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active,
            IsActive = true, CustomFields = "{\"tshirt_size\": \"M\"}",
        });
        db.Employees.Add(new Employee
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeNo = "EMP-0003",
            FirstName = "C", LastName = "C", Email = "c@test.com",
            DateOfJoining = DateTime.UtcNow, DepartmentId = deptId, JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active,
            IsActive = true, CustomFields = null, // no custom fields
        });
        await db.SaveChangesAsync();

        var svc2 = CreateService();
        var result = await svc2.GetAllAsync("employee");
        result.IsSuccess.Should().BeTrue();
        result.Value![0].Fields[0].UsageCount.Should().Be(2);
    }

    // ── Update tests ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UpdatesFieldName()
    {
        await SeedTenant();
        var svc = CreateService();
        var created = await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee", FieldName = "Old Name", FieldType = "text",
        });

        var svc2 = CreateService();
        var result = await svc2.UpdateAsync(created.Value!.Id, new UpdateCustomFieldRequest
        {
            FieldName = "New Name",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.FieldName.Should().Be("New Name");
        // FieldKey should remain unchanged (immutable)
        result.Value.FieldKey.Should().Be("old_name");
    }

    [Fact]
    public async Task UpdateAsync_BlocksRemovingOptionInUse()
    {
        await SeedTenant();
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();

        var svc = CreateService();
        var created = await svc.CreateAsync(new CreateCustomFieldRequest
        {
            EntityType = "employee",
            FieldName = "Size",
            FieldType = "dropdown",
            Options = "[\"S\",\"M\",\"L\"]",
        });

        // Create employee using "L"
        await using var db = CreateDbContext();
        db.Employees.Add(new Employee
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeNo = "EMP-0001",
            FirstName = "Test", LastName = "User", Email = "test@test.com",
            DateOfJoining = DateTime.UtcNow, DepartmentId = deptId, JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active,
            IsActive = true, CustomFields = "{\"size\": \"L\"}",
        });
        await db.SaveChangesAsync();

        // Try to remove "L" from options
        var svc2 = CreateService();
        var result = await svc2.UpdateAsync(created.Value!.Id, new UpdateCustomFieldRequest
        {
            FieldName = "Size",
            Options = "[\"S\",\"M\"]", // "L" removed
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Cannot remove option 'L'");
        result.Error.Should().Contain("in use");
    }

    // ── Slugify tests ───────────────────────────────────────────

    [Theory]
    [InlineData("T-Shirt Size", "tshirt_size")]
    [InlineData("Project Code", "project_code")]
    [InlineData("Union Membership", "union_membership")]
    [InlineData("employee ID", "employee_id")]
    [InlineData("  Spaces  ", "spaces")]
    public void Slugify_ProducesExpectedKeys(string input, string expected)
    {
        var result = CustomFieldService.Slugify(input);
        result.Should().Be(expected);
    }

    // ── ValidateFieldValue static tests ─────────────────────────

    [Fact]
    public void ValidateFieldValue_NumberRejectsString()
    {
        var def = new CustomFieldDefinition
        {
            FieldName = "Age", FieldKey = "age", FieldType = "number",
        };

        using var doc = JsonDocument.Parse("\"abc\"");
        var result = CustomFieldService.ValidateFieldValue(def, doc.RootElement);
        result.Should().Contain("must be a numeric value");
    }

    [Fact]
    public void ValidateFieldValue_NumberAcceptsStringRepresentation()
    {
        var def = new CustomFieldDefinition
        {
            FieldName = "Score", FieldKey = "score", FieldType = "number",
        };

        using var doc = JsonDocument.Parse("\"42.5\"");
        var result = CustomFieldService.ValidateFieldValue(def, doc.RootElement);
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateFieldValue_CheckboxRejectsString()
    {
        var def = new CustomFieldDefinition
        {
            FieldName = "Active", FieldKey = "active", FieldType = "checkbox",
        };

        using var doc = JsonDocument.Parse("\"yes\"");
        var result = CustomFieldService.ValidateFieldValue(def, doc.RootElement);
        result.Should().Contain("must be a boolean value");
    }

    [Fact]
    public void ValidateFieldValue_UrlRejectsNonHttp()
    {
        var def = new CustomFieldDefinition
        {
            FieldName = "Link", FieldKey = "link", FieldType = "url",
        };

        using var doc = JsonDocument.Parse("\"ftp://example.com\"");
        var result = CustomFieldService.ValidateFieldValue(def, doc.RootElement);
        result.Should().Contain("invalid URL format");
    }

    [Fact]
    public void ValidateFieldValue_UrlAcceptsHttps()
    {
        var def = new CustomFieldDefinition
        {
            FieldName = "Link", FieldKey = "link", FieldType = "url",
        };

        using var doc = JsonDocument.Parse("\"https://example.com\"");
        var result = CustomFieldService.ValidateFieldValue(def, doc.RootElement);
        result.Should().BeNull();
    }
}
