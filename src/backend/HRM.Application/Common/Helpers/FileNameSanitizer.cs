using System.IO;

namespace HRM.Application.Common.Helpers;

/// <summary>
/// Shared file-name sanitization for blob/storage keys. Strips any path components
/// (<see cref="Path.GetFileName(string)"/>) and replaces every character invalid for a file name
/// (<see cref="Path.GetInvalidFileNameChars"/>) with <c>_</c>.
///
/// <para>Extracted from four near-identical private <c>SanitizeFileName</c> helpers
/// (<c>EmployeeDocumentService</c>, <c>AssetService</c>, <c>OnboardingChecklistService</c>,
/// <c>PayrollAdjustmentService</c>) that differed ONLY in the fallback used when sanitization leaves an
/// empty/blank name. Each caller passes its own <paramref name="fallback"/> to preserve prior behavior
/// (employee-document = empty, asset = "acknowledgment", onboarding = "attachment", payroll = "document").</para>
/// </summary>
public static class FileNameSanitizer
{
    /// <summary>
    /// Returns <paramref name="fileName"/> reduced to its file-name component with invalid characters
    /// replaced by <c>_</c>. When the result is null/empty/whitespace, returns <paramref name="fallback"/>
    /// (default empty, matching the historical no-fallback behavior).
    /// </summary>
    public static string Sanitize(string fileName, string fallback = "")
    {
        var name = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }
}
