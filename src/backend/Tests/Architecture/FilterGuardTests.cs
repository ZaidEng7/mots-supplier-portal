// Every text filter a read endpoint declares must reach a guard, never be used raw.
//
//
// WHY THIS RULE AND NOT THE ONE THAT WAS NEARLY WRITTEN
//
// An earlier review reported that binding a filter to a nullable value type made a malformed value bind to
// nothing and silently widen the result.
//
// That was inferred from the signature and never reproduced, and it is false: the framework refuses a value it
// cannot bind, so those parameters were always rejected. A check over nullable-bound parameters would have
// guarded a mechanism that does not exist.
//
//
// THE CLASS THAT DID WIDEN IS TEXT-TYPED
//
// A text filter binds anything at all, so a value the handler does not recognise falls out of its parse chain
// having applied no condition, and an empty condition is an absent filter, which returns everything.
//
// A misspelt state returned the whole review queue. A garbage assignee did the same. A misspelt record type
// returned the entire audit trail. Those were REPRODUCED.
//
// The class was then known, fixed in three places, and backlogged, and then reproduced again in newly written
// code. That is the argument for a check rather than another backlog entry.
//
//
// WHAT THIS READS, AND WHAT IT DELIBERATELY CANNOT SEE
//
// Source, matched syntactically: a read-endpoint mapping whose last argument is a lambda, the nullable text
// parameters that lambda declares, and whether the shared filter vocabulary is mentioned anywhere in its body.
//
// There is no semantic model here, so the match is on spelling. A filter guarded through some other helper
// would be reported, and a lambda that mentions the vocabulary without guarding THIS parameter would pass.
//
// Both are deliberate. The rule is "reach for the guard", and a check strict enough to prove which parameter a
// guard covers would need dataflow analysis and would fail on the first legitimate refactor.
//
//
// THE NON-VACUITY ASSERTION
//
// A walk that matched nothing, because of a renamed folder, a changed lambda shape, or a parser version that
// reads differently, would pass in silence. That is exactly how other instruments in this project came to
// measure nothing, so the count of what was found is asserted too.
//
//
// THE TWO EXEMPTION LISTS, AND WHY EVERY ENTRY IS ARGUED FOR
//
// Not a filter at all: the paging cursor. It is opaque to the handler by design, because an unreadable cursor
// is documented as "start from the beginning" rather than an error, so there is nothing for a guard to reject.
//
// The sort parameter was on that list too and was REMOVED by the stale-exemption check on its first run,
// correctly. No handler binds it at all, because the shared list filter whitelists it against the endpoint's
// policy from the raw query string before binding, so exempting it was exempting nothing and would have
// silently covered any future parameter of that name. That is exactly the rot the check exists to catch, and
// it caught it in the list it shipped with.
//
// Deliberately free-form, each for a stated reason. The record type and the action are compared for equality
// against a column, so an unrecognised value NARROWS to zero rather than widening to everything, which is the
// opposite of the failure this check exists for; a whitelist would also mean maintaining a vocabulary of some
// thirty audited action strings that grows with every feature and goes stale silently. The field-config
// category and the offering category were found by this check on its first run and are safe for the same
// reason.
//
// Two free-text search terms have no vocabulary to check against. The catalogue search is interpolated into a
// pattern without escaping its wildcards, which is recorded rather than fixed here because the same endpoint
// with no term returns every active entry anyway, so the caller is already entitled to the unfiltered list; it
// becomes a disclosure the day that endpoint is scoped by row or paged by relevance. The cross-entity search
// is different and safer: a blank or operator-only term returns NOTHING rather than everything, asserted both
// ways in its own tests, and the query operators are stripped from every token rather than executed, so a
// caller searching for an ampersand is not issuing a boolean expression by accident.
//
//
// THE STALE-EXEMPTION CHECK
//
// The allow-list is the part that rots. An exemption for a parameter nobody declares any more is an exemption
// nobody is reading, and the next person to add a filter of that name inherits a hole they did not know was
// there.
//
// Failing on a STALE entry is what stops the list growing quietly, because the only way to keep it green is to
// delete what is no longer true.
//
// Exempt parameters are therefore RECORDED by the scan and filtered at the assertion rather than skipped
// during it. Skipping them made this check structurally unable to see them: it reported the cursor as naming
// nothing, on a codebase where every list endpoint declares one.
//
//
// FINDING THE SOURCE
//
// It walks up from the test binaries to the solution file, so this works from the command line, from an
// editor, and in continuous integration without any of them agreeing on a working directory.

