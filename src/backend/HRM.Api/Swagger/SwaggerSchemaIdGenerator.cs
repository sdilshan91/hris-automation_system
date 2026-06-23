namespace HRM.Api.Swagger;

/// <summary>
/// Generates unique Swagger schema ids.
///
/// Swashbuckle's default id is the bare type name, which collides when two feature
/// modules expose a DTO of the same name (e.g. <c>Onboarding.DTOs.TrendPointDto</c> vs
/// <c>Attendance.DTOs.TrendPointDto</c>) — that collision 500s <c>swagger.json</c>.
///
/// Overriding <c>CustomSchemaIds</c> with <c>type.FullName</c> would "fix" the collision
/// but breaks generic types: once overridden, Swashbuckle no longer applies its own
/// generic naming, so <c>ApiResponse&lt;T&gt;</c> would render as a raw, assembly-qualified
/// mess. This generator keeps readable, short ids AND stays unique by:
///   1. prefixing the feature segment for types under <c>HRM.Application.Features.*</c>
///      (so <c>OnboardingTrendPointDto</c> / <c>AttendanceTrendPointDto</c>), and
///   2. recursively flattening generics into <c>ApiResponseOfOnboardingExitInterviewAnalyticsDto</c>.
/// </summary>
public static class SwaggerSchemaIdGenerator
{
    public static string GetSchemaId(Type type)
    {
        if (type.IsGenericType)
        {
            var name = type.Name;
            var baseName = name[..name.IndexOf('`')];
            var args = string.Join("And", type.GetGenericArguments().Select(GetSchemaId));
            return $"{baseName}Of{args}";
        }

        return FeaturePrefix(type) + type.Name;
    }

    // For HRM.Application.Features.{Feature}.* types, return "{Feature}" to disambiguate
    // same-named DTOs across features. Everything else (shared wrappers, primitives) is
    // left unprefixed because those names are already unique.
    private static string FeaturePrefix(Type type)
    {
        var ns = type.Namespace;
        if (ns is null)
        {
            return string.Empty;
        }

        const string marker = ".Features.";
        var markerIndex = ns.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return string.Empty;
        }

        var afterMarker = ns[(markerIndex + marker.Length)..];
        var dotIndex = afterMarker.IndexOf('.');
        return dotIndex < 0 ? afterMarker : afterMarker[..dotIndex];
    }
}
