namespace HRM.Application.DTOs;

/// <summary>
/// Canonical paged-list envelope for every list endpoint. Serialized under the
/// <c>ApiResponse.data</c> wrapper as { items, page, pageSize, totalCount, totalPages }.
/// This is the single shared shape — module-specific paged DTOs must use it rather than redefine one.
/// </summary>
public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
