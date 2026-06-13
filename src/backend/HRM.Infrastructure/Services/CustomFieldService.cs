using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Custom field definition CRUD and value validation service (US-CHR-012).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class CustomFieldService : ICustomFieldService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CustomFieldService> _logger;

    /// <summary>
    /// Default maximum number of custom fields per entity type per tenant.
    /// TODO(subscription): Replace with plan-tier lookup (Starter=5, Professional=20, Enterprise=unlimited)
    /// when the Subscription/Plan module is built. For now, this is also overridable via
    /// Tenant.MaxCustomFields (nullable). Null means use this default.
    /// </summary>
    internal const int DefaultMaxCustomFields = 20;

    private static readonly HashSet<string> OptionFieldTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "dropdown", "multi_select"
    };

    public CustomFieldService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<CustomFieldService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<CustomFieldDefinitionListResult>>> GetAllAsync(
        string? entityType = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<CustomFieldDefinitionListResult>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.CustomFieldDefinitions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(c => c.EntityType == entityType);

        var definitions = await query
            .OrderBy(c => c.EntityType)
            .ThenBy(c => c.DisplayOrder)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        // Compute usage counts for employee entity type fields by checking JSONB keys
        var usageCounts = new Dictionary<string, int>();
        if (definitions.Count > 0)
        {
            var employeeFieldKeys = definitions
                .Where(d => d.EntityType == "employee")
                .Select(d => d.FieldKey)
                .ToList();

            if (employeeFieldKeys.Count > 0)
            {
                // Count employees that have each field key in their custom_fields JSONB
                // This is done in C# to stay InMemory-compatible for unit tests.
                var employees = await _dbContext.Employees
                    .Where(e => e.CustomFields != null)
                    .Select(e => e.CustomFields)
                    .ToListAsync(cancellationToken);

                foreach (var fieldKey in employeeFieldKeys)
                {
                    var count = 0;
                    foreach (var customFieldsJson in employees)
                    {
                        if (string.IsNullOrWhiteSpace(customFieldsJson)) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(customFieldsJson);
                            if (doc.RootElement.TryGetProperty(fieldKey, out var val) &&
                                val.ValueKind != JsonValueKind.Null)
                            {
                                count++;
                            }
                        }
                        catch (JsonException)
                        {
                            // Skip malformed JSON
                        }
                    }
                    usageCounts[fieldKey] = count;
                }
            }
        }

        var maxAllowed = await GetMaxCustomFieldsAsync(cancellationToken);

        var grouped = definitions
            .GroupBy(d => d.EntityType)
            .Select(g => new CustomFieldDefinitionListResult
            {
                EntityType = g.Key,
                Fields = g.Select(d => ToDto(d, usageCounts.GetValueOrDefault(d.FieldKey, 0))).ToList(),
                TotalCount = g.Count(),
                MaxAllowed = maxAllowed,
            })
            .ToList();

        return Result<IReadOnlyList<CustomFieldDefinitionListResult>>.Success(grouped);
    }

    public async Task<Result<CustomFieldDefinitionDto>> GetByIdAsync(
        Guid customFieldId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CustomFieldDefinitionDto>.Failure("Tenant context is not resolved.", 400);

        var definition = await _dbContext.CustomFieldDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customFieldId, cancellationToken);

        if (definition is null)
            return Result<CustomFieldDefinitionDto>.Failure("Custom field definition not found.", 404);

        return Result<CustomFieldDefinitionDto>.Success(ToDto(definition, 0));
    }

    public async Task<Result<CustomFieldDefinitionDto>> CreateAsync(
        CreateCustomFieldRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CustomFieldDefinitionDto>.Failure("Tenant context is not resolved.", 400);

        // Plan limit enforcement (FR-6, AC-4, BR-4)
        var maxAllowed = await GetMaxCustomFieldsAsync(cancellationToken);
        var currentCount = await _dbContext.CustomFieldDefinitions
            .CountAsync(c => c.EntityType == request.EntityType, cancellationToken);

        if (currentCount >= maxAllowed)
            return Result<CustomFieldDefinitionDto>.Failure(
                $"You have reached the maximum number of custom fields ({maxAllowed}) for your current plan. Upgrade to add more.", 403);

        // BR-1: field_name unique per tenant + entity_type
        var nameExists = await _dbContext.CustomFieldDefinitions
            .AnyAsync(c => c.EntityType == request.EntityType && c.FieldName == request.FieldName,
                cancellationToken);

        if (nameExists)
            return Result<CustomFieldDefinitionDto>.Failure(
                $"A custom field with name '{request.FieldName}' already exists for entity type '{request.EntityType}'.", 400);

        // Generate or validate field_key
        var fieldKey = string.IsNullOrWhiteSpace(request.FieldKey)
            ? Slugify(request.FieldName)
            : request.FieldKey;

        var keyExists = await _dbContext.CustomFieldDefinitions
            .AnyAsync(c => c.EntityType == request.EntityType && c.FieldKey == fieldKey,
                cancellationToken);

        if (keyExists)
            return Result<CustomFieldDefinitionDto>.Failure(
                $"A custom field with key '{fieldKey}' already exists for entity type '{request.EntityType}'.", 400);

        // Validate options JSON for dropdown/multi_select
        if (OptionFieldTypes.Contains(request.FieldType))
        {
            var optionsValidation = ValidateOptionsJson(request.Options);
            if (optionsValidation.IsFailure)
                return Result<CustomFieldDefinitionDto>.Failure(optionsValidation.Error!, 400);
        }

        // Determine display_order
        var displayOrder = request.DisplayOrder ?? (currentCount + 1);

        var definition = new CustomFieldDefinition
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EntityType = request.EntityType,
            FieldName = request.FieldName,
            FieldKey = fieldKey,
            FieldType = request.FieldType.ToLowerInvariant(),
            IsRequired = request.IsRequired,
            Options = OptionFieldTypes.Contains(request.FieldType) ? request.Options : null,
            DisplayOrder = displayOrder,
            IsActive = true,
            IsDeleted = false,
        };

        _dbContext.CustomFieldDefinitions.Add(definition);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Custom field definition created. Id={CustomFieldId}, FieldName={FieldName}, FieldKey={FieldKey}, EntityType={EntityType}, TenantId={TenantId}",
            definition.Id, definition.FieldName, definition.FieldKey, definition.EntityType, _tenantContext.TenantId);

        return Result<CustomFieldDefinitionDto>.Success(ToDto(definition, 0));
    }

    public async Task<Result<CustomFieldDefinitionDto>> UpdateAsync(
        Guid customFieldId,
        UpdateCustomFieldRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CustomFieldDefinitionDto>.Failure("Tenant context is not resolved.", 400);

        var definition = await _dbContext.CustomFieldDefinitions
            .FirstOrDefaultAsync(c => c.Id == customFieldId, cancellationToken);

        if (definition is null)
            return Result<CustomFieldDefinitionDto>.Failure("Custom field definition not found.", 404);

        // BR-1: field_name unique per tenant + entity_type (excluding self)
        if (request.FieldName != definition.FieldName)
        {
            var nameExists = await _dbContext.CustomFieldDefinitions
                .AnyAsync(c => c.EntityType == definition.EntityType &&
                               c.FieldName == request.FieldName &&
                               c.Id != customFieldId,
                    cancellationToken);

            if (nameExists)
                return Result<CustomFieldDefinitionDto>.Failure(
                    $"A custom field with name '{request.FieldName}' already exists for entity type '{definition.EntityType}'.", 400);
        }

        // BR-6: Cannot remove dropdown options that are in use
        if (OptionFieldTypes.Contains(definition.FieldType) && request.Options is not null)
        {
            var optionsValidation = ValidateOptionsJson(request.Options);
            if (optionsValidation.IsFailure)
                return Result<CustomFieldDefinitionDto>.Failure(optionsValidation.Error!, 400);

            var removedInUseCheck = await CheckRemovedOptionsInUseAsync(
                definition.FieldKey, definition.Options, request.Options, cancellationToken);
            if (removedInUseCheck.IsFailure)
                return Result<CustomFieldDefinitionDto>.Failure(removedInUseCheck.Error!, 400);
        }

        definition.FieldName = request.FieldName;
        definition.IsRequired = request.IsRequired;

        if (OptionFieldTypes.Contains(definition.FieldType) && request.Options is not null)
            definition.Options = request.Options;

        if (request.DisplayOrder.HasValue)
            definition.DisplayOrder = request.DisplayOrder.Value;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Custom field definition updated. Id={CustomFieldId}, FieldName={FieldName}, TenantId={TenantId}",
            definition.Id, definition.FieldName, _tenantContext.TenantId);

        return Result<CustomFieldDefinitionDto>.Success(ToDto(definition, 0));
    }

    public async Task<Result> DeactivateAsync(
        Guid customFieldId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var definition = await _dbContext.CustomFieldDefinitions
            .FirstOrDefaultAsync(c => c.Id == customFieldId, cancellationToken);

        if (definition is null)
            return Result.Failure("Custom field definition not found.", 404);

        if (!definition.IsActive)
            return Result.Failure("Custom field is already deactivated.", 400);

        definition.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Custom field deactivated. Id={CustomFieldId}, FieldKey={FieldKey}, TenantId={TenantId}",
            definition.Id, definition.FieldKey, _tenantContext.TenantId);

        return Result.Success();
    }

    public async Task<Result> ReactivateAsync(
        Guid customFieldId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var definition = await _dbContext.CustomFieldDefinitions
            .FirstOrDefaultAsync(c => c.Id == customFieldId, cancellationToken);

        if (definition is null)
            return Result.Failure("Custom field definition not found.", 404);

        if (definition.IsActive)
            return Result.Failure("Custom field is already active.", 400);

        definition.IsActive = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Custom field reactivated. Id={CustomFieldId}, FieldKey={FieldKey}, TenantId={TenantId}",
            definition.Id, definition.FieldKey, _tenantContext.TenantId);

        return Result.Success();
    }

    public async Task<Result> ReorderAsync(
        string entityType,
        IReadOnlyList<Guid> fieldIds,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var definitions = await _dbContext.CustomFieldDefinitions
            .Where(c => c.EntityType == entityType)
            .ToListAsync(cancellationToken);

        if (fieldIds.Count != definitions.Count)
            return Result.Failure("The number of field IDs must match the number of custom fields for this entity type.", 400);

        var definitionMap = definitions.ToDictionary(d => d.Id);

        for (var i = 0; i < fieldIds.Count; i++)
        {
            if (!definitionMap.TryGetValue(fieldIds[i], out var def))
                return Result.Failure($"Custom field with ID '{fieldIds[i]}' not found.", 400);

            def.DisplayOrder = i + 1;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Custom fields reordered. EntityType={EntityType}, Count={Count}, TenantId={TenantId}",
            entityType, fieldIds.Count, _tenantContext.TenantId);

        return Result.Success();
    }

    /// <summary>
    /// Validates custom field values against active definitions (FR-5, AC-3).
    /// All validation logic is in C# (not raw SQL) so it is unit-testable with InMemory provider.
    /// </summary>
    public async Task<Result> ValidateCustomFieldValuesAsync(
        string entityType,
        string? customFieldsJson,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        // Load active definitions for this entity type
        var activeDefinitions = await _dbContext.CustomFieldDefinitions
            .AsNoTracking()
            .Where(c => c.EntityType == entityType && c.IsActive)
            .ToListAsync(cancellationToken);

        if (activeDefinitions.Count == 0 && string.IsNullOrWhiteSpace(customFieldsJson))
            return Result.Success(); // No definitions, no values -- valid

        // Parse the values JSON
        Dictionary<string, JsonElement>? values = null;
        if (!string.IsNullOrWhiteSpace(customFieldsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(customFieldsJson);
                values = new Dictionary<string, JsonElement>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    values[prop.Name] = prop.Value.Clone();
                }
            }
            catch (JsonException)
            {
                return Result.Failure("Custom fields must be a valid JSON object.", 400);
            }
        }

        values ??= new Dictionary<string, JsonElement>();

        var errors = new List<string>();

        foreach (var def in activeDefinitions)
        {
            var hasValue = values.TryGetValue(def.FieldKey, out var value) &&
                           value.ValueKind != JsonValueKind.Null;

            // Required check (FR-5)
            if (def.IsRequired && !hasValue)
            {
                errors.Add($"Custom field '{def.FieldName}' is required.");
                continue;
            }

            if (!hasValue)
                continue;

            // Type validation
            var typeError = ValidateFieldValue(def, value);
            if (typeError is not null)
                errors.Add(typeError);
        }

        if (errors.Count > 0)
            return Result.Failure(string.Join(" ", errors), 400);

        return Result.Success();
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Validates a single field value against its definition type.
    /// Returns null on success or a descriptive error message.
    /// </summary>
    internal static string? ValidateFieldValue(CustomFieldDefinition def, JsonElement value)
    {
        return def.FieldType switch
        {
            "text" or "textarea" => ValidateStringValue(def, value),
            "number" => ValidateNumberValue(def, value),
            "date" => ValidateDateValue(def, value),
            "dropdown" => ValidateDropdownValue(def, value),
            "multi_select" => ValidateMultiSelectValue(def, value),
            "checkbox" => ValidateCheckboxValue(def, value),
            "email" => ValidateEmailValue(def, value),
            "phone" => ValidatePhoneValue(def, value),
            "url" => ValidateUrlValue(def, value),
            _ => $"Unknown field type '{def.FieldType}' for field '{def.FieldName}'."
        };
    }

    private static string? ValidateStringValue(CustomFieldDefinition def, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return $"Custom field '{def.FieldName}' must be a text value.";
        return null;
    }

    private static string? ValidateNumberValue(CustomFieldDefinition def, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return null;

        // Also accept string that can be parsed as a number
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            return null;

        return $"Custom field '{def.FieldName}' must be a numeric value.";
    }

    private static string? ValidateDateValue(CustomFieldDefinition def, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return $"Custom field '{def.FieldName}' must be a date string (ISO 8601 format).";

        if (!DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            return $"Custom field '{def.FieldName}' has an invalid date format. Expected ISO 8601 (e.g. 2024-01-15).";

        return null;
    }

    private static string? ValidateDropdownValue(CustomFieldDefinition def, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return $"Custom field '{def.FieldName}' must be a string value for dropdown.";

        var selectedValue = value.GetString();
        if (string.IsNullOrEmpty(selectedValue))
            return null;

        var validOptions = ParseOptions(def.Options);
        if (validOptions is not null && !validOptions.Contains(selectedValue))
            return $"Custom field '{def.FieldName}' has an invalid value '{selectedValue}'. Valid options: {string.Join(", ", validOptions)}.";

        return null;
    }

    private static string? ValidateMultiSelectValue(CustomFieldDefinition def, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return $"Custom field '{def.FieldName}' must be an array for multi_select.";

        var validOptions = ParseOptions(def.Options);
        if (validOptions is null)
            return null;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return $"Custom field '{def.FieldName}' multi_select values must be strings.";

            var itemValue = item.GetString();
            if (itemValue is not null && !validOptions.Contains(itemValue))
                return $"Custom field '{def.FieldName}' has an invalid option '{itemValue}'. Valid options: {string.Join(", ", validOptions)}.";
        }

        return null;
    }

    private static string? ValidateCheckboxValue(CustomFieldDefinition def, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return null;

        return $"Custom field '{def.FieldName}' must be a boolean value for checkbox.";
    }

    private static string? ValidateEmailValue(CustomFieldDefinition def, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return $"Custom field '{def.FieldName}' must be a string value for email.";

        var email = value.GetString();
        if (string.IsNullOrWhiteSpace(email))
            return null;

        // Simple email pattern check
        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return $"Custom field '{def.FieldName}' has an invalid email format.";

        return null;
    }

    private static string? ValidatePhoneValue(CustomFieldDefinition def, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return $"Custom field '{def.FieldName}' must be a string value for phone.";

        var phone = value.GetString();
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        // Simple phone pattern: allows digits, spaces, dashes, parens, plus sign
        if (!Regex.IsMatch(phone, @"^[\d\s\-\+\(\)\.]+$"))
            return $"Custom field '{def.FieldName}' has an invalid phone format.";

        return null;
    }

    private static string? ValidateUrlValue(CustomFieldDefinition def, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return $"Custom field '{def.FieldName}' must be a string value for URL.";

        var url = value.GetString();
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return $"Custom field '{def.FieldName}' has an invalid URL format.";

        return null;
    }

    private static HashSet<string>? ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
            return null;

        try
        {
            var options = JsonSerializer.Deserialize<List<string>>(optionsJson);
            return options is not null ? new HashSet<string>(options) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Result ValidateOptionsJson(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
            return Result.Failure("Options JSON is required for dropdown/multi_select field types.");

        try
        {
            var options = JsonSerializer.Deserialize<List<string>>(optionsJson);
            if (options is null || options.Count == 0)
                return Result.Failure("Options must be a non-empty JSON array of strings.");
            return Result.Success();
        }
        catch (JsonException)
        {
            return Result.Failure("Options must be a valid JSON array of strings.");
        }
    }

    /// <summary>
    /// BR-6: Check if any removed dropdown options are currently in use by employee records.
    /// </summary>
    private async Task<Result> CheckRemovedOptionsInUseAsync(
        string fieldKey, string? currentOptionsJson, string? newOptionsJson,
        CancellationToken cancellationToken)
    {
        var currentOptions = ParseOptions(currentOptionsJson);
        var newOptions = ParseOptions(newOptionsJson);

        if (currentOptions is null || newOptions is null)
            return Result.Success();

        var removedOptions = currentOptions.Except(newOptions).ToList();
        if (removedOptions.Count == 0)
            return Result.Success();

        // Check if any employee has a value matching a removed option
        var employees = await _dbContext.Employees
            .Where(e => e.CustomFields != null)
            .Select(e => e.CustomFields)
            .ToListAsync(cancellationToken);

        foreach (var customFieldsJson in employees)
        {
            if (string.IsNullOrWhiteSpace(customFieldsJson)) continue;
            try
            {
                using var doc = JsonDocument.Parse(customFieldsJson);
                if (!doc.RootElement.TryGetProperty(fieldKey, out var val))
                    continue;

                if (val.ValueKind == JsonValueKind.String)
                {
                    var strVal = val.GetString();
                    if (strVal is not null && removedOptions.Contains(strVal))
                        return Result.Failure($"Cannot remove option '{strVal}' because it is in use by existing employee records.");
                }
                else if (val.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in val.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var itemVal = item.GetString();
                            if (itemVal is not null && removedOptions.Contains(itemVal))
                                return Result.Failure($"Cannot remove option '{itemVal}' because it is in use by existing employee records.");
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Skip malformed JSON
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Gets the maximum number of custom fields allowed per entity type for this tenant.
    /// Uses Tenant.MaxCustomFields if set; otherwise the DefaultMaxCustomFields constant.
    /// TODO(subscription): Replace with plan-tier lookup when Subscription module exists.
    /// </summary>
    private async Task<int> GetMaxCustomFieldsAsync(CancellationToken cancellationToken)
    {
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);

        return tenant?.MaxCustomFields ?? DefaultMaxCustomFields;
    }

    /// <summary>
    /// Converts a field name to a URL-safe slug for use as a JSONB key.
    /// Example: "T-Shirt Size" -> "tshirt_size"
    /// </summary>
    internal static string Slugify(string fieldName)
    {
        var slug = fieldName.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s]", "");
        slug = Regex.Replace(slug, @"\s+", "_");
        slug = slug.Trim('_');
        return string.IsNullOrEmpty(slug) ? "field" : slug;
    }

    private static CustomFieldDefinitionDto ToDto(CustomFieldDefinition d, int usageCount) => new()
    {
        Id = d.Id,
        EntityType = d.EntityType,
        FieldName = d.FieldName,
        FieldKey = d.FieldKey,
        FieldType = d.FieldType,
        IsRequired = d.IsRequired,
        Options = d.Options,
        DisplayOrder = d.DisplayOrder,
        IsActive = d.IsActive,
        UsageCount = usageCount,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };
}
