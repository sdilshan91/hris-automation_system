using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HRM.Api.Json;
using Xunit;

namespace HRM.Tests.Unit;

/// <summary>
/// Unit arms for the <b>BUG-431</b> boundary converters (<see cref="UtcDateTimeJsonConverter"/> /
/// <see cref="UtcNullableDateTimeJsonConverter"/>). The defect was that an offset-less JSON date
/// (<c>"2026-01-01"</c>, exactly what an Angular <c>&lt;input type="date"&gt;</c> emits) deserialized with
/// <see cref="DateTimeKind.Unspecified"/>, which Npgsql rejects for a <c>timestamp with time zone</c> column —
/// producing an HTTP 500 where the same value with a <c>Z</c> suffix produced a 201.
///
/// <para>These deserialize with the app's REAL controller options (Web defaults + the global
/// <see cref="JsonStringEnumConverter"/> + the two converters, mirroring <c>Program.cs AddJsonOptions</c>), so
/// they assert the converter as it is actually wired, not in isolation. The end-to-end HTTP proof is
/// <c>HRM.Tests.Integration.Http.CycleCreateDateOnlyPayloadApiTests</c>.</para>
/// </summary>
[Trait("TC", "TC-PRF-004-431")]
public sealed class UtcDateTimeJsonConverterTests
{
    private static readonly JsonSerializerOptions WireOptions = BuildWireOptions();

    private static JsonSerializerOptions BuildWireOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new UtcDateTimeJsonConverter());
        options.Converters.Add(new UtcNullableDateTimeJsonConverter());
        return options;
    }

    private sealed record DateHolder(DateTime When);

    private sealed record NullableDateHolder(DateTime? When);

    // ── Unspecified → Utc: the BUG-431 case ────────────────────────────────────────────────────────
    [Theory]
    [InlineData("2026-01-01", 2026, 1, 1, 0, 0, 0)]                 // date-only: what <input type="date"> sends
    [InlineData("2026-01-31T09:30:00", 2026, 1, 31, 9, 30, 0)]      // date-time with no offset at all
    public void OffsetLessValue_IsTreatedAsUtc_WithoutShiftingTheInstant(
        string json, int year, int month, int day, int hour, int minute, int second)
    {
        var holder = JsonSerializer.Deserialize<DateHolder>($$"""{"when":"{{json}}"}""", WireOptions);

        holder!.When.Kind.Should().Be(DateTimeKind.Utc,
            "Kind=Unspecified is what Npgsql rejects for a timestamptz column");
        holder.When.Should().Be(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc),
            "an offset-less value must be STAMPED as UTC, never shifted by the server's local offset");
    }

    // ── Utc stays Utc, unchanged ───────────────────────────────────────────────────────────────────
    [Fact]
    public void UtcSuffixedValue_StaysUtc_Unchanged()
    {
        var holder = JsonSerializer.Deserialize<DateHolder>("""{"when":"2026-01-31T09:30:00Z"}""", WireOptions);

        holder!.When.Kind.Should().Be(DateTimeKind.Utc);
        holder.When.Should().Be(new DateTime(2026, 1, 31, 9, 30, 0, DateTimeKind.Utc),
            "the previously-working payload shape must be byte-for-byte unaffected by the fix");
    }

    // ── Local (numeric offset) → the same instant in Utc ───────────────────────────────────────────
    [Fact]
    public void OffsetBearingValue_IsConvertedToTheSameInstantInUtc()
    {
        // System.Text.Json materialises an offset-bearing ISO string as DateTimeKind.Local. +05:30 means the
        // instant is 04:00Z, so this asserts a genuine conversion, not a re-stamp.
        var holder = JsonSerializer.Deserialize<DateHolder>("""{"when":"2026-01-31T09:30:00+05:30"}""", WireOptions);

        holder!.When.Kind.Should().Be(DateTimeKind.Utc);
        holder.When.Should().Be(new DateTime(2026, 1, 31, 4, 0, 0, DateTimeKind.Utc),
            "an offset-bearing value carries a real instant, which must be preserved (not re-stamped)");
    }

    // ── Nullable sibling: null stays null ──────────────────────────────────────────────────────────
    [Fact]
    public void NullableConverter_KeepsNullAsNull()
    {
        JsonSerializer.Deserialize<NullableDateHolder>("""{"when":null}""", WireOptions)!.When
            .Should().BeNull("an omitted/optional date must not become a default DateTime");

        JsonSerializer.Deserialize<NullableDateHolder>("{}", WireOptions)!.When
            .Should().BeNull();
    }

    // ── Nullable sibling: same Unspecified → Utc coercion when a value IS present ──────────────────
    [Fact]
    public void NullableConverter_CoercesOffsetLessValueToUtc()
    {
        var holder = JsonSerializer.Deserialize<NullableDateHolder>("""{"when":"2026-01-01"}""", WireOptions);

        holder!.When!.Value.Kind.Should().Be(DateTimeKind.Utc);
        holder.When.Value.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    // ── Malformed input must raise JsonException (→ 400 at the MVC boundary), not FormatException ──
    [Theory]
    [InlineData("\"not-a-date\"")]
    [InlineData("\"2026-13-45\"")]
    [InlineData("12345")]
    public void MalformedValue_ThrowsJsonException_SoTheBoundaryReturns400(string rawJsonValue)
    {
        var act = () => JsonSerializer.Deserialize<DateHolder>($$"""{"when":{{rawJsonValue}}}""", WireOptions);

        // SystemTextJsonInputFormatter maps JsonException to a model-state error → ValidationFilter → 400.
        // Anything else (e.g. FormatException from GetDateTime) risks escaping as an unhandled 500.
        act.Should().Throw<JsonException>();
    }

    // ── Serialization must be unchanged — the fix is read-side only ───────────────────────────────
    [Fact]
    public void Serialization_IsUnchangedByTheConverters()
    {
        var value = new DateTime(2026, 1, 31, 9, 30, 0, DateTimeKind.Utc);

        var withConverters = JsonSerializer.Serialize(new DateHolder(value), WireOptions);
        var withoutConverters = JsonSerializer.Serialize(
            new DateHolder(value), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        withConverters.Should().Be(withoutConverters,
            "responses must keep their existing wire format — the FE contract and the OpenAPI schema-diff gate depend on it");
    }
}
