using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HRM.ArchitectureTests;

/// <summary>
/// <b>ARCH-004 (ISSUE-439) — a public domain method must not carry an optional parameter that no
/// production caller ever supplies.</b>
///
/// <para><b>The failure this exists to catch, from the actual incident (GAP-022).</b>
/// <c>PayrollOvertimeCalculator.Compute</c> gained trailing optional <c>fte</c> and
/// <c>fteScaledBase</c> parameters, but <c>PayrollRunProcessor.ComputeOvertime</c> kept calling it
/// with four arguments for months. The tenant setting <c>AttendanceSettings.FteScaledOvertimeBase</c>
/// was persisted, settable through the API, surfaced in the UI — and completely inert. Part-time
/// employees' overtime was priced at the full-time hourly base; at 0.5 FTE they were underpaid by
/// roughly half. Nothing went red: the code compiles (that is what a default value is <i>for</i>), and
/// the calculator's own unit tests passed <c>fte</c> in directly, so they proved the maths while the
/// plumbing was missing. That test file's own header conceded it "proves the MATH, not the plumbing".</para>
///
/// <para><b>Why this cannot be a NetArchTest rule, or any reflection rule.</b> NetArchTest reasons about
/// type-to-type dependencies; "was this argument supplied at this call site" is not a dependency and it
/// has no vocabulary for it. Reflection is no better, and for a subtler reason: when C# omits an optional
/// argument, the compiler <b>materialises the default at the call site</b>. In IL, <c>Compute(a,b,c,d)</c>
/// and <c>Compute(a,b,c,d,1.5m,1.0m,false)</c> are the same instruction sequence. The information this
/// rule needs is destroyed by compilation and exists only in the source — so this rule reads the source,
/// via Roslyn syntax analysis.</para>
///
/// <para><b>Known precision limits — stated because a guard whose blind spots are undocumented invites
/// false confidence.</b> The analysis is syntactic, not semantic (binding the full solution through
/// MSBuildWorkspace would be far more fragile than the rule is worth). Consequently methods are matched
/// by <i>name</i>: if two domain types both expose <c>Compute</c> and only one caller passes the extra
/// argument, both are treated as covered. That is a false negative — the rule under-reports rather than
/// over-reports, which is the correct direction for a guard that must not be silenced by noise. Methods
/// with <b>no</b> production call site at all are skipped here: that is dead code, a different defect
/// with a different fix, and folding it in would bury the signal this rule exists for.</para>
/// </summary>
public sealed class InertOptionalParameterTests
{
    /// <summary>
    /// Optional parameters that are legitimately not supplied by any production caller today, each with
    /// the reason it is not the GAP-022 failure mode. This list may only <b>shrink</b>. Adding an entry
    /// to silence a new failure is how this rule stops working — a new inert parameter is a wiring bug,
    /// and the fix is at the call site.
    /// </summary>
    /// <remarks>
    /// <b>These three were found by this rule on its first run.</b> They are recorded here, not forgiven:
    /// two of them are open findings that need a payroll owner's decision, and fixing them is a money
    /// change requiring a run-level integration arm — out of scope for the commit that added this rule.
    /// The baseline exists so the rule can start biting for NEW violations immediately instead of
    /// sitting red and being disabled.
    /// </remarks>
    private static readonly HashSet<string> KnownInert = new(StringComparer.Ordinal)
    {
        // BENIGN — a structural invariant, not tenant policy. `residualFloor: 0` encodes "the residual
        // earning component may not be driven negative", which is true for every tenant and has no
        // configuration behind it. This is the honest false-positive class of this rule: a convenience
        // default that is correct precisely because nobody overrides it.
        "CtcResidualBalancer.Balance(residualFloor)",

        // OPEN FINDING — GAP-022 recurrence, same file, same method. `defaultMultiplier` prices the
        // legacy-attendance fallback path (empty per-multiplier breakdown + positive approved minutes),
        // and the sole production caller (HRM.Infrastructure/Services/PayrollRunProcessor.cs:1001) omits
        // it. Meanwhile AttendanceSettings.WeekdayOvertimeMultiplier is persisted and tenant-configurable
        // (HRM.Domain/Entities/AttendanceSettings.cs:152, same 1.5m default). A tenant that sets 2.0x gets
        // 1.5x on that path. Remove this entry when the caller threads the resolved multiplier through.
        "PayrollOvertimeCalculator.Compute(defaultMultiplier)",

        // OPEN FINDING — unwired capability rather than an ignored setting. Both production call sites
        // (HRM.Infrastructure/Services/StatutoryDeductionResolver.cs:196 and :205) omit
        // `taxExemptThreshold`, so every tenant computes PAYE with a zero personal allowance. Unlike the
        // entry above there is no persisted field feeding it — "TaxExempt" appears nowhere else in the
        // backend — so this is a half-built feature, not a silently-dropped configured value. Remove this
        // entry when the threshold is either wired to a real setting or deleted from the signature.
        "StatutoryCalculator.ComputeIncomeTaxYtd(taxExemptThreshold)",
    };

