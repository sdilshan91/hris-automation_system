namespace HRM.Domain.Enums;

/// <summary>
/// Delivery mode of a training course (US-TRN-001 FR-1). Stored as a string column.
/// </summary>
public enum TrainingMode
{
    InPerson = 0,
    Online = 1,
    Hybrid = 2,
}
