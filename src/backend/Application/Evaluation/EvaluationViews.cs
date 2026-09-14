// The shapes an evaluation is read through, on both sides: the buyer's overview and the evaluator's own work.
//
//
// THE ROSTER NEVER CARRIES A SCORE
//
// Scoring is blind, so the buyer-facing roster row says who is assigned and whether they have submitted, and
// never what they scored.
//
// It carries the evaluator's own name, because the screen was printing an internal identifier. A manager
// asking which evaluators are on this tender got a column of raw identifiers, and the button to remove one
// named nobody.
//
//
// THE CANDIDATE LIST
//
// Who a manager may assign. It did not exist, and the screen could not work without it.
//
// Assigning an evaluator was a free-text box for a raw identifier, and the only list of staff in the product
// requires a permission a procurement manager does not hold. So the persona the route names had no way to
// learn the identifier it demanded. Found by walking a tender through in the browser: the step was unusable
// without opening the database.
//
//
// A CONSOLIDATED RESULT
//
// The unresolved-tie flag says this rank came from a tie no rule broke. The award flow refuses to offer the
// top rank while it is set, and the screen has to be able to say why.
//
// The bid's public code is required rather than optional. It used to be optional, and six of the eight
// handlers that build this never looked one up, so most responses carried nothing there beside the internal
// identifier, and the results table fell back to rendering that identifier. Which is the exact failure the
// code was added to stop. Every handler supplies it now, so there is no fallback left to get wrong.
//
// The internal identifier is still on the wire and no longer read by anything. Removing it is a breaking
// change, which the versioning rules and the contract check both refuse, correctly. The screens no longer
// read it and the award request no longer takes it, so the defect is closed; the field's removal belongs to
// whoever opens the next major version.
//
//
// THE BUYER'S OVERVIEW
//
// It deliberately excludes every individual score, and the results are empty until the scores have been
// gathered.

namespace MotsSupplierPortal.Application.Evaluation;

using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;

public sealed record EvaluationCriterionDto(
    Guid Id, string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight, decimal MaxScore, decimal? Threshold, ScoringType ScoringType, bool IsFinancial,
    bool RequiresJustification = false,
    string? GuidanceAr = null, string? GuidanceEn = null);

public sealed record EvaluationAssignmentDto(
    Guid EvaluatorUserId, string? EvaluatorName, DateTimeOffset AssignedAt, DateTimeOffset? SubmittedAt,
    DateTimeOffset? RecusedAt, string? RecusalReason);

public sealed record EvaluatorCandidateDto(Guid UserId, string FullName, string Email);

public sealed record ConsolidatedResultDto(
    Guid ProposalId, string ProposalCode, bool TechnicallyQualified, decimal TechnicalWeightedScore,
    decimal? FinancialWeightedScore, decimal WeightedTotal, int? Rank, bool TieUnresolved = false,
    string? TieResolutionReason = null);

public sealed record EvaluationDto(
    Guid Id, Guid RfqId, string RfqReferenceCode, EvaluationState State,
    IReadOnlyList<EvaluationCriterionDto> Criteria, IReadOnlyList<EvaluationAssignmentDto> Assignments, IReadOnlyList<ConsolidatedResultDto> Results,
    uint RowVersion);

public sealed record MyScoreDto(string ProposalCode, Guid CriterionId, decimal RawScore, string? CommentAr, string? CommentEn, DateTimeOffset ScoredAt);

public sealed record EvaluatorProposalDto(
    string ProposalCode,
    string BidderLabelAr,
    string BidderLabelEn,
    string? SupplierReferenceCode,
    string? SupplierDisplayNameAr,
    string? SupplierDisplayNameEn,
    string? NarrativeAr,
    string? NarrativeEn,
    IReadOnlyList<RequirementAnswerDto> RequirementAnswers,
    IReadOnlyList<EvaluatorProposalDocumentDto> Documents,
    bool TechnicallyQualified);

public sealed record EvaluatorProposalDocumentDto(
    Guid Id, string OriginalFileName, string ContentType, string? Caption, DateTimeOffset UploadedAt);

public sealed record MyEvaluationDto(
    string RfqReferenceCode, EvaluationState State,
    string RfqTitleAr, string RfqTitleEn, string? RfqDescriptionAr, string? RfqDescriptionEn,
    IReadOnlyList<RfqItemDto> RfqItems, IReadOnlyList<RequirementDto> RfqRequirements,
    DateTimeOffset? SubmittedAt, IReadOnlyList<EvaluationCriterionDto> Criteria,
    IReadOnlyList<EvaluatorProposalDto> Proposals,
    IReadOnlyList<MyScoreDto> MyScores);

public sealed record ConflictDeclarationDto(
    bool DeclarationRequired,
    IReadOnlyList<DeclarationBidderDto> Bidders);

public sealed record DeclarationBidderDto(string ProposalCode, string SupplierDisplayNameAr, string SupplierDisplayNameEn);
