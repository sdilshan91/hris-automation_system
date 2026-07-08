using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// JWT service for generating RS256-signed access tokens and cryptographically secure refresh tokens.
/// Access tokens expire in 15 minutes; refresh tokens expire in 7 days.
/// </summary>
public sealed class JwtService : IJwtService
{
    /// <summary>US-ADM-003 NFR-2: impersonation tokens are capped at 60 minutes and are not refreshable.</summary>
    private const int ImpersonationMaxTtlMinutes = 60;

    /// <summary>US-ADM-003 FR-2: the truncation length for the <c>imp_reason</c> claim.</summary>
    private const int ImpersonationReasonClaimMaxLength = 100;

    private readonly IConfiguration _configuration;
    private readonly RsaSecurityKey _signingKey;

    // The signing key ring: the current primary's PUBLIC key plus any extra public keys configured for a
    // rotation overlap window. The JWT handler matches a token to a key by its `kid` header, so a token
    // signed under a previous (now-retired) key still validates as long as that key remains in this set —
    // that is the overlap. See JwtKeyRingOptions for the deploy -> promote -> drop rotation procedure.
    private readonly IReadOnlyList<SecurityKey> _validationKeys;

    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;

        var options = configuration.GetSection("Jwt").Get<JwtKeyRingOptions>() ?? new JwtKeyRingOptions();

        // In production, load from secrets vault / key management service.
        // For local dev, we generate an RSA key pair on startup.
        var rsa = RSA.Create(2048);

        var privateKeyPem = options.PrivateKey;
        if (!string.IsNullOrEmpty(privateKeyPem))
        {
            rsa.ImportFromPem(privateKeyPem);
        }

        _signingKey = new RsaSecurityKey(rsa) { KeyId = options.SigningKeyId };

        // Build the validation ring: the primary's public key first, then each configured overlap key.
        var validationKeys = new List<SecurityKey>
        {
            new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = options.SigningKeyId },
        };

        foreach (var extra in options.ValidationKeys)
        {
            if (string.IsNullOrWhiteSpace(extra.PublicKeyPem))
                continue;

            var extraRsa = RSA.Create();
            extraRsa.ImportFromPem(extra.PublicKeyPem);
            validationKeys.Add(new RsaSecurityKey(extraRsa.ExportParameters(false)) { KeyId = extra.Kid });
        }

        _validationKeys = validationKeys;
    }

    public string GenerateAccessToken(User user, Guid tenantId, Guid userTenantId, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", tenantId.ToString()),
            new("user_tenant_id", userTenantId.ToString()),
            new("is_impersonation", "false"),
        };

        foreach (var role in roles)
            claims.Add(new Claim("roles", role));

        foreach (var permission in permissions)
            claims.Add(new Claim("permissions", permission));

        var signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "hrm-api",
            audience: _configuration["Jwt:Audience"] ?? "hrm-client",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateImpersonationToken(
        User targetUser,
        Guid targetTenantId,
        Guid targetUserTenantId,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        Guid sessionId,
        Guid impersonatorUserId,
        string reason,
        bool isReadOnly,
        DateTime expiresAt)
    {
        var now = DateTime.UtcNow;

        // NFR-2: hard-cap the TTL at 60 minutes. The token's exp is the EARLIER of the requested session
        // expiry and now+60min, so a caller can never mint a longer-lived impersonation token.
        var maxExpiry = now.AddMinutes(ImpersonationMaxTtlMinutes);
        var effectiveExpiry = expiresAt < maxExpiry ? expiresAt : maxExpiry;

        // The reason is informational in the token (the verbatim copy lives on the session row); truncate it
        // so the token stays small and never carries an oversized free-text field.
        var truncatedReason = reason.Length > ImpersonationReasonClaimMaxLength
            ? reason[..ImpersonationReasonClaimMaxLength]
            : reason;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, targetUser.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, targetUser.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", targetTenantId.ToString()),
            new("user_tenant_id", targetUserTenantId.ToString()),
            // is_impersonation flips to "true" for this token type (it is "false" on a normal access token).
            new(ImpersonationClaims.IsImpersonation, "true"),
            new(ImpersonationClaims.SessionId, sessionId.ToString()),
            new(ImpersonationClaims.ActorId, impersonatorUserId.ToString()),
            new(ImpersonationClaims.Reason, truncatedReason),
            new(ImpersonationClaims.ReadOnly, isReadOnly ? "true" : "false"),
            new(ImpersonationClaims.ExpiresAt,
                new DateTimeOffset(effectiveExpiry).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        foreach (var role in roles)
            claims.Add(new Claim("roles", role));

        foreach (var permission in permissions)
            claims.Add(new Claim("permissions", permission));

        var signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "hrm-api",
            audience: _configuration["Jwt:Audience"] ?? "hrm-client",
            claims: claims,
            notBefore: now,
            expires: effectiveExpiry,
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _configuration["Jwt:Issuer"] ?? "hrm-api",
            ValidateAudience = true,
            ValidAudience = _configuration["Jwt:Audience"] ?? "hrm-client",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = _validationKeys,
        };

        try
        {
            return tokenHandler.ValidateToken(token, validationParameters, out _);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the token validation parameters for ASP.NET Core authentication middleware.
    /// </summary>
    public TokenValidationParameters GetTokenValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _configuration["Jwt:Issuer"] ?? "hrm-api",
        ValidateAudience = true,
        ValidAudience = _configuration["Jwt:Audience"] ?? "hrm-client",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = _validationKeys,
        // BUG-240: roles are emitted under the custom "roles" claim (GenerateAccessToken),
        // and MapInboundClaims=false (BUG-001) keeps claim names 1:1 — so the framework
        // never maps them to the default ClaimTypes.Role. Point role-based checks
        // ([Authorize(Roles=...)] / User.IsInRole()) at the "roles" claim so they resolve
        // instead of 403-ing for the correct role. (This only tells the middleware which
        // claim is the role claim; it does NOT remap claims, so the read-only System-Support
        // impersonation behaviour from BUG-001/106 is unaffected.)
        RoleClaimType = "roles",
    };
}
