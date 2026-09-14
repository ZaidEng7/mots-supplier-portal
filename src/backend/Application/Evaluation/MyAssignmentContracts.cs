// The vocabulary for the evaluator's own dashboard: the evaluations assigned to them, across tenders.
//
// Progress is scores recorded over scores expected for this evaluator: how many pairs of bid and technical
// criterion they are asked to score, and how many they have done.
//
// It is two numbers rather than a percentage, because a percentage alone hides whether four fifths means four
// of five criteria or forty of fifty.
//
// The target date is the date the evaluation is expected to be finished by, and it is absent when the tender
// never set one. Shown as no date rather than invented.
//
// Each assignment belongs to one of three tabs: assigned, in progress, and submitted.

namespace MotsSupplierPortal.Application.Evaluation;

public sealed record MyAssignmentDto(
    string RfqReferenceCode,
    string RfqTitleAr,
    string RfqTitleEn,
    string EvaluationState,
    DateTimeOffset? EvaluationTargetDate,
    DateTimeOffset AssignedAt,
    DateTimeOffset? SubmittedAt,
    int ScoresRecorded,
    int ScoresExpected,
    string Tab);

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
