using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Auth.Commands;
using HRM.Application.Features.Auth.DTOs;
using HRM.Application.Features.Auth.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Authentication endpoints: login, refresh, logout, password reset, and current user.
/// All endpoints follow the consistent ApiResponse wrapper pattern.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    private const string RefreshTokenCookieName = "refreshToken";

    public AuthController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// POST /api/v1/auth/login
    /// Authenticates user with email + password, optionally with MFA code.
    /// Returns JWT access token in body and sets refresh token as httpOnly cookie.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var command = new LoginCommand(
            request.Email,
            request.Password,
            request.MfaCode,
            GetIpAddress(),
            GetUserAgent());

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));
        }

        var response = result.Value!;

        // If MFA challenge, don't set tokens
        if (response.MfaChallenge)
        {
            return Ok(ApiResponse<LoginResponse>.Ok(response));
        }

        // Set refresh token as httpOnly cookie
        if (!string.IsNullOrEmpty(response.RefreshToken))
        {
            SetRefreshTokenCookie(response.RefreshToken);
        }

        // Remove refresh token from response body (it's in the cookie)
        var bodyResponse = response with { RefreshToken = null };

        return Ok(ApiResponse<LoginResponse>.Ok(bodyResponse));
    }

    /// <summary>
    /// POST /api/v1/auth/refresh
    /// Refreshes the access token using the httpOnly refresh token cookie.
    /// Returns new access token and rotates the refresh token cookie.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RefreshTokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(ApiResponse.Fail("No refresh token provided."));
        }

        var command = new RefreshTokenCommand(refreshToken, GetIpAddress(), GetUserAgent());
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            // Clear cookie on failure
            ClearRefreshTokenCookie();
            return StatusCode(result.StatusCode ?? 401, ApiResponse.Fail(result.Error!));
        }

        var response = result.Value!;

        // Set new refresh token cookie
        if (!string.IsNullOrEmpty(response.RefreshToken))
        {
            SetRefreshTokenCookie(response.RefreshToken);
        }

        // Remove refresh token from response body
        var bodyResponse = response with { RefreshToken = null };

        return Ok(ApiResponse<RefreshTokenResponse>.Ok(bodyResponse));
    }

    /// <summary>
    /// POST /api/v1/auth/logout
    /// Revokes the current refresh token and clears the cookie.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName] ?? string.Empty;

        var command = new LogoutCommand(refreshToken);
        await _mediator.Send(command, cancellationToken);

        ClearRefreshTokenCookie();

        return Ok(ApiResponse.Ok("Logged out successfully."));
    }

    /// <summary>
    /// POST /api/v1/auth/forgot-password
    /// Initiates password reset. Always returns 200 to prevent user enumeration.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ForgotPasswordCommand(request.Email);
        await _mediator.Send(command, cancellationToken);

        return Ok(ApiResponse.Ok("If an account with that email exists, we've sent a password reset link."));
    }

    /// <summary>
    /// POST /api/v1/auth/reset-password
    /// Resets the user's password using a valid reset token.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ResetPasswordCommand(request.Email, request.Token, request.NewPassword);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));
        }

        return Ok(ApiResponse.Ok("Password updated successfully."));
    }

    /// <summary>
    /// GET /api/v1/auth/me
    /// Returns the current authenticated user's profile and tenant context.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<CurrentUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var query = new GetCurrentUserQuery(_currentUser.UserId, _currentUser.TenantId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));
        }

        return Ok(ApiResponse<CurrentUserDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/auth/revoke-sessions
    /// Revokes all sessions for the current user in the current tenant.
    /// </summary>
    [HttpPost("revoke-sessions")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeAllSessions(CancellationToken cancellationToken)
    {
        var authService = HttpContext.RequestServices.GetRequiredService<IAuthService>();
        var result = await authService.RevokeAllSessionsAsync(
            _currentUser.UserId,
            _currentUser.TenantId,
            cancellationToken);

        ClearRefreshTokenCookie();

        return Ok(ApiResponse.Ok("All sessions revoked."));
    }

    #region Private Helpers

    private void SetRefreshTokenCookie(string token)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/api/v1/auth",
        };

        Response.Cookies.Append(RefreshTokenCookieName, token, cookieOptions);
    }

    private void ClearRefreshTokenCookie()
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(-1),
            Path = "/api/v1/auth",
        };

        Response.Cookies.Append(RefreshTokenCookieName, string.Empty, cookieOptions);
    }

    private string? GetIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private string? GetUserAgent()
    {
        return Request.Headers.UserAgent.ToString();
    }

    #endregion
}
