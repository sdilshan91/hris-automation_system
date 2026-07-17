using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Serilog;

namespace HRM.Infrastructure.Persistence.Converters;

/// <summary>
/// An enum &lt;-&gt; string value converter whose READ side tolerates a DB value outside the enum: instead of
/// throwing during materialization (EF's default <c>EnumToStringConverter&lt;T&gt;</c> / <c>HasConversion&lt;string&gt;()</c>
/// behaviour, made strict by the EF Core 6 breaking change dotnet/efcore#24084), an unrecognised string is
/// mapped to an explicit <paramref name="fallback"/> sentinel and logged.
///
/// <para><b>Why this exists (ISSUE-231 / ENH-021).</b> These enum columns are materialized as part of a
/// LIST/BOARD projection (the recruitment pipeline board, the payroll-run list). Because the strict converter
/// throws while iterating the result set, ONE row holding a stale/renamed enum string (legacy seed, manual
/// SQL, a removed member) 500s the ENTIRE query for every user. Mapping the unknown value to a dedicated
/// <c>Unknown</c> sentinel keeps the board available and surfaces the bad row rather than taking everything
/// down. Prior art: the bespoke <c>LeaveLedgerConfiguration.ParseEntryType</c> (BUG-037/086), of which this is
/// the generalised form.</para>
///
/// <para><b>The fallback MUST be a dedicated sentinel, never a real state.</b> The EF Core 6 change removed the
/// old silent coercion precisely because coercing an unknown value to a meaningful member and then saving the
/// entity back would overwrite (destroy) the corrupt value. So this is applied ONLY to genuinely enumerable
/// STATUS columns whose <c>Unknown</c> member is a non-semantic sentinel — never to money/amount enums, which
/// keep the strict throwing converter. The WRITE side is unchanged (<c>v.ToString()</c>, injective), so
/// equality/constant query translation — <c>Where(x =&gt; x.Status == Approved)</c> — is unaffected, and the
/// <c>Unknown</c> sentinel is never written by the app (the write validators reject it).</para>
/// </summary>
public sealed class TolerantEnumToStringConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    public TolerantEnumToStringConverter(TEnum fallback)
        : base(v => v.ToString(), v => Parse(v, fallback))
    {
    }

    private static TEnum Parse(string value, TEnum fallback)
    {
        if (Enum.TryParse<TEnum>(value, out var parsed) && Enum.IsDefined(parsed))
            return parsed;

        // Loud, not swallowed: a bad row is rare (the write path validates the enum) and worth surfacing.
        Log.ForContext("SourceContext", "TolerantEnumConversion")
            .Warning("Unmapped {EnumType} value {Value} read from the database; using {Fallback} sentinel",
                typeof(TEnum).Name, value, fallback);
        return fallback;
    }
}
