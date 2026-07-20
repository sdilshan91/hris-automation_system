namespace HRM.Application.Common.Payroll;

/// <summary>
/// Derives + validates the tenant-scoped relative storage path for a payslip PDF (US-PAY-004 FR-5/NFR-6).
/// The path is built ONLY from GUIDs (run id + employee id) — never from user input — so path-traversal is
/// structurally impossible. The defensive assert (no <c>..</c>, no rooted/absolute path) is belt-and-braces
/// in case a caller ever passes a non-GUID; it throws rather than silently returning a traversing path.
///
/// <para>Tenant isolation: the <c>{tenantId}</c> segment of the full blob path
/// (<c>{tenantId}/payroll/{runId}/{employeeId}.pdf</c>) is supplied by <see cref="IFileStorage"/> itself
/// (it prefixes the tenant id), so this returns only the part WITHIN the tenant scope —
/// <c>payroll/{runId}/{employeeId}.pdf</c>. A caller in tenant A can never address tenant B's files.</para>
/// </summary>
public static class PayslipStoragePath
{
    /// <summary>The relative path within a tenant's scope: <c>payroll/{runId}/{employeeId}.pdf</c>.</summary>
    public static string ForSlip(Guid runId, Guid employeeId)
    {
        var path = $"payroll/{runId:D}/{employeeId:D}.pdf";
        AssertSafe(path);
        return path;
    }

    /// <summary>The download file name per BR-5: <c>{EmployeeNo}_{PayMonth}_{PayYear}.pdf</c>.</summary>
    public static string DownloadFileName(string employeeNo, int payMonth, int payYear)
    {
        // EmployeeNo is tenant-controlled config, not free-form user input, but sanitize to a safe file-name
        // charset so a stray separator can never alter a Content-Disposition header or a ZIP entry path.
        var safeNo = Sanitize(employeeNo);
        // ISSUE-163: zero-pad the month so files sort correctly and read consistently (EMP_05_2026, not EMP_5_2026).
        return $"{safeNo}_{payMonth:D2}_{payYear}.pdf";
    }

    /// <summary>NFR-6: rejects any relative path that escapes the tenant scope. Defensive — paths are GUID-derived.</summary>
    public static void AssertSafe(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Storage path must not be empty.", nameof(relativePath));

        if (relativePath.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"Storage path traversal detected: '{relativePath}'.", nameof(relativePath));

        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith('/') || relativePath.StartsWith('\\'))
            throw new ArgumentException($"Storage path must be relative: '{relativePath}'.", nameof(relativePath));
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "payslip";
        var chars = value.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' ? c : '_').ToArray();
        return new string(chars);
    }
}
