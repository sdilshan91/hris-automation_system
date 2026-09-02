using System.Text.Json;
using System.Text.Json.Serialization;

namespace HRM.Api.Json;

/// <summary>
/// BUG-431 — the API boundary's date-time normaliser.
///
/// <para><b>The defect.</b> <see cref="System.Text.Json"/> parses an ISO-8601 string without an offset
/// (<c>"2026-01-01"</c>, the exact shape an Angular <c>&lt;input type="date"&gt;</c> emits) into a
/// <see cref="DateTime"/> with <see cref="DateTimeKind.Unspecified"/>. Npgsql then refuses to write it:
/// <c>ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time
/// zone', only UTC is supported</c> — an unhandled <c>DbUpdateException</c> that surfaced as HTTP <b>500</b>.
/// The same string with a <c>Z</c> suffix returned 201, so the failure was the <i>Kind</i>, not the payload.</para>
///
/// <para><b>Why here and not per-DTO.</b> The backend exposes 26+ bare <see cref="DateTime"/> request
/// properties bound to <c>timestamptz</c> columns, and 42 frontend files bind <c>&lt;input type="date"&gt;</c>;
/// any pairing reproduces the bug. One converter registered globally in <c>Program.cs</c>'s
/// <c>AddJsonOptions</c> closes all of them at once, without changing a single DTO's contract
/// (<c>DateTime</c> stays <c>DateTime</c>; Swagger still reports <c>format: date-time</c>).</para>
///
/// <para><b>Semantics.</b> An offset-less value is interpreted as <b>UTC</b> (<c>"2026-01-01"</c> ⇒
/// <c>2026-01-01T00:00:00Z</c>) rather than as server-local time: the platform is multi-tenant with
/// per-tenant time zones, so the server's own zone carries no meaning and must never silently shift a
/// caller's calendar day. A value that already carries <c>Z</c> is unchanged; one carrying a numeric offset
/// (which System.Text.Json materialises as <see cref="DateTimeKind.Local"/>) is converted to the same instant
/// in UTC. Serialization is deliberately left at the framework default, so no response shape changes.</para>
///
/// <para><b>Malformed input stays a 400.</b> A value that is not a parseable date throws
/// <see cref="JsonException"/>, which <c>SystemTextJsonInputFormatter</c> converts into a model-state error →
/// <c>ValidationFilter</c> → HTTP 400. It must never reach <c>ExceptionHandlingMiddleware</c>'s 500 branch.</para>
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => UtcDateTimeJson.ReadAsUtc(ref reader);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

/// <summary>
/// Nullable sibling of <see cref="UtcDateTimeJsonConverter"/> — same read semantics, <c>null</c> stays
/// <c>null</c>. Required separately: a <see cref="JsonConverter{T}"/> for <see cref="DateTime"/> is not
/// applied to <c>DateTime?</c> properties, and roughly half the affected request DTOs (optional
/// <c>EffectiveFrom</c>/<c>EndDate</c> fields) are nullable.
/// </summary>
public sealed class UtcNullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : UtcDateTimeJson.ReadAsUtc(ref reader);

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value);
        }
    }
}

/// <summary>Shared read path for the two converters above (see <see cref="UtcDateTimeJsonConverter"/>).</summary>
internal static class UtcDateTimeJson
{
    internal static DateTime ReadAsUtc(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected an ISO-8601 date or date-time string, but found {reader.TokenType}.");
        }

        // TryGetDateTime (not GetDateTime) so an unparseable value raises a JsonException — which the MVC
        // input formatter turns into a 400 — rather than a FormatException that could escape as a 500.
        if (!reader.TryGetDateTime(out var value))
        {
            throw new JsonException(
                "The value is not a valid ISO-8601 date or date-time (expected e.g. 2026-01-31 or 2026-01-31T09:00:00Z).");
        }

        return ToUtc(value);
    }

    /// <summary>
    /// Unspecified ⇒ stamped UTC (no instant shift); Local ⇒ converted to the same instant in UTC;
    /// Utc ⇒ unchanged. See the class remarks for why Unspecified is not treated as server-local.
    /// </summary>
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
