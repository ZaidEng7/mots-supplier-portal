// A scoring template: the criteria, their weights and their thresholds that a tender is evaluated
// against.
//
// It lives only in this portal and is never synced to the ministry's finance system.
//
// Three enums sit alongside it. Status is where a template is in its life: a draft being written, an
// active one that tenders may bind to, or an archived one that is out of use. Dimension is which part
// of a bid a criterion judges, and the commercial dimension is what marks a criterion as the financial
// envelope. ScoringType is how a score is given: a number, a scale, a yes-or-no, or a formula.
//
//
// VERSIONING, which is the point of this design
//
// A template referenced by a live tender can never change. Editing produces a new version instead.
//
// Each version is its own row with its own identifier, not one row whose version number is bumped in
// place. FamilyId groups every version of the same template together, and Version starts at 1 and
// increases when the template is forked.
//
// Once IsReferenced is set, meaning some tender has bound to this exact version, every editing method
// refuses. The caller has to fork a new version and edit that instead.
//
// The refusal is deliberately hard rather than an automatic fork behind the caller's back. Whoever is
// authoring decides whether forking is what they want; this record only guarantees that the referenced
// row itself can never change underneath a live tender.
//
// Fork produces a new, independent, editable version in the same family. It starts from this version's
// criteria, copied with new identifiers, and begins its own life as an unreferenced draft, so it has to
// be edited and activated on its own. Existing tenders keep pointing at the exact version they
// originally bound to, and each of them also holds its own frozen copy of it besides the link.
//
// MarkReferenced is called by the tender-binding handler, in the same save as the tender's own. It
// deliberately does not check editability, because marking a template as referenced is not an edit; it
// is what makes future edits illegal. Only an active template can be bound.
//
//
// ACTIVATION
//
// A template needs at least one criterion, and the weights must sum to exactly 100 before it can
// become active.
//
// Exactly 100, not a tolerance band. Weights are stored as decimals with two places, so the arithmetic
// here is exact as long as the inputs are, and a template that does not sum to 100 is a real authoring
// mistake for the author to fix rather than something to round past.
//
// Archiving is only possible from active.

namespace MotsSupplierPortal.Domain.Evaluation;

using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Suppliers;

public enum EvaluationTemplateStatus
{
    Draft,
    Active,
    Archived,
}

public enum CriterionDimension
{
    Technical,
    Commercial,
    Compliance,
    Delivery,
}

public enum ScoringType
{
    Numeric,
    Scale,
    Boolean,
    Formula,
}

public sealed class EvaluationTemplate : IVersionedAggregate
{
    private readonly List<Criterion> _criteria = [];

    public Guid Id { get; private init; }
    public Guid FamilyId { get; private init; }
    public int Version { get; private init; }
    public string NameAr { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public EvaluationTemplateStatus Status { get; private set; } = EvaluationTemplateStatus.Draft;
    public bool IsReferenced { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<Criterion> Criteria => _criteria;

    private EvaluationTemplate() { }

    public static EvaluationTemplate Create(string nameAr, string nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr)) throw new DomainException("Template name (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Template name (English) is required.");

        return new EvaluationTemplate
        {
            Id = Guid.CreateVersion7(),
            FamilyId = Guid.CreateVersion7(),
            Version = 1,
            NameAr = nameAr,
            NameEn = nameEn,
            Status = EvaluationTemplateStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private void EnsureEditable()
    {
        if (IsReferenced)
        {
            throw new DomainException(
                "This template version is already referenced by an RFQ and is immutable; fork a new version to make changes.");
        }
    }

    public void Rename(string nameAr, string nameEn)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(nameAr)) throw new DomainException("Template name (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Template name (English) is required.");
        NameAr = nameAr;
        NameEn = nameEn;
    }

