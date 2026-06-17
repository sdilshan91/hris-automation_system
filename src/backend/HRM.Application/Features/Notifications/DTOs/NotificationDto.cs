namespace HRM.Application.Features.Notifications.DTOs;

/// <summary>
/// A single notification as surfaced to the client (US-NTF-001, pinned FE↔BE contract).
/// Serialized camelCase; <c>createdAt</c> is ISO-8601 (the global JsonStringEnumConverter does not
/// affect these since Type is carried as a plain string from the entity).
/// </summary>
public sealed record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Message,
    string? ResourceType,
    string? ResourceId,
    bool IsRead,
    DateTime CreatedAt);

/// <summary>
/// Paged notification panel payload (US-NTF-001 AC-3/FR-6). Carries the page of items plus the
/// total + unread counts so the bell badge (FR-5) and pager render in one round-trip.
/// </summary>
public sealed record NotificationPageDto(
    IReadOnlyList<NotificationDto> Items,
    int UnreadCount,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>Unread-count badge payload (US-NTF-001 FR-5).</summary>
public sealed record UnreadCountDto(int Count);

/// <summary>Result of the Mark-All-as-Read bulk action (US-NTF-001 AC-5).</summary>
public sealed record MarkAllReadResultDto(int Updated);