namespace MotsSupplierPortal.Tests.Architecture;

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed class FilterGuardTests
{
    private static readonly HashSet<string> NotFilters = new(StringComparer.Ordinal) { "cursor" };

    private static readonly Dictionary<string, string> DeliberatelyUnguarded = new(StringComparer.Ordinal)
    {
        ["aggregateType"] = "compared with == against a column; a typo narrows to zero rather than widening",
        ["action"] = "same - and the vocabulary is ~30 strings that grows with every feature",

        ["category"] = "field-config: == against SupplierFieldConfig.Category; a typo narrows to zero",
        ["categoryCode"] = "offering search: == against Offering.CategoryCode; a typo narrows to zero",

        ["query"] = "free-text ILIKE search; no vocabulary to validate against, and unfiltered is already the default",

        ["q"] = "free-text tsquery search; a blank or unparseable term returns NOTHING, never everything",

        ["modifiedSince"] = "ministry feeds: parsed as a timestamp and REFUSED with 400 when it will not parse, "
            + "so an unreadable value never becomes an absent filter - which is the widening this check exists "
            + "to prevent. Asserted in MinistryFeedIncrementalTests.",

    };

    private sealed record FilterParameter(string File, int Line, string Endpoint, string Name, bool Guarded);

    [Fact]
    public void Every_string_filter_on_a_GET_endpoint_reaches_a_guard()
    {
        var found = ScanEndpoints();

        found.Should().HaveCountGreaterThan(10,
            "the walk must actually be finding filters; a check that inspects nothing always passes");

        var unguarded = found
            .Where(f => !f.Guarded)
            .Where(f => !NotFilters.Contains(f.Name) && !DeliberatelyUnguarded.ContainsKey(f.Name))
            .ToList();

        unguarded.Should().BeEmpty(
            "a string filter with no guard binds anything, and an unrecognised value that applies no " +
            "predicate is an ABSENT filter, which returns everything:\n" +
            string.Join("\n", unguarded.Select(u => $"  {u.File}:{u.Line}  {u.Endpoint} -> '{u.Name}'")));
    }

    [Fact]
    public void Every_exemption_still_names_a_parameter_that_exists()
    {
        var declared = ScanEndpoints().Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

        var stale = NotFilters.Concat(DeliberatelyUnguarded.Keys)
            .Where(name => !declared.Contains(name))
            .ToList();

        stale.Should().BeEmpty(
            "these exemptions no longer match any string filter on any GET endpoint, so they are " +
            $"exempting nothing and hiding whatever is added under those names next: {string.Join(", ", stale)}");
    }

    private static List<FilterParameter> ScanEndpoints()
    {
        var results = new List<FilterParameter>();

        foreach (var file in Directory.EnumerateFiles(EndpointsDirectory(), "*.cs"))
        {
            var text = File.ReadAllText(file);
            var root = CSharpSyntaxTree.ParseText(text).GetRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "MapGet" })
                {
                    continue;
                }

                var lambda = invocation.ArgumentList.Arguments
                    .Select(a => a.Expression)
                    .OfType<ParenthesizedLambdaExpressionSyntax>()
                    .LastOrDefault();

                if (lambda?.ParameterList is null) continue;

                var guarded = lambda.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Any(m => m.Expression is IdentifierNameSyntax { Identifier.ValueText: "FilterValues" });

                var route = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() ?? "(route?)";

                foreach (var parameter in lambda.ParameterList.Parameters)
                {
                    if (!IsNullableString(parameter.Type)) continue;

                    var line = parameter.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    results.Add(new FilterParameter(
                        Path.GetFileName(file), line, route.Trim('"'), parameter.Identifier.ValueText, guarded));
                }
            }
        }

        return results;
    }

    private static bool IsNullableString(TypeSyntax? type) =>
        type is NullableTypeSyntax nullable
        && (nullable.ElementType is PredefinedTypeSyntax { Keyword.ValueText: "string" }
            || nullable.ElementType is IdentifierNameSyntax { Identifier.ValueText: "String" });

    private static string EndpointsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MotsSupplierPortal.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the check cannot find the backend solution root from the test binaries");

        var endpoints = Path.Combine(directory!.FullName, "Api", "Endpoints");
        Directory.Exists(endpoints).Should().BeTrue($"endpoints are expected at {endpoints}");
        return endpoints;
    }
}