    public Criterion AddCriterion(
        string nameAr, string nameEn, CriterionDimension dimension, decimal weight, decimal maxScore,
        decimal? threshold, ScoringType scoringType, string? guidanceAr, string? guidanceEn,
        bool requiresJustification = false)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(nameAr)) throw new DomainException("Criterion name (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Criterion name (English) is required.");
        if (weight is <= 0 or > 100) throw new DomainException("Criterion weight must be between 0 and 100.");
        if (maxScore <= 0) throw new DomainException("Criterion max score must be positive.");
        if (threshold is not null && threshold > maxScore)
        {
            throw new DomainException("Criterion threshold cannot exceed its max score.");
        }

        var criterion = new Criterion
        {
            Id = Guid.CreateVersion7(),
            EvaluationTemplateId = Id,
            NameAr = nameAr,
            NameEn = nameEn,
            Dimension = dimension,
            Weight = weight,
            MaxScore = maxScore,
            Threshold = threshold,
            ScoringType = scoringType,
            GuidanceAr = guidanceAr,
            GuidanceEn = guidanceEn,
            RequiresJustification = requiresJustification,
            SortOrder = _criteria.Count,
        };
        _criteria.Add(criterion);
        return criterion;
    }

    public void UpdateCriterion(
        Guid criterionId, string nameAr, string nameEn, CriterionDimension dimension, decimal weight,
        decimal maxScore, decimal? threshold, ScoringType scoringType, string? guidanceAr, string? guidanceEn,
        bool requiresJustification = false)
    {
        EnsureEditable();
        var criterion = _criteria.FirstOrDefault(c => c.Id == criterionId)
            ?? throw new DomainException("Criterion not found.");
        if (string.IsNullOrWhiteSpace(nameAr)) throw new DomainException("Criterion name (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Criterion name (English) is required.");
        if (weight is <= 0 or > 100) throw new DomainException("Criterion weight must be between 0 and 100.");
        if (maxScore <= 0) throw new DomainException("Criterion max score must be positive.");
        if (threshold is not null && threshold > maxScore)
        {
            throw new DomainException("Criterion threshold cannot exceed its max score.");
        }

        criterion.NameAr = nameAr;
        criterion.NameEn = nameEn;
        criterion.Dimension = dimension;
        criterion.Weight = weight;
        criterion.MaxScore = maxScore;
        criterion.Threshold = threshold;
        criterion.ScoringType = scoringType;
        criterion.GuidanceAr = guidanceAr;
        criterion.GuidanceEn = guidanceEn;
        criterion.RequiresJustification = requiresJustification;
    }

    public void RemoveCriterion(Guid criterionId)
    {
        EnsureEditable();
        var criterion = _criteria.FirstOrDefault(c => c.Id == criterionId)
            ?? throw new DomainException("Criterion not found.");
        _criteria.Remove(criterion);
    }

    public void Activate()
    {
        EnsureEditable();
        if (_criteria.Count == 0)
        {
            throw new DomainException("A template needs at least one criterion before it can be activated.");
        }

        var totalWeight = _criteria.Sum(c => c.Weight);
        if (totalWeight != 100m)
        {
            throw new DomainException(
                $"Criterion weights must sum to exactly 100 before activation; current total is {totalWeight}.");
        }

        Status = EvaluationTemplateStatus.Active;
    }

    public void Archive()
    {
        if (Status != EvaluationTemplateStatus.Active)
        {
            throw new DomainException($"Cannot archive from status '{Status}'; only 'Active' is valid.");
        }

        Status = EvaluationTemplateStatus.Archived;
    }

    public void MarkReferenced()
    {
        if (Status != EvaluationTemplateStatus.Active)
        {
            throw new DomainException($"Only an 'Active' template can be bound to an RFQ; this template is '{Status}'.");
        }

        IsReferenced = true;
    }

    public EvaluationTemplate Fork()
    {
        var forked = new EvaluationTemplate
        {
            Id = Guid.CreateVersion7(),
            FamilyId = FamilyId,
            Version = Version + 1,
            NameAr = NameAr,
            NameEn = NameEn,
            Status = EvaluationTemplateStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var c in _criteria)
        {
            forked._criteria.Add(new Criterion
            {
                Id = Guid.CreateVersion7(),
                EvaluationTemplateId = forked.Id,
                NameAr = c.NameAr,
                NameEn = c.NameEn,
                Dimension = c.Dimension,
                Weight = c.Weight,
                MaxScore = c.MaxScore,
                Threshold = c.Threshold,
                ScoringType = c.ScoringType,
                GuidanceAr = c.GuidanceAr,
                GuidanceEn = c.GuidanceEn,
                SortOrder = c.SortOrder,
            });
        }

        return forked;
    }
}
