namespace HRM.Application.Common.Security;

/// <summary>
/// ISSUE-203 — the BCrypt cost factor, made configurable instead of a literal scattered across three files.
///
/// <para><b>Why this exists.</b> Login is CPU-bound on exactly ONE BCrypt verify (the two "dummy" verifies in
/// <c>AuthService</c> sit on mutually exclusive early-return branches — user-not-found and account-locked — so
/// no request performs more than one). At cost 12 that verify measured <b>~607 ms</b> on an 8-core dev machine,
/// capping login throughput at roughly 13/sec; 20 concurrent users queue behind it and p95 lands near 3.9 s
/// against an 800 ms SLA. Each step down the factor halves the cost: <b>~370 ms at 11, ~149 ms at 10</b>.</para>
///
/// <para><b>Default is 11</b> (user decision, 2026-08-05, on the measurements above): ~370 ms and roughly 21
/// logins/sec on 8 cores, which clears the 800 ms p95 SLA at 20 concurrent users while staying a full step
/// above the OWASP floor. It is deliberately not 10 — that would be more comfortable but leaves nowhere to go
/// down later without breaching guidance. The margin at 11 is thin, so it assumes production has at least the
/// cores this measurement was taken on; re-measure with k6 if the production box is smaller.</para>
///
/// <para><b>Floor of 10.</b> Anything lower is below current OWASP guidance for bcrypt, so it is rejected at
/// startup rather than accepted quietly. A silently-weakened password hash is the kind of change that is
/// invisible until a breach, which is precisely when nobody is reading configuration files.</para>
/// </summary>
public sealed class PasswordHashingOptions
{
    public const string SectionName = "Authentication:PasswordHashing";

    /// <summary>The OWASP floor. Below this the application refuses to start.</summary>
    public const int MinimumWorkFactor = 10;

    /// <summary>
    /// Lowered from the historical hard-coded 12 (ISSUE-203). See the measurements in the class remarks: 12
    /// capped login at ~13/sec and blew the 800 ms p95 SLA to ~3.9 s at 20 concurrent users.
    /// </summary>
    public const int DefaultWorkFactor = 11;

    public int WorkFactor { get; set; } = DefaultWorkFactor;

    /// <summary>
    /// When true (default), a successful login whose stored hash was produced at a DIFFERENT cost factor is
    /// transparently re-hashed at the configured one.
    ///
    /// <para>Without this, changing the factor only affects new passwords: every existing user keeps paying
    /// the old cost forever, so the SLA never actually improves and the setting looks broken. Re-hashing on
    /// login migrates the estate gradually, at exactly the moment the plaintext is legitimately in hand — and
    /// costs one extra hash on the first login per user, never on subsequent ones.</para>
    /// </summary>
    public bool RehashOnLogin { get; set; } = true;
}
