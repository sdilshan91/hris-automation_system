using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Pure rule-priority/specificity engine for resolving effective leave entitlement (US-LV-002 FR-2, AC-2).
///
/// Resolution order (highest precedence first):
///   1. Employee override (AC-3)
///   2. Rule: department + job_title + employment_type (most specific)
///   3. Rule: department + job_title
///   4. Rule: department only
///   5. Rule: job_title only
///   6. Leave type default annual_entitlement
///
/// Within the same specificity tier, the rule with the lowest Priority value wins.
/// Tenure brackets are applied as an additional filter, not a specificity dimension.
///
/// This class is static and pure — no I/O, no DI — for easy unit testing.
/// </summary>
internal static class LeaveEntitlementEngine
{
    /// <summary>
    /// Result of entitlement resolution.
    /// </summary>
    internal sealed record EntitlementResolution(
        decimal BaseEntitlementDays,
        string Source);

    /// <summary>
    /// Resolves the effective entitlement from a set of candidate rules.
    /// </summary>
    /// <param name="matchingRules">Active rules matching the leave type, effective date range, and tenant.</param>
    /// <param name="employeeDepartmentId">The employee's department ID.</param>
    /// <param name="employeeJobTitleId">The employee's job title ID.</param>
    /// <param name="employeeEmploymentType">The employee's employment type.</param>
    /// <param name="tenureMonths">Employee tenure in months at the point of evaluation.</param>
    /// <param name="leaveTypeDefaultEntitlement">Fallback: the leave type's annual_entitlement.</param>
    /// <returns>The resolved entitlement days and source label.</returns>
    internal static EntitlementResolution Resolve(
        IReadOnlyList<LeaveEntitlementRule> matchingRules,
        Guid employeeDepartmentId,
        Guid employeeJobTitleId,
        EmploymentType employeeEmploymentType,
        int tenureMonths,
        decimal leaveTypeDefaultEntitlement)
    {
        // Filter rules that match employee dimensions and tenure.
        var candidates = new List<(LeaveEntitlementRule Rule, int SpecificityScore)>();

        foreach (var rule in matchingRules)
        {
            // Check dimension match.
            if (rule.DepartmentId.HasValue && rule.DepartmentId.Value != employeeDepartmentId)
                continue;
            if (rule.JobTitleId.HasValue && rule.JobTitleId.Value != employeeJobTitleId)
                continue;
            if (rule.EmploymentType.HasValue && rule.EmploymentType.Value != employeeEmploymentType)
                continue;

            // Check tenure bracket.
            if (rule.TenureMinMonths.HasValue && tenureMonths < rule.TenureMinMonths.Value)
                continue;
            if (rule.TenureMaxMonths.HasValue && tenureMonths >= rule.TenureMaxMonths.Value)
                continue;

            // Calculate specificity score (higher = more specific).
            int score = 0;
            if (rule.DepartmentId.HasValue) score += 4;
            if (rule.JobTitleId.HasValue) score += 2;
            if (rule.EmploymentType.HasValue) score += 1;

            candidates.Add((rule, score));
        }

        if (candidates.Count == 0)
        {
            return new EntitlementResolution(leaveTypeDefaultEntitlement, "leave_type_default");
        }

        // Pick the most specific rule (highest score), then lowest Priority number as tiebreaker.
        var winner = candidates
            .OrderByDescending(c => c.SpecificityScore)
            .ThenBy(c => c.Rule.Priority)
            .First();

        return new EntitlementResolution(winner.Rule.EntitlementDays, $"rule:{winner.Rule.Id}");
    }

    /// <summary>
    /// Calculates pro-rated entitlement for mid-year joiners (FR-3, AC-4, BR-2).
    /// </summary>
    /// <param name="fullYearEntitlement">The full-year entitlement in days.</param>
    /// <param name="dateOfJoining">Employee's date of joining.</param>
    /// <param name="leaveYear">The leave year being computed.</param>
    /// <param name="fte">Full-time equivalent ratio (1.0 for full-time). Defaults to 1.0.</param>
    /// <returns>Pro-rated entitlement, rounded to 2dp half-up, never negative (BR-4).</returns>
    internal static decimal CalculateProRata(
        decimal fullYearEntitlement,
        DateTime dateOfJoining,
        int leaveYear,
        decimal fte = 1.0m)
    {
        var yearStart = new DateTime(leaveYear, 1, 1);
        var yearEnd = new DateTime(leaveYear, 12, 31);

        // If joined before this year, full entitlement for the year.
        DateTime effectiveStart = dateOfJoining <= yearStart ? yearStart : dateOfJoining;

        if (effectiveStart > yearEnd)
        {
            // Not employed during this leave year.
            return 0m;
        }

        int totalDaysInYear = (yearEnd - yearStart).Days + 1; // 365 or 366
        int remainingDays = (yearEnd - effectiveStart).Days + 1;

        decimal ratio = (decimal)remainingDays / totalDaysInYear;
        decimal prorated = fullYearEntitlement * ratio * fte;

        // Round to 2 decimal places, half-up (MidpointRounding.AwayFromZero).
        prorated = Math.Round(prorated, 2, MidpointRounding.AwayFromZero);

        // BR-4: Entitlement cannot be negative; minimum is zero.
        return Math.Max(prorated, 0m);
    }

    /// <summary>
    /// Computes employee tenure in whole months from date_of_joining to a reference date.
    /// </summary>
    internal static int ComputeTenureMonths(DateTime dateOfJoining, DateTime referenceDate)
    {
        if (referenceDate < dateOfJoining)
            return 0;

        int months = (referenceDate.Year - dateOfJoining.Year) * 12
                     + (referenceDate.Month - dateOfJoining.Month);

        // If the day hasn't been reached yet in the current month, subtract one.
        if (referenceDate.Day < dateOfJoining.Day)
            months--;

        return Math.Max(months, 0);
    }
}
