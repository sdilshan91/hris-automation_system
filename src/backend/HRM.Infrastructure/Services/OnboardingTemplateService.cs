using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Onboarding checklist template service (US-ONB-001). All queries are tenant-scoped via ITenantContext
/// + the EF global query filter (AC-5 cross-tenant isolation). The tenant_id is always taken from the
/// resolved session context, never from user input (FR-5). Audit columns (created_by/updated_by, FR-8)
/// are stamped by the AuditInterceptor on every SaveChanges.
/// </summary>
public sealed class OnboardingTemplateService : IOnboardingTemplateService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<OnboardingTemplateService> _logger;

    private const int MaxPageSize = 100;

    public OnboardingTemplateService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<OnboardingTemplateService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<OnboardingTemplateDto>> CreateAsync(
        CreateOnboardingTemplateInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OnboardingTemplateDto>.Failure("Tenant context is not resolved.", 400);

        // BR-2: a template must contain at least one task to be saved (defense in depth — also a validator).
        if (input.Tasks is null || input.Tasks.Count == 0)
            return Result<OnboardingTemplateDto>.Failure(
                "A template must contain at least one task.", 400, "no_tasks");

        var name = input.TemplateName.Trim();

        // AC-3 / BR-1: template name unique per tenant (the global filter scopes the check to this tenant).
        if (await NameExistsAsync(name, null, cancellationToken))
            return Result<OnboardingTemplateDto>.Failure(
                "A checklist template with this name already exists.", 409, "duplicate_name");

        var refCheck = await ValidateReferencesAsync(input.ApplicableDepartments, input.ApplicableJobTitles, input.Tasks, cancellationToken);
        if (refCheck is not null)
            return Result<OnboardingTemplateDto>.Failure(refCheck.Value.error, 400, refCheck.Value.code);

        var template = new OnboardingChecklistTemplate
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId, // FR-5: from session, never user input.
            TemplateName = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            ApplicableDepartments = (input.ApplicableDepartments ?? []).Distinct().ToList(),
            ApplicableJobTitles = (input.ApplicableJobTitles ?? []).Distinct().ToList(),
            IsActive = input.IsActive,
            IsDeleted = false,
            Tasks = input.Tasks.Select(t => NewTask(t)).ToList(),
        };

        _dbContext.OnboardingChecklistTemplates.Add(template);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Onboarding template created. Id={TemplateId}, Tasks={TaskCount}, TenantId={TenantId}, By={User}",
            template.Id, template.Tasks.Count, _tenantContext.TenantId, _currentUser.Email);

        return Result<OnboardingTemplateDto>.Success(ToDto(template));
    }

    public async Task<Result<OnboardingTemplateDto>> CloneAsync(
        Guid templateId, string? newName, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OnboardingTemplateDto>.Failure("Tenant context is not resolved.", 400);

        var source = await _dbContext.OnboardingChecklistTemplates
            .Include(t => t.Tasks)
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (source is null)
            return Result<OnboardingTemplateDto>.Failure("Template not found.", 404);

        var name = string.IsNullOrWhiteSpace(newName)
            ? await GenerateCopyNameAsync(source.TemplateName, cancellationToken)
            : newName.Trim();

        if (name.Length < 3)
            return Result<OnboardingTemplateDto>.Failure(
                "Template name must be at least 3 characters.", 400, "invalid_name");
        if (name.Length > 200)
            return Result<OnboardingTemplateDto>.Failure(
                "Template name cannot exceed 200 characters.", 400, "invalid_name");

        if (await NameExistsAsync(name, null, cancellationToken))
            return Result<OnboardingTemplateDto>.Failure(
                "A checklist template with this name already exists.", 409, "duplicate_name");

        var clone = new OnboardingChecklistTemplate
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId, // FR-5
            TemplateName = name,
            Description = source.Description,
            ApplicableDepartments = source.ApplicableDepartments.ToList(),
            ApplicableJobTitles = source.ApplicableJobTitles.ToList(),
            IsActive = source.IsActive,
            IsDeleted = false,
            Tasks = source.Tasks
                .OrderBy(t => t.SortOrder)
                .Select(t => NewTask(new OnboardingTaskInput(
                    t.Title, t.Description, t.Category, t.ResponsibleRole, t.ResponsibleUserId,
                    t.DueOffsetDays, t.IsMandatory, t.SortOrder)))
                .ToList(),
        };

        _dbContext.OnboardingChecklistTemplates.Add(clone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Onboarding template cloned. SourceId={SourceId}, NewId={NewId}, Tasks={TaskCount}, TenantId={TenantId}, By={User}",
            source.Id, clone.Id, clone.Tasks.Count, _tenantContext.TenantId, _currentUser.Email);

        return Result<OnboardingTemplateDto>.Success(ToDto(clone));
    }

    public Task<Result<OnboardingTemplateDto>> ActivateAsync(Guid templateId, CancellationToken cancellationToken = default)
        => SetActiveAsync(templateId, true, cancellationToken);

    public Task<Result<OnboardingTemplateDto>> DeactivateAsync(Guid templateId, CancellationToken cancellationToken = default)
        => SetActiveAsync(templateId, false, cancellationToken);

    private async Task<Result<OnboardingTemplateDto>> SetActiveAsync(
        Guid templateId, bool isActive, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<OnboardingTemplateDto>.Failure("Tenant context is not resolved.", 400);

        var template = await _dbContext.OnboardingChecklistTemplates
            .Include(t => t.Tasks)
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template is null)
            return Result<OnboardingTemplateDto>.Failure("Template not found.", 404);

        if (template.IsActive == isActive)
            return Result<OnboardingTemplateDto>.Failure(
                $"Template is already {(isActive ? "active" : "inactive")}.", 409, "no_op");

        template.IsActive = isActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Onboarding template {Action}. Id={TemplateId}, TenantId={TenantId}, By={User}",
            isActive ? "activated" : "deactivated", template.Id, _tenantContext.TenantId, _currentUser.Email);

        return Result<OnboardingTemplateDto>.Success(ToDto(template));
    }

    public async Task<Result<OnboardingTemplateDto>> GetByIdAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OnboardingTemplateDto>.Failure("Tenant context is not resolved.", 400);

        var template = await _dbContext.OnboardingChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Tasks)
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template is null)
            return Result<OnboardingTemplateDto>.Failure("Template not found.", 404);

        return Result<OnboardingTemplateDto>.Success(ToDto(template));
    }

    public async Task<Result<PagedResult<OnboardingTemplateListItemDto>>> ListAsync(
        bool? isActive, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PagedResult<OnboardingTemplateListItemDto>>.Failure("Tenant context is not resolved.", 400);

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? Math.Clamp(pageSize, 1, MaxPageSize) : pageSize;

        var query = _dbContext.OnboardingChecklistTemplates.AsNoTracking();
        if (isActive.HasValue)
            query = query.Where(t => t.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t => t.TemplateName.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var templates = await query
            .Include(t => t.Tasks)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = templates.Select(t => new OnboardingTemplateListItemDto
        {
            Id = t.Id,
            TemplateName = t.TemplateName,
            Description = t.Description,
            IsActive = t.IsActive,
            IsUniversal = t.ApplicableDepartments.Count == 0 && t.ApplicableJobTitles.Count == 0,
            TaskCount = t.Tasks.Count,
            MandatoryTaskCount = t.Tasks.Count(x => x.IsMandatory),
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
        }).ToList();

        return Result<PagedResult<OnboardingTemplateListItemDto>>.Success(new PagedResult<OnboardingTemplateListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private OnboardingTemplateTask NewTask(OnboardingTaskInput t) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantContext.TenantId, // FR-5
        Title = t.Title.Trim(),
        Description = string.IsNullOrWhiteSpace(t.Description) ? null : t.Description.Trim(),
        Category = string.IsNullOrWhiteSpace(t.Category) ? null : t.Category.Trim(),
        ResponsibleRole = t.ResponsibleRole,
        ResponsibleUserId = t.ResponsibleUserId,
        DueOffsetDays = t.DueOffsetDays,
        IsMandatory = t.IsMandatory,
        SortOrder = t.SortOrder,
        IsDeleted = false,
    };

    private async Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        // The global query filter scopes this to the current tenant + excludes soft-deleted (BR-1/BR-5).
        var q = _dbContext.OnboardingChecklistTemplates
            .Where(t => t.TemplateName.ToLower() == name.ToLower());
        if (excludeId.HasValue)
            q = q.Where(t => t.Id != excludeId.Value);
        return await q.AnyAsync(cancellationToken);
    }

    private async Task<string> GenerateCopyNameAsync(string baseName, CancellationToken cancellationToken)
    {
        // "(Copy)" appended, then "(Copy 2)", "(Copy 3)" ... to stay unique per tenant. Trim to 200.
        static string Clamp(string s) => s.Length <= 200 ? s : s[..200];

        var candidate = Clamp($"{baseName} (Copy)");
        if (!await NameExistsAsync(candidate, null, cancellationToken))
            return candidate;

        for (var i = 2; ; i++)
        {
            candidate = Clamp($"{baseName} (Copy {i})");
            if (!await NameExistsAsync(candidate, null, cancellationToken))
                return candidate;
        }
    }

    private async Task<(string error, string code)?> ValidateReferencesAsync(
        IReadOnlyList<Guid>? departmentIds,
        IReadOnlyList<Guid>? jobTitleIds,
        IReadOnlyList<OnboardingTaskInput> tasks,
        CancellationToken cancellationToken)
    {
        // Department scoping (FR-4) — each id must exist in this tenant (global filter ⇒ found = in-tenant).
        var deptIds = (departmentIds ?? []).Distinct().ToList();
        if (deptIds.Count > 0)
        {
            var foundCount = await _dbContext.Departments.CountAsync(d => deptIds.Contains(d.Id), cancellationToken);
            if (foundCount != deptIds.Count)
                return ("One or more applicable departments do not exist.", "department_not_found");
        }

        // Job-title scoping (FR-4).
        var jtIds = (jobTitleIds ?? []).Distinct().ToList();
        if (jtIds.Count > 0)
        {
            var foundCount = await _dbContext.JobTitles.CountAsync(j => jtIds.Contains(j.Id), cancellationToken);
            if (foundCount != jtIds.Count)
                return ("One or more applicable job titles do not exist.", "job_title_not_found");
        }

        // Named responsible users (FR-3) — must exist in this tenant.
        var userIds = tasks.Where(t => t.ResponsibleUserId.HasValue)
            .Select(t => t.ResponsibleUserId!.Value).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var foundCount = await _dbContext.Employees.CountAsync(e => userIds.Contains(e.Id), cancellationToken);
            if (foundCount != userIds.Count)
                return ("One or more responsible users do not exist.", "responsible_user_not_found");
        }

        return null;
    }

    private static OnboardingTemplateDto ToDto(OnboardingChecklistTemplate t) => new()
    {
        Id = t.Id,
        TemplateName = t.TemplateName,
        Description = t.Description,
        ApplicableDepartments = t.ApplicableDepartments.ToList(),
        ApplicableJobTitles = t.ApplicableJobTitles.ToList(),
        IsActive = t.IsActive,
        IsUniversal = t.ApplicableDepartments.Count == 0 && t.ApplicableJobTitles.Count == 0,
        TaskCount = t.Tasks.Count,
        Tasks = t.Tasks
            .OrderBy(x => x.SortOrder)
            .Select(x => new OnboardingTemplateTaskDto
            {
                Id = x.Id,
                Title = x.Title,
                Description = x.Description,
                Category = x.Category,
                ResponsibleRole = x.ResponsibleRole,
                ResponsibleRoleName = x.ResponsibleRole.ToString(),
                ResponsibleUserId = x.ResponsibleUserId,
                DueOffsetDays = x.DueOffsetDays,
                IsMandatory = x.IsMandatory,
                SortOrder = x.SortOrder,
            }).ToList(),
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
    };
}
