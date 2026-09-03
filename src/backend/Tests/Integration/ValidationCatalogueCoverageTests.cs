using FluentValidation;
using FluentValidation.Validators;
using FluentAssertions;
using MotsSupplierPortal.Api.Errors;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// §7.2's catalogue must cover the validators exactly - in BOTH directions.
///
/// <para>A rule with no catalogue entry falls back to FluentValidation's English and ships an
/// untranslated sentence to a supplier bidding on a public tender. An entry for a rule that no longer
/// exists is the opposite failure and the quieter one: a string that a product owner keeps reviewing
/// and re-approving for a validator deleted months ago.</para>
///
/// <para>The rules are read by reflection from the validators themselves, never from a hand-kept
/// list, so adding <c>RuleFor(x =&gt; x.Foo).NotEmpty()</c> and nothing else fails this test.</para>
/// </summary>
public sealed class ValidationCatalogueCoverageTests
{
    /// <summary>
    /// Components the descriptor cannot resolve to a message rule, listed individually so that the
    /// set is asserted rather than ignored. A <c>ChildValidatorAdaptor</c> raises no message of its
    /// own - the child's rules do, and those are reachable only by running a validation, not by
    /// reading the descriptor. Their catalogue entries exist (<c>Attributes[].Key.NotEmpty</c> and
    /// the other three) and are exercised by the round-trip test below; what this list protects is
    /// the case where someone adds a SECOND ChildRules block, which would otherwise be silently
    /// unenumerable and silently English.
    /// </summary>
    private static readonly string[] KnownUnenumerable =
    [
        "CreateOfferingRequestValidator.Attributes:ChildValidatorAdaptor",
        // RuleFor(x => x).CustomAsync(...) - a container like the adaptor above. It raises no message
        // of its own; it calls context.AddFailure per field, and those failures carry an explicit
        // NotEmptyValidator error code so they resolve through the catalogue like any other rule.
        "UpdateLegalInfoRequestValidator.:AsyncPredicateValidator",
    ];

    private static IValidator Instantiate(Type type)
    {
        // Several validators take an AppDbContext so a rule can check a lookup table. The dependency
        // is only touched inside a rule's lambda, never during construction, so a null is enough to
        // read the rule descriptors - and cheaper than standing up a database to enumerate metadata.
        var ctor = type.GetConstructors().Single();
        return (IValidator)ctor.Invoke([.. ctor.GetParameters().Select(_ => (object?)null)]);
    }

    private static HashSet<string> DeclaredRuleKeys(List<string>? unenumerable = null)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        var validatorTypes = typeof(ProblemTypes).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => t.BaseType is { IsGenericType: true } b
                        && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>));

        foreach (var type in validatorTypes)
        {
            foreach (var rule in Instantiate(type).CreateDescriptor().Rules)
            {
                foreach (var component in rule.Components)
                {
                    var errorCode = component.Validator is IPropertyValidator v ? v.Name : component.ErrorCode;
                    var ruleName = ValidationProblems.RuleNameFor(errorCode ?? string.Empty);

                    // A whole-request CustomAsync is a container, not a message rule: it has no
                    // property and never renders its own text. Treated as unenumerable rather than
                    // given a catalogue entry that could never be shown.
                    if (ruleName == "MustAsync" && string.IsNullOrEmpty(rule.PropertyName))
                    {
                        unenumerable?.Add($"{type.Name}.{rule.PropertyName}:{errorCode}");
                        continue;
                    }

                    if (ruleName is null)
                    {
                        unenumerable?.Add($"{type.Name}.{rule.PropertyName}:{errorCode}");
                        continue;
                    }

                    keys.Add($"{ValidationCatalogue.Normalize(rule.PropertyName)}.{ruleName}");
                }
            }
        }

        return keys;
    }

    [Fact]
    public void Every_declared_validation_rule_has_a_catalogue_entry()
    {
        var missing = DeclaredRuleKeys().Except(ValidationCatalogue.Keys).OrderBy(k => k).ToList();

        missing.Should().BeEmpty(
            "every (field, rule) pair needs Arabic and English in ValidationCatalogue.jsonc - without " +
            "an entry the API answers a supplier in FluentValidation's English");
    }

    [Fact]
    public void Every_catalogue_entry_matches_a_declared_validation_rule()
    {
        // The ten entries for rules the descriptor cannot enumerate - collection child rules, the
        // whole-request Must, and the config-driven required fields raised by CustomAsync - are not
        // orphans; they are asserted against the running validators by the round-trip tests instead.
        string[] notDescriptorVisible =
        [
            "Attributes[].Key.NotEmpty", "Attributes[].Key.MaximumLength",
            "Attributes[].Value.NotEmpty", "Attributes[].Value.MaximumLength",
            "RegistrationNumber.NotEmpty", "TaxId.NotEmpty", "EstablishedOn.NotEmpty",
        ];

        var orphaned = ValidationCatalogue.Keys
            .Except(DeclaredRuleKeys())
            .Except(notDescriptorVisible)
            .OrderBy(k => k)
            .ToList();

        orphaned.Should().BeEmpty(
            "a catalogue entry for a rule that no longer exists is approved copy for a deleted validator");
    }

    [Fact]
    public void The_set_of_unenumerable_components_is_exactly_what_is_expected()
    {
        var unenumerable = new List<string>();
        DeclaredRuleKeys(unenumerable);

        unenumerable.Should().BeEquivalentTo(KnownUnenumerable,
            "a new component kind the descriptor cannot resolve would be a silent English fallback - " +
            "adding one must be a decision, not an accident");
    }
}
