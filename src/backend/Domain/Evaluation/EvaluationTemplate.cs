using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Domain.Evaluation;

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

/// <summary>FEAT-11.1/FR-ADM-005, pulled forward ahead of EPIC-11's own phase because EPIC-07
/// needs a real template to bind an RFQ to (docs/architecture/DOMAIN-MODEL.md §5.6). Portal-only -
/// no ERP sync markers (DOMAIN-MODEL.md §3's aggregate catalogue lists EvaluationTemplate's
/// "ERP-synced" column as No).
///
/// <para><b>Versioning/immutability (DOMAIN-MODEL.md §5.6: "A template referenced by any live RFQ
/// is immutable; edits produce a new version").</b> Each version is its own row with its own Id -
/// not a single row whose Version column increments in place. <see cref="FamilyId"/> groups every
/// version of "the same" template together; <see cref="Version"/> is 1 at first creation and
/// increments on <see cref="Fork"/>. Once <see cref="IsReferenced"/> is set (an RFQ has bound to
/// this exact Id+Version), every edit method throws - the caller must <see cref="Fork"/> a new
/// version and edit that instead. This is deliberately a hard reject, not a silent auto-fork:
/// the caller (RFQ authoring) decides whether forking is what it wants, the template aggregate only
/// enforces that the referenced row itself can never change under a live RFQ.</para>
/// </summary>
public sealed class EvaluationTemplate
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

    /// <summary>Refuses any edit once this exact version has been bound to a live RFQ - the caller
    /// must <see cref="Fork"/> a new version instead of mutating a row an RFQ already snapshotted
    /// from.</summary>
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
        decimal? threshold, ScoringType scoringType, string? guidanceAr, string? guidanceEn)
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
            SortOrder = _criteria.Count,
        };
        _criteria.Add(criterion);
        return criterion;
    }

    public void UpdateCriterion(
        Guid criterionId, string nameAr, string nameEn, CriterionDimension dimension, decimal weight,
        decimal maxScore, decimal? threshold, ScoringType scoringType, string? guidanceAr, string? guidanceEn)
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
    }

    public void RemoveCriterion(Guid criterionId)
    {
        EnsureEditable();
        var criterion = _criteria.FirstOrDefault(c => c.Id == criterionId)
            ?? throw new DomainException("Criterion not found.");
        _criteria.Remove(criterion);
    }

    /// <summary>BRULE-065/DOMAIN-MODEL.md §5.6: sum of Criterion.weight across the template must
    /// equal exactly 100 before it can become Active. Exact equality, not a tolerance band - weight
    /// is `numeric(5,2)` (DATABASE-MODEL.md §2.5), so decimal arithmetic here is exact as long as
    /// inputs are, and a template that doesn't sum to 100 is a real authoring error the caller
    /// should fix, not round past.</summary>
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

    /// <summary>Called by the RFQ-binding handler, in the same unit of work as the RFQ's own save,
    /// when an RFQ binds to this exact template version. Deliberately does not check
    /// <see cref="EnsureEditable"/> - marking a template referenced is not itself an edit, it is
    /// what makes future edits illegal.</summary>
    public void MarkReferenced()
    {
        if (Status != EvaluationTemplateStatus.Active)
        {
            throw new DomainException($"Only an 'Active' template can be bound to an RFQ; this template is '{Status}'.");
        }

        IsReferenced = true;
    }

    /// <summary>Creates a new, independent, editable version in the same family - the only way to
    /// change a template once <see cref="IsReferenced"/> is true. The new version starts from this
    /// version's criteria (deep-copied, new Ids) and its own Draft/unreferenced lifecycle; it must
    /// be edited and re-activated independently, and existing RFQs keep referencing the exact old
    /// Id+Version they originally bound to (RFQ holds its own frozen JSON snapshot besides the
    /// FK - see DATABASE-MODEL.md §2.3 `evaluation_template_snapshot`).</summary>
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
