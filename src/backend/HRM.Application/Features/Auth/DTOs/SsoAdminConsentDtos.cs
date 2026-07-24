namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// US-AUTH-016 FR-5/AC-4: the Microsoft admin-consent URL the SPA opens so the customer's Microsoft 365 admin can
/// grant tenant-wide consent. The FE navigates the browser to <see cref="ConsentUrl"/>.
/// </summary>
public sealed record AdminConsentUrlResponse(string ConsentUrl);
