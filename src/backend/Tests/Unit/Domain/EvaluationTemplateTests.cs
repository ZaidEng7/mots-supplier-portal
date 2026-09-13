// The two scoring-template invariants the tender authoring flow depends on.
//
// The weights must sum to one hundred before a template can be activated, and a template is immutable once a
// tender has referenced it, with forking as the only way to change a referenced version.
//
// Archiving deliberately does not check whether a template is referenced: a referenced, in-use, active template
// must still be archivable once it is no longer wanted for future tenders.
//
// The fork is genuinely editable even though its source was referenced, and the original, still referenced,
// remains untouched and still immutable.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;

public class EvaluationTemplateTests
{
    private static EvaluationTemplate CreateTemplateWithWeights(params decimal[] weights)
    {
        var template = EvaluationTemplate.Create("قالب اختبار", "Test Template");
        foreach (var weight in weights)
        {
            template.AddCriterion(
                $"معيار {weight}", $"Criterion {weight}", CriterionDimension.Technical, weight, 10m,
                threshold: null, ScoringType.Numeric, guidanceAr: null, guidanceEn: null);
        }
        return template;
    }

    [Fact]
    public void Activate_succeeds_when_weights_sum_to_exactly_100()
    {
        var template = CreateTemplateWithWeights(40m, 35m, 25m);

        var act = () => template.Activate();

        act.Should().NotThrow();
        template.Status.Should().Be(EvaluationTemplateStatus.Active);
    }

    [Fact]
    public void Activate_is_rejected_when_weights_do_not_sum_to_100()
    {
        var template = CreateTemplateWithWeights(40m, 35m, 20m); // sums to 95

        var act = () => template.Activate();

        act.Should().Throw<DomainException>()
            .WithMessage("*sum to exactly 100*95*");
        template.Status.Should().Be(EvaluationTemplateStatus.Draft, "a failed activation must not leave the template Active");
    }

    [Fact]
    public void Activate_is_rejected_when_weights_overshoot_100()
    {
        var template = CreateTemplateWithWeights(60m, 60m); // sums to 120

        var act = () => template.Activate();

        act.Should().Throw<DomainException>().WithMessage("*120*");
    }

    [Fact]
    public void Activate_is_rejected_with_no_criteria()
    {
        var template = EvaluationTemplate.Create("قالب فارغ", "Empty Template");

        var act = () => template.Activate();

        act.Should().Throw<DomainException>().WithMessage("*at least one criterion*");
    }

    [Fact]
    public void AddCriterion_rejects_a_threshold_greater_than_max_score()
    {
        var template = EvaluationTemplate.Create("قالب اختبار", "Test Template");

        var act = () => template.AddCriterion(
            "معيار", "Criterion", CriterionDimension.Technical, 50m, maxScore: 10m,
            threshold: 20m, ScoringType.Numeric, null, null);

        act.Should().Throw<DomainException>().WithMessage("*threshold*exceed*");
    }

    [Fact]
    public void Editing_a_referenced_template_is_rejected()
    {
        var template = CreateTemplateWithWeights(100m);
        template.Activate();
        template.MarkReferenced();

        var addAct = () => template.AddCriterion("جديد", "New", CriterionDimension.Commercial, 10m, 10m, null, ScoringType.Numeric, null, null);
        var renameAct = () => template.Rename("اسم جديد", "New Name");
        var archiveAct = () => template.Archive(); // not editing per se, but exercise EnsureEditable's scope: Archive does NOT call EnsureEditable

        addAct.Should().Throw<DomainException>().WithMessage("*immutable*fork*");
        renameAct.Should().Throw<DomainException>().WithMessage("*immutable*fork*");
        archiveAct.Should().NotThrow();
    }

    [Fact]
    public void Forking_a_referenced_template_creates_a_new_independent_editable_version()
    {
        var original = CreateTemplateWithWeights(60m, 40m);
        original.Activate();
        original.MarkReferenced();

        var forked = original.Fork();

        forked.Id.Should().NotBe(original.Id);
        forked.FamilyId.Should().Be(original.FamilyId);
        forked.Version.Should().Be(original.Version + 1);
        forked.Status.Should().Be(EvaluationTemplateStatus.Draft);
        forked.IsReferenced.Should().BeFalse();
        forked.Criteria.Should().HaveCount(2);
        forked.Criteria.Select(c => c.Id).Should().NotIntersectWith(original.Criteria.Select(c => c.Id),
            "forked criteria must be independent rows, not shared with the original version");

        var act = () => forked.AddCriterion("إضافي", "Extra", CriterionDimension.Delivery, 5m, 5m, null, ScoringType.Boolean, null, null);
        act.Should().NotThrow();

        original.Criteria.Should().HaveCount(2);
        var originalStillLocked = () => original.AddCriterion("x", "x", CriterionDimension.Technical, 1m, 1m, null, ScoringType.Numeric, null, null);
        originalStillLocked.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkReferenced_requires_the_template_to_be_active()
    {
        var draft = CreateTemplateWithWeights(100m); // not activated

        var act = () => draft.MarkReferenced();

        act.Should().Throw<DomainException>().WithMessage("*Active*");
    }
}
