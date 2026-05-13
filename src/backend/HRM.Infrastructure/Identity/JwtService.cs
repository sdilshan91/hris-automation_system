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
    private readonly IConfiguration _configuration;
    private readonly RsaSecurityKey _signingKey;
    private readonly RsaSecurityKey _validationKey;

    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;

        // In production, load from secrets vault / key management service.
        // For local dev, we generate an RSA key pair on startup.
        var rsa = RSA.Create(2048);

        var privateKeyPem = _configuration["Jwt:PrivateKey"];
        if (!string.IsNullOrEmpty(privateKeyPem))
        {
            rsa.ImportFromPem(privateKeyPem);
        }

        _signingKey = new RsaSecurityKey(rsa) { KeyId = "hrm-dev-key-1" };
        _validationKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "hrm-dev-key-1" };
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
            IssuerSigningKey = _validationKey,
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
        IssuerSigningKey = _validationKey,
    };
}
