namespace MotsSupplierPortal.Application.Evaluations;

/// <summary>
/// One row of SCR-500, the evaluator's own dashboard.
///
/// <para>Progress is scores-recorded over scores-expected for THIS evaluator: the number of
/// (submitted proposal × technical criterion) pairs they are asked to score, and how many they have.
/// A percentage alone would hide whether "80%" is four of five criteria or forty of fifty.</para>
/// </summary>
public sealed record MyAssignmentDto(
    string RfqReferenceCode,
    string RfqTitleAr,
    string RfqTitleEn,
    string EvaluationState,
    /// <summary>§3.1's own field: the date the evaluation is expected to be done by. Null when the
    /// RFQ never set one - shown as "no date" rather than invented.</summary>
    DateTimeOffset? EvaluationTargetDate,
    DateTimeOffset AssignedAt,
    DateTimeOffset? SubmittedAt,
    int ScoresRecorded,
    int ScoresExpected,
    /// <summary>Which of IA §4.3's three tabs this assignment belongs in.</summary>
    string Tab);

/// <summary>IA §4.3: "My Evaluations → tabs `Assigned · In Progress · Submitted`".</summary>
public static class MyAssignmentTabs
{
    public const string Assigned = "Assigned";
    public const string InProgress = "InProgress";
    public const string Submitted = "Submitted";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Assigned, InProgress, Submitted };
}

public interface IListMyAssignmentsHandler
{
    Task<IReadOnlyList<MyAssignmentDto>> HandleAsync(string? tab, CancellationToken ct);
}
