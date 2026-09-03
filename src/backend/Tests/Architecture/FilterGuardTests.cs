using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MotsSupplierPortal.Tests.Architecture;

/// <summary>
/// Every string-typed filter a GET endpoint declares must be parsed or validated, never used raw.
///
/// <para><b>Why this rule and not the one that was nearly written.</b> EPIC-19 part 1 reported that
/// binding a filter to a nullable value type (<c>DateTimeOffset?</c>, <c>Guid?</c>, <c>bool?</c>)
/// made a malformed value bind to null and silently widen the result. That was INFERRED from the
/// signature and never reproduced, and it is false: minimal APIs throw
/// <c>BadHttpRequestException</c> on a value they cannot bind, so those parameters were always
/// refused. A check over nullable-bound parameters would therefore have guarded a mechanism that
/// does not exist.</para>
///
/// <para><b>The class that did widen is string-typed.</b> A <c>string?</c> filter binds anything at
/// all, so a value the handler does not recognise falls out of its parse chain having applied no
/// predicate - and an empty predicate is an absent filter, which returns everything.
/// <c>?state=Approvd</c> returned the whole review queue; <c>?assignedTo=grbage</c> did the same;
/// <c>?aggregateTyp=X</c> returned the entire audit trail. Those were REPRODUCED. The class was then
/// known, fixed in three places, backlogged - and reproduced again afterwards in newly written code,
/// which is the argument for a check rather than another backlog entry.</para>
///
/// <para><b>What this reads.</b> Source, matched syntactically: an invocation named
/// <c>MapGet</c> whose last argument is a lambda, the <c>string?</c> parameters that lambda
/// declares, and whether <c>FilterValues.*</c> appears anywhere in its body. There is no semantic
/// model here, so the match is on spelling: a filter guarded through some other helper would be
/// reported, and a lambda that mentions <c>FilterValues</c> without guarding THIS parameter would
/// pass. Both are deliberate - the rule is "reach for the guard", and a check strict enough to prove
/// which parameter a guard covers would need dataflow analysis and would fail on the first
/// legitimate refactor.</para>
/// </summary>
public sealed class FilterGuardTests
{
    /// <summary>
    /// Parameters that are not filters at all. §6.1's <c>cursor</c> is opaque to the handler by
    /// design: an unreadable cursor is documented as "start from the beginning" rather than an
    /// error, so there is nothing for a guard to reject.
    ///
    /// <para><c>sort</c> was here too and was REMOVED by the stale-exemption check on its first
    /// run, correctly. No handler binds <c>sort</c> at all - ListQueryFilter whitelists it against
    /// the endpoint's policy from the raw query string, before binding - so exempting it was
    /// exempting nothing, and it would have silently covered any future parameter that happened to
    /// be called <c>sort</c>. That is exactly the rot the check exists to catch, and it caught it in
    /// the list it shipped with.</para>
    /// </summary>
    private static readonly HashSet<string> NotFilters = new(StringComparer.Ordinal) { "cursor" };

    /// <summary>
    /// Filters that are deliberately free-form, each with the reason it is safe to leave open.
    ///
    /// <para>Both are compared with <c>==</c> against a column, so an unrecognised value NARROWS to
    /// zero rather than widening to everything - the opposite of the failure this check exists for.
    /// A whitelist here would also mean maintaining a vocabulary of some thirty audited action
    /// strings that grows with every feature and goes stale silently.</para>
    /// </summary>
    private static readonly Dictionary<string, string> DeliberatelyUnguarded = new(StringComparer.Ordinal)
    {
        ["aggregateType"] = "compared with == against a column; a typo narrows to zero rather than widening",
        ["action"] = "same - and the vocabulary is ~30 strings that grows with every feature",

        // Found by this check on its first run, and safe for the same reason: each is compared with
        // == against a column, so a value nobody recognises returns nothing rather than everything.
        ["category"] = "field-config: == against SupplierFieldConfig.Category; a typo narrows to zero",
        ["categoryCode"] = "offering search: == against Offering.CategoryCode; a typo narrows to zero",

        // A free-text search term rather than a filter with a vocabulary - there is no set of valid
        // values to check it against. Worth recording what was looked at and dismissed: the term is
        // interpolated into an ILIKE pattern without escaping % or _, so `?query=%` matches every
        // row. That is not a disclosure here, because the same endpoint with NO query returns every
        // active offering anyway - the caller is already entitled to the unfiltered list. It would
        // become one the day this endpoint is row-scoped or paginated by relevance.
        ["query"] = "free-text ILIKE search; no vocabulary to validate against, and unfiltered is already the default",
    };

    private sealed record FilterParameter(string File, int Line, string Endpoint, string Name, bool Guarded);

    // Exempt parameters are RECORDED by the scan and filtered at the assertion, not skipped during
    // it. Skipping them made the stale-exemption test structurally unable to see them: it reported
    // `cursor` as naming nothing, on a codebase where every list endpoint declares one.

    [Fact]
    public void Every_string_filter_on_a_GET_endpoint_reaches_a_guard()
    {
        var found = ScanEndpoints();

        // Non-vacuity. A walk that matched nothing - a renamed folder, a changed lambda shape, a
        // Roslyn version that parses differently - would pass this test in silence, which is exactly
        // how the other instruments in this project came to measure nothing.
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
        // The allow-list is the part that rots. An exemption for a parameter nobody declares any
        // more is an exemption nobody is reading, and the next person to add a filter of that name
        // inherits a hole they did not know was there. Failing on a STALE entry is what stops the
        // list growing quietly, since the only way to keep it green is to delete what is no longer
        // true.
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

                // "Reaches a guard" is the whole lambda mentioning FilterValues - see the class
                // comment for why this is deliberately not per-parameter.
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

    /// <summary>`string?`, written either as a nullable predefined type or as `String?`.</summary>
    private static bool IsNullableString(TypeSyntax? type) =>
        type is NullableTypeSyntax nullable
        && (nullable.ElementType is PredefinedTypeSyntax { Keyword.ValueText: "string" }
            || nullable.ElementType is IdentifierNameSyntax { Identifier.ValueText: "String" });

    /// <summary>
    /// Walks up from the test binaries to the repository, so this works from `dotnet test`, from an
    /// IDE, and in CI without any of them agreeing on a working directory.
    /// </summary>
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
