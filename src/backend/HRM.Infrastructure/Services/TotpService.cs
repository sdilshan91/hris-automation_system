using System.Security.Cryptography;
using System.Text;
using HRM.Application.Common.Interfaces;
using OtpNet;
using QRCoder;

namespace HRM.Infrastructure.Services;

/// <summary>
/// TOTP service implementing RFC 6238 using Otp.NET and QRCoder.
/// Registered as singleton (no per-request state).
/// </summary>
public sealed class TotpService : ITotpService
{
    // Base32 alphabet excluding ambiguous characters (0, 1, O, I, L)
    private static readonly char[] SafeBase32Chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789".ToCharArray();

    public string GenerateSecret()
    {
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(secretBytes);
    }

    public string GenerateOtpAuthUri(string secret, string accountName, string issuer)
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString(accountName);
        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    public string GenerateQrCodeDataUrl(string otpAuthUri)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(otpAuthUri, QRCodeGenerator.ECCLevel.M);
        using var pngQrCode = new PngByteQRCode(qrCodeData);
        var pngBytes = pngQrCode.GetGraphic(5);
        var base64 = Convert.ToBase64String(pngBytes);
        return $"data:image/png;base64,{base64}";
    }

    public bool ValidateCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
            return false;

        var secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);

        // VerifyTotp with a time window of +/- 1 step (FR-10)
        return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }

    public List<string> GenerateRecoveryCodes(int count = 10)
    {
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            codes.Add(GenerateSingleRecoveryCode());
        }
        return codes;
    }

    public string HashRecoveryCode(string code)
    {
        // Normalize: strip dashes and uppercase
        var normalized = code.Replace("-", "").ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public bool VerifyRecoveryCode(string code, string hash)
    {
        var computedHash = HashRecoveryCode(code);
        return string.Equals(computedHash, hash, StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateSingleRecoveryCode()
    {
        // 10 characters from safe base32 alphabet, formatted as XXXXX-XXXXX
        Span<byte> randomBytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(randomBytes);

        var sb = new StringBuilder(11); // 10 chars + 1 dash
        for (var i = 0; i < 10; i++)
        {
            if (i == 5) sb.Append('-');
            sb.Append(SafeBase32Chars[randomBytes[i] % SafeBase32Chars.Length]);
        }
        return sb.ToString();
    }
}
