namespace HRM.Domain.Enums;

/// <summary>
/// Exit-interview question types (US-ONB-006 FR-1). The questionnaire template is configurable per tenant
/// (BR-6); each question carries one of these answer shapes. Stored as a string column. The FE contract uses
/// the snake_case wire values "rating" | "multiple_choice" | "free_text" | "yes_no".
/// </summary>
public enum ExitInterviewQuestionType
{
    /// <summary>1-5 rating scale (FR-1). Answer goes in <c>rating</c>.</summary>
    Rating = 0,

    /// <summary>Single selection from a fixed option list (FR-1). Answer goes in <c>selected_option</c>.</summary>
    MultipleChoice = 1,

    /// <summary>Free-text answer (FR-1, max 2000 chars). Answer goes in <c>free_text</c>.</summary>
    FreeText = 2,

    /// <summary>Yes/No answer (FR-1). Stored in <c>selected_option</c> as "yes"/"no".</summary>
    YesNo = 3,
}
