using System.Text.RegularExpressions;

namespace HRM.Domain.ValueObjects;

/// <summary>
/// Value object representing a validated, normalized email address.
/// Emails are always stored and compared in lowercase.
/// </summary>
public sealed partial record Email
{
    public string Value { get; }

    private Email(string value) => Value = value;

    public static Email Create(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty.", nameof(email));

        email = email.Trim().ToLowerInvariant();

        if (email.Length > 150)
            throw new ArgumentException("Email cannot exceed 150 characters.", nameof(email));

        if (!EmailRegex().IsMatch(email))
            throw new ArgumentException("Email format is invalid.", nameof(email));

        return new Email(email);
    }

    /// <summary>
    /// Non-throwing counterpart to <see cref="Create"/> for validation paths that must return a 400 rather than
    /// surface an exception (e.g. tenant-settings writes). Returns false for null/blank, over-length, or
    /// malformed input; on success yields the normalized (trimmed, lower-cased) <see cref="Email"/>.
    /// </summary>
    public static bool TryCreate(string? email, out Email? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 150 || !EmailRegex().IsMatch(normalized))
            return false;

        result = new Email(normalized);
        return true;
    }

    public override string ToString() => Value;

    public static implicit operator string(Email email) => email.Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();
}