    [Fact]
    [Trait("Category", "Architecture")]
    public void No_public_domain_method_has_an_optional_parameter_that_production_never_supplies()
    {
        var domainSources = BackendSource.ProductionSources
            .Where(f => f.Project == "HRM.Domain")
            .ToArray();

        domainSources.Should().HaveCountGreaterThan(100,
            "HRM.Domain holds ~280 source files; a much smaller number means the scan is pointed at the " +
            "wrong tree and this rule is passing vacuously");

        var candidates = domainSources.SelectMany(FindOptionalParameters).ToArray();

        candidates.Should().NotBeEmpty(
            "the domain layer is known to contain optional parameters (PayrollOvertimeCalculator.Compute " +
            "carries three). Finding none means the syntax walk is broken — fix the walk; do not delete " +
            "this assertion.");

        // Call sites across ALL production code (HRM.Tests is not in ProductionSources — that exclusion
        // is the entire point: a parameter supplied only by its own unit test is exactly the GAP-022 shape).
        var callSites = BuildCallSiteIndex();

        var inert = DetectInert(candidates, callSites)
            .Where(c => !KnownInert.Contains(c.Key))
            .ToList();

        var report = inert
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c =>
                $"  - {c.Key}{Environment.NewLine}" +
                $"      declared at {c.Location}, but no caller outside HRM.Tests ever passes it, " +
                $"so it is permanently its default ({c.DefaultText}).");

        inert.Should().BeEmpty(
            "ISSUE-439 / GAP-022: an optional parameter on a public domain method that no production " +
            "caller supplies is dead wiring that looks live. It compiles, it is unit-tested (the " +
            "method's own tests pass it directly), and it silently does nothing in production — which " +
            "is how FTE-scaled overtime under-paid part-timers for weeks. Either thread the value " +
            "through from the real caller, or delete the parameter. Do NOT add it to KnownInert to go " +
            $"green.{Environment.NewLine}{string.Join(Environment.NewLine, report)}{Environment.NewLine}");
    }

    /// <summary>
    /// The baseline may only <b>shrink</b>. When one of the entries above is fixed — the caller starts
    /// supplying the argument, or the parameter is deleted — this goes red and forces the entry out.
    /// Without it a baseline becomes a permanent amnesty list that outlives the problem it recorded, and
    /// the next reader cannot tell which entries are still real.
    /// </summary>
    [Fact]
    [Trait("Category", "Architecture")]
    public void KnownInert_baseline_has_no_stale_entries()
    {
        var candidates = BackendSource.ProductionSources
            .Where(f => f.Project == "HRM.Domain")
            .SelectMany(FindOptionalParameters)
            .ToArray();

        var stillInert = DetectInert(candidates, BuildCallSiteIndex())
            .Select(c => c.Key)
            .ToHashSet(StringComparer.Ordinal);

        var stale = KnownInert.Where(k => !stillInert.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        stale.Should().BeEmpty(
            "these baseline entries are no longer inert — the parameter is now supplied by a production " +
            "caller, or it no longer exists. Delete them from KnownInert so the list keeps reflecting " +
            $"reality. Stale: [{string.Join(", ", stale)}]");
    }

    private static IEnumerable<OptionalParameter> DetectInert(
        IEnumerable<OptionalParameter> candidates,
        Dictionary<string, List<CallSite>> callSites)
    {
        foreach (var candidate in candidates)
        {
            // No production caller at all is dead code — a different defect with a different fix.
            if (!callSites.TryGetValue(candidate.MethodName, out var sites) || sites.Count == 0)
                continue;

            var supplied = sites.Any(s =>
                s.PositionalCount > candidate.Index || s.NamedArguments.Contains(candidate.ParameterName));

            if (!supplied)
                yield return candidate;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Declaration side
    // ---------------------------------------------------------------------------------------------

    private sealed record OptionalParameter(
        string TypeName,
        string MethodName,
        string ParameterName,
        int Index,
        string DefaultText,
        string Location)
    {
        internal string Key => $"{TypeName}.{MethodName}({ParameterName})";
    }

    private static IEnumerable<OptionalParameter> FindOptionalParameters(BackendSource.SourceFile file)
    {
        foreach (var method in file.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!method.Modifiers.Any(SyntaxKind.PublicKeyword))
                continue;

            // A parameter list is only meaningfully "public API" if the containing type is public too.
            if (method.Parent is not TypeDeclarationSyntax type || !type.Modifiers.Any(SyntaxKind.PublicKeyword))
                continue;

            var parameters = method.ParameterList.Parameters;
            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (parameter.Default is null)
                    continue; // no explicit default → not optional (`params` arrays land here, correctly)

                yield return new OptionalParameter(
                    type.Identifier.Text,
                    method.Identifier.Text,
                    parameter.Identifier.Text,
                    i,
                    parameter.Default.Value.ToString(),
                    $"{file.RelativePath}:{file.LineOf(parameter)}");
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Call-site side
    // ---------------------------------------------------------------------------------------------

    private sealed record CallSite(int PositionalCount, HashSet<string> NamedArguments);

    private static Dictionary<string, List<CallSite>> BuildCallSiteIndex()
    {
        var index = new Dictionary<string, List<CallSite>>(StringComparer.Ordinal);

        foreach (var file in BackendSource.ProductionSources)
        {
            foreach (var invocation in file.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = InvokedName(invocation.Expression);
                if (name is null)
                    continue;

                var arguments = invocation.ArgumentList.Arguments;

                // Leading positional arguments. C# permits non-trailing named arguments, so stop at the
                // first named one rather than assuming all named arguments are trailing.
                var positional = 0;
                while (positional < arguments.Count && arguments[positional].NameColon is null)
                    positional++;

                var named = arguments
                    .Where(a => a.NameColon is not null)
                    .Select(a => a.NameColon!.Name.Identifier.Text)
                    .ToHashSet(StringComparer.Ordinal);

                if (!index.TryGetValue(name, out var sites))
                    index[name] = sites = [];

                sites.Add(new CallSite(positional, named));
            }
        }

        return index;
    }

    private static string? InvokedName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text, // Calculator.Compute(...)
        IdentifierNameSyntax identifier => identifier.Identifier.Text,      // Compute(...)
        GenericNameSyntax generic => generic.Identifier.Text,               // Compute<T>(...)
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text, // x?.Compute(...)
        _ => null,
    };
}
