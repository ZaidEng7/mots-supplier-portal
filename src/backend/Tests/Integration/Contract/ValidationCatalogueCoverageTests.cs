// The message catalogue must cover the validators exactly, in BOTH directions.
//
// A rule with no entry falls back to the library's English and ships an untranslated sentence to a supplier bidding
// on a public tender.
//
// An entry for a rule that no longer exists is the opposite failure and the quieter one: a string a product owner
// keeps reviewing and re-approving for a validator deleted months ago.
//
// The rules are read by reflection from the validators themselves, never from a hand-kept list, so adding one rule
// and nothing else fails this test.
//
//
// THE UNENUMERABLE COMPONENTS ARE LISTED INDIVIDUALLY, SO THE SET IS ASSERTED RATHER THAN IGNORED
//
// A child-validator adaptor raises no message of its own: the child's rules do, and those are reachable only by
// running a validation rather than by reading the metadata. Their entries exist and are exercised by the round-trip
// tests.
//
// What the list protects is the case where somebody adds a SECOND block of child rules, which would otherwise be
// silently unenumerable and silently English.
//
// A whole-request custom rule is a container in the same way. It has no property and never renders its own text, so
// it is treated as unenumerable rather than given an entry that could never be shown; the failures it adds carry
// an explicit error code, so they resolve through the catalogue like any other rule.
//
// The entries for those rules are not orphans. They are asserted against the running validators by the round-trip
// tests instead.
//
// Several validators take a database context so a rule can check a lookup table. The dependency is only touched
// inside a rule's body and never during construction, so an empty one is enough to read the metadata, and cheaper
// than standing up a database to enumerate it.

namespace MotsSupplierPortal.Tests.Integration.Contract;

using FluentValidation;
using FluentValidation.Validators;
using FluentAssertions;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Tests.Integration;

public sealed class ValidationCatalogueCoverageTests
{
    private static readonly string[] KnownUnenumerable =
    [
        "CreateOfferingRequestValidator.Attributes:ChildValidatorAdaptor",
        "UpdateLegalInfoRequestValidator.:AsyncPredicateValidator",
    ];

    private static IValidator Instantiate(Type type)
    {
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
