// ============================================================================
// US-PLT-004 (item 3): the HRM.Domain meters must actually record what they
// claim, at the right instrument, with the right tags. A MeterListener scoped
// to the "HRM.Domain" meter captures the real measurements — this fails if an
// instrument is renamed, a tag is dropped, or a record call is removed (i.e.
// the mutants the story asks to be killed).
// ============================================================================

using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using HRM.Application.Common.Observability;

namespace HRM.Tests.Unit;

public sealed class HrmDomainMetricsTests
{
    private sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

    /// <summary>
    /// Runs <paramref name="act"/> while a MeterListener scoped to <see cref="HrmDomainMetrics.MeterName"/> is
    /// attached, and returns every measurement it recorded (both long counters and double histograms).
    /// </summary>
    private static IReadOnlyList<Measurement> Capture(Action act)
    {
        // The instruments are static field initializers — force the class ctor so they exist BEFORE Start()
        // publishes instruments (referencing the const MeterName alone would be inlined and not trigger it).
        RuntimeHelpers.RunClassConstructor(typeof(HrmDomainMetrics).TypeHandle);

        var captured = new List<Measurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == HrmDomainMetrics.MeterName)
                l.EnableMeasurementEvents(instrument);
        };

        void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            where T : struct
        {
            var dict = new Dictionary<string, object?>();
            foreach (var tag in tags)
                dict[tag.Key] = tag.Value;
            captured.Add(new Measurement(instrument.Name, Convert.ToDouble(value), dict));
        }

        listener.SetMeasurementEventCallback<long>((i, v, t, _) => Record(i, v, t));
        listener.SetMeasurementEventCallback<double>((i, v, t, _) => Record(i, v, t));
        listener.Start();

        act();

        listener.Dispose(); // flush
        return captured;
    }

    [Fact]
    public void RecordLogin_Success_EmitsLoginCounter_TaggedSuccess()
    {
        var measurements = Capture(() => HrmDomainMetrics.RecordLogin(true));

        var m = measurements.Should().ContainSingle(x => x.Instrument == "hrm.auth.login").Subject;
        m.Value.Should().Be(1);
        m.Tags.Should().ContainKey("outcome").WhoseValue.Should().Be("success");
    }

    [Fact]
    public void RecordLogin_Failure_EmitsLoginCounter_TaggedFailure()
    {
        var measurements = Capture(() => HrmDomainMetrics.RecordLogin(false));

        var m = measurements.Should().ContainSingle(x => x.Instrument == "hrm.auth.login").Subject;
        m.Value.Should().Be(1);
        m.Tags.Should().ContainKey("outcome").WhoseValue.Should().Be("failure");
    }

    [Fact]
    public void RecordLeaveRequestSubmitted_EmitsLeaveCounter()
    {
        var measurements = Capture(HrmDomainMetrics.RecordLeaveRequestSubmitted);

        var m = measurements.Should().ContainSingle(x => x.Instrument == "hrm.leave.request.submitted").Subject;
        m.Value.Should().Be(1);
    }

    [Fact]
    public void RecordPayrollRunDuration_EmitsHistogram_WithGivenValue()
    {
        var measurements = Capture(() => HrmDomainMetrics.RecordPayrollRunDuration(1234.5));

        var m = measurements.Should().ContainSingle(x => x.Instrument == "hrm.payroll.run.duration").Subject;
        m.Value.Should().Be(1234.5);
    }
}
