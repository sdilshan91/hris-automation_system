using HRM.Application.Common.Models;

namespace HRM.Application.Common.Interfaces;

/// <summary>How much of the stored file estate is still plaintext (ISSUE-359).</summary>
public sealed record FileEncryptionStatusDto
{
    /// <summary>Files carrying the encryption envelope.</summary>
    public int EncryptedFiles { get; init; }

    /// <summary>
    /// Files still stored as plaintext — the number the rollout decision called NOT optional.
    ///
    /// <para>Tolerating legacy on read is what makes shipping encryption safe without a migration. It is also
    /// exactly how "we will back-fill later" becomes "there is still salary in plaintext two years on". A
    /// visible count is the difference between a deliberate transition and a silent one.</para>
    /// </summary>
    public int PlaintextFiles { get; init; }

    /// <summary>Files whose envelope this build cannot read (a future version) — should be zero.</summary>
    public int UnreadableFiles { get; init; }

    /// <summary>True when new writes are being encrypted (the FileEncryption:Enabled kill-switch).</summary>
    public bool EncryptionEnabled { get; init; }
}

/// <summary>Result of a back-fill sweep.</summary>
public sealed record FileEncryptionSweepResultDto
{
    public int Scanned { get; init; }
    public int Encrypted { get; init; }
    public int Failed { get; init; }
}

/// <summary>
/// ISSUE-359 back-fill: reports how much of the file estate is still plaintext, and sweeps it.
///
/// <para>Deliberately admin-triggered rather than run at startup. A file estate can be large, and blocking boot
/// on a full re-encryption pass would turn a security improvement into a deploy hazard — unlike the
/// column-level back-fill, which is bounded by row count and runs in a transaction.</para>
/// </summary>
public interface IFileEncryptionMaintenanceService
{
    /// <summary>Counts encrypted vs still-plaintext files for the CURRENT tenant.</summary>
    Task<Result<FileEncryptionStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-writes the current tenant's plaintext files through the encrypting storage.</summary>
    Task<Result<FileEncryptionSweepResultDto>> SweepAsync(CancellationToken cancellationToken = default);
}
