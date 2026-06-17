namespace HRM.Application.Features.NotificationPreferences.DTOs;

/// <summary>
/// One row of the effective preference matrix (US-NTF-003 AC-1). The merged result of the user's saved
/// override (if any) over the category default. <c>category</c> is the enum NAME (US-PLT-003 string enums).
/// Mandatory categories report <c>isMandatory=true</c> with both channels forced on (BR-2/AC-3/AC-4).
/// </summary>
public sealed record CategoryPreferenceDto(
    string Category,
    string CategoryName,
    string Description,
    bool ChannelInApp,
    bool ChannelEmail,
    bool IsMandatory);

/// <summary>
/// The user's quiet-hours setting (US-NTF-003 FR-9/BR-5). <c>enabled</c> is true when a start/end window is
/// configured. Times serialize as "HH:mm:ss"; the FE renders/sends "HH:mm" (handled leniently on input).
/// <c>timezone</c> is an IANA id, e.g. "Asia/Colombo".
/// </summary>
public sealed record QuietHoursDto(
    bool Enabled,
    TimeOnly? Start,
    TimeOnly? End,
    string? Timezone);

/// <summary>The complete preference matrix returned to the user (US-NTF-003 AC-1, GET / reset).</summary>
public sealed record PreferenceMatrixDto(
    IReadOnlyList<CategoryPreferenceDto> Categories,
    QuietHoursDto QuietHours);
