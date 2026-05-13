namespace HRM.Domain.Entities;

/// <summary>
/// Base entity with common audit fields and tenant isolation.
/// All entities inherit from this to enforce tenant_id and audit columns.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Generates a new UUIDv7-like identifier (time-ordered UUID).
    /// </summary>
    public static Guid NewUuidV7()
    {
        // UUIDv7: timestamp-based UUID for ordering
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Span<byte> bytes = stackalloc byte[16];

        // First 6 bytes: 48-bit timestamp
        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;

        // Remaining 10 bytes: random
        Random.Shared.NextBytes(bytes[6..]);

        // Set version 7 (0111)
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        // Set variant 10xx
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}
