namespace HRM.Domain.Entities;

/// <summary>
/// Stores idempotency keys per tenant to prevent duplicate write operations (US-CHR-009 NFR-3).
/// Each record holds the key, the response payload, and an expiry time.
/// After expiry the record can be cleaned up by a background job.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>
    /// Primary key (UUIDv7).
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant scoping: the idempotency key is unique per tenant.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The client-provided idempotency key (from the Idempotency-Key header).
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// The name of the operation (e.g. "ChangeEmployeeStatus").
    /// </summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>
    /// Serialized response JSON from the first successful execution.
    /// Returned verbatim on subsequent calls with the same key.
    /// </summary>
    public string? ResponseJson { get; set; }

    /// <summary>
    /// HTTP status code of the original response.
    /// </summary>
    public int ResponseStatusCode { get; set; }

    /// <summary>
    /// When this record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this record expires and can be cleaned up (default: 24 hours after creation).
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}
