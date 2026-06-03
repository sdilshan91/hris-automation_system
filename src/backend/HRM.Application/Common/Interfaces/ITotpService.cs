namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service for TOTP (Time-based One-Time Password) operations per RFC 6238.
/// Handles secret generation, code validation, QR code generation, and recovery codes.
/// </summary>
public interface ITotpService
{
    /// <summary>
    /// Generates a new 20-byte Base32-encoded TOTP secret.
    /// </summary>
    string GenerateSecret();

    /// <summary>
    /// Generates an otpauth:// URI for TOTP enrollment (RFC 6238).
    /// </summary>
    string GenerateOtpAuthUri(string secret, string accountName, string issuer);

    /// <summary>
    /// Generates a QR code data URL (data:image/png;base64,...) for the given otpauth URI.
    /// Server-side generation avoids external service dependencies (NFR-5).
    /// </summary>
    string GenerateQrCodeDataUrl(string otpAuthUri);

    /// <summary>
    /// Validates a 6-digit TOTP code against the secret with +/- 1 step drift window (FR-10).
    /// SHA1, 6 digits, 30-second time step.
    /// </summary>
    bool ValidateCode(string secret, string code);

    /// <summary>
    /// Generates cryptographically random single-use recovery codes.
    /// Format: XXXXX-XXXXX (10 base32 chars, no ambiguous characters).
    /// </summary>
    List<string> GenerateRecoveryCodes(int count = 10);

    /// <summary>
    /// Computes SHA-256 hash of a recovery code for storage.
    /// Recovery codes are high-entropy single-use values, so SHA-256 is sufficient.
    /// </summary>
    string HashRecoveryCode(string code);

    /// <summary>
    /// Verifies a recovery code against its stored hash.
    /// </summary>
    bool VerifyRecoveryCode(string code, string hash);
}
