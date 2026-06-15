using System.Globalization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Infrastructure.Services;

/// <summary>
/// The single built-in default offer-letter template (US-REC-007 FR-2/BR-4). Configurable per-tenant
/// templates (S35.2.9) are DEFERRED — Phase 1 uses this one template with variable substitution.
///
/// Variables (BR-4): {{applicant_name}}, {{position}}, {{department}}, {{salary}}, {{currency}},
/// {{salary_frequency}}, {{start_date}}, {{expiry_date}}, {{company_name}}, {{probation}}, {{benefits}},
/// {{custom_clauses}}, {{reference_number}}. The substituted body is rendered to PDF by
/// <see cref="OfferPdfRenderer"/>.
/// </summary>
public static class OfferLetterTemplate
{
    /// <summary>
    /// Builds the substitution variable map from the offer + resolved display names. All values are
    /// stringified for rendering; optional values fall back to a readable placeholder.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildVariables(
        Offer offer, string applicantName, string companyName, string? departmentName)
    {
        var ci = CultureInfo.InvariantCulture;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["applicant_name"] = string.IsNullOrWhiteSpace(applicantName) ? "Candidate" : applicantName,
            ["position"] = offer.OfferedPosition,
            ["department"] = string.IsNullOrWhiteSpace(departmentName) ? "—" : departmentName!,
            ["salary"] = offer.SalaryAmount.ToString("N2", ci),
            ["currency"] = offer.Currency,
            ["salary_frequency"] = offer.SalaryFrequency.ToString(),
            ["start_date"] = offer.StartDate.ToString("yyyy-MM-dd", ci),
            ["expiry_date"] = offer.ExpiryDate.ToString("yyyy-MM-dd", ci),
            ["company_name"] = companyName,
            ["probation"] = offer.ProbationMonths is { } m ? $"{m} month(s)" : "Not applicable",
            ["benefits"] = string.IsNullOrWhiteSpace(offer.BenefitsSummary) ? "As per company policy." : offer.BenefitsSummary!,
            ["custom_clauses"] = string.IsNullOrWhiteSpace(offer.CustomClauses) ? string.Empty : offer.CustomClauses!,
            ["reference_number"] = offer.OfferReferenceNumber,
        };
    }

    /// <summary>
    /// Substitutes {{key}} placeholders in <paramref name="template"/> from <paramref name="variables"/>.
    /// Unknown placeholders are left intact (defensive — a typo'd template key is visible, not silently
    /// dropped). Used for any plain-text substitution; the PDF renderer composes its own structured layout.
    /// </summary>
    public static string Substitute(string template, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        var result = template;
        foreach (var (key, value) in variables)
            result = result.Replace("{{" + key + "}}", value, StringComparison.Ordinal);

        return result;
    }
}
