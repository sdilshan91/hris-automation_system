namespace HRM.Application.Common.Security;

/// <summary>
/// Pure password-history reuse check (US-AUTH-004 FR-5 / ISSUE-053). Compares a candidate password against a
/// set of previously-used BCrypt hashes. Kept dependency-light and static so it is unit-testable in isolation;
/// the hash verification delegate is injected so callers supply <c>BCrypt.Verify</c>.
/// </summary>
public static class PasswordHistoryValidator
{
    /// <summary>
    /// True iff <paramref name="candidate"/> matches any of <paramref name="priorHashes"/> according to
    /// <paramref name="verify"/> (typically <c>BCrypt.Verify</c>). Null/empty hashes are ignored.
    /// </summary>
    public static bool IsReused(string candidate, IEnumerable<string?> priorHashes, Func<string, string, bool> verify)
    {
        foreach (var hash in priorHashes)
        {
            if (!string.IsNullOrEmpty(hash) && verify(candidate, hash))
                return true;
        }

        return false;
    }
}
