using System.Text.RegularExpressions;
using FluentAssertions;

namespace MotsSupplierPortal.Tests.Architecture;

/// <summary>
/// Every route that accepts a request type with a validator declares that it validates it.
///
/// <para><b>The failure this exists for.</b> The organization routes declared three validators, had them
/// registered by the assembly scan that picks up every validator in the project, and never called any of
/// them. Nothing enforced a non-empty legal name or the column width behind it, so a name longer than its
/// column reached Postgres and came back to the caller as a 500 naming neither the field nor the
/// limit.</para>
///
/// <para><b>Why nothing noticed.</b> Validation used to be two ordinary lines inside a route's own
/// lambda. A route that omitted them validated nothing, returned a perfectly normal answer, and passed
/// its own tests. There was no shape to check: a permission is declared and therefore countable, and
/// validation was not.</para>
///
/// <para>It is countable now. <c>.Validate&lt;T&gt;()</c> is a declaration on the route, so this check
/// compares the routes that bind a request type against the request types that have a validator, and
/// reports any route that binds one without saying so.</para>
///
/// <para>Read syntactically from source, like <c>FreshETagGuardTests</c>: the invocation chain following
/// each <c>Map*</c> call, matched on spelling. A route that validates by some other means is reported
/// here and must be exempted by name, with its reason - which is the point, because those three reasons
/// are worth being asked for.</para>
/// </summary>
public sealed class ValidationDeclarationTests
{
    /// <summary>
    /// Routes that validate inside the handler on purpose, because something has to happen first and a
    /// filter runs before the handler by definition.
    ///
    /// <para>Named individually, with the reason, so a new one cannot join this list by resembling an
    /// existing member.</para>
    /// </summary>
    private static readonly (string File, string Route, string Why)[] Exempt =
    [
        ("RegistrationEndpoints.cs", "/register",
            "Checks whether registration is open before judging the body. A closed portal must not tell an applicant their password is weak, nor spend one of their few attempts a minute to say the front door is shut."),
        ("MfaEndpoints.cs", "/confirm",
            "Checks whether the second-factor surface is switched on at all, so a deployment with it off answers 404 rather than commenting on the code somebody sent."),
        ("SupplierEndpoints.cs", "/{supplierCode}",
            "Resolves the caller's own scope first, so somebody asking about another company's profile is told it does not exist rather than which of its fields are wrong."),
        ("ProposalEndpoints.cs", "/{proposalCode}",
            "A JSON Merge Patch. It runs the retired sub-routes' validators over the patch document and re-paths each failure to where it sits in that document, which a filter over a typed request cannot do."),
    ];

    private static readonly string[] Verbs = ["MapPost", "MapPut", "MapPatch", "MapDelete"];

    [Fact]
    public void Every_route_that_binds_a_validated_request_declares_the_filter()
    {
        var validated = ValidatedRequestTypes();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(EndpointsDirectory(), "*.cs"))
        {
            var name = Path.GetFileName(file);
            var source = File.ReadAllText(file);

            foreach (var (route, block) in MapCalls(source))
            {
                if (Exempt.Any(e => e.File == name && e.Route == route)) continue;

                foreach (var type in BoundRequestTypes(block).Where(validated.Contains))
                {
                    if (!block.Contains($".Validate<{type}>()")) offenders.Add($"{name} {route} -> {type}");
                }
            }
        }

        offenders.Should().BeEquivalentTo(Array.Empty<string>(),
            "a route that binds a request type with a validator and does not declare it validates nothing, "
            + "silently, which is how an over-long legal name reached the database as a 500");
    }

    [Fact]
    public void Every_declared_filter_names_a_type_that_has_a_validator()
    {
        // The other direction. Declaring the filter for a type with no validator throws on the first
        // request, deliberately, and a test failure is a better place to learn it than production.
        var validated = ValidatedRequestTypes();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(EndpointsDirectory(), "*.cs"))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"\.Validate<(\w+)>\(\)"))
            {
                var type = match.Groups[1].Value;
                if (!validated.Contains(type)) offenders.Add($"{Path.GetFileName(file)} -> {type}");
            }
        }

        offenders.Should().BeEquivalentTo(Array.Empty<string>(),
            "the filter demands its validator rather than skipping when one is missing, so a type without "
            + "one is a wiring mistake that would surface as a 500 on the first request");
    }

    [Fact]
    public void The_guard_can_see_the_routes_and_the_validators_it_is_checking()
    {
        // The control. A matcher that found nothing, or a validator set that came back empty, would pass
        // both checks above while checking nothing at all.
        var validated = ValidatedRequestTypes();
        validated.Should().HaveCountGreaterThan(40, "the project declares dozens of validators");
        validated.Should().Contain("RequirementRequest");

        var declared = 0;
        var bound = 0;
        foreach (var file in Directory.EnumerateFiles(EndpointsDirectory(), "*.cs"))
        {
            var source = File.ReadAllText(file);
            declared += Regex.Matches(source, @"\.Validate<\w+>\(\)").Count;
            bound += MapCalls(source).Sum(c => BoundRequestTypes(c.Block).Count(validated.Contains));
        }

        declared.Should().BeGreaterThan(50, "the filter is declared on most write routes");
        bound.Should().BeGreaterThan(declared, "and the exempt routes bind one without declaring it");
    }

    /// <summary>Request types the project has written a validator for.</summary>
    private static HashSet<string> ValidatedRequestTypes()
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(ApiDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"AbstractValidator<(\w+)>"))
            {
                types.Add(match.Groups[1].Value);
            }
        }

        return types;
    }

    /// <summary>Request types a route's own parameter list binds from the body.</summary>
    private static IEnumerable<string> BoundRequestTypes(string block) =>
        Regex.Matches(block, @"\b(\w+Request) request\b").Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal);

    /// <summary>Each mutating <c>Map*</c> call and the source between it and the next one.</summary>
    private static IEnumerable<(string Route, string Block)> MapCalls(string source)
    {
        var starts = new List<(int Index, string Route)>();

        foreach (var verb in Verbs)
        {
            foreach (Match match in Regex.Matches(source, $@"\w+\.{verb}\(""([^""]*)"""))
            {
                starts.Add((match.Index, match.Groups[1].Value));
            }
        }

        starts.Sort((a, b) => a.Index.CompareTo(b.Index));

        for (var n = 0; n < starts.Count; n++)
        {
            var to = n + 1 < starts.Count ? starts[n + 1].Index : source.Length;
            yield return (starts[n].Route, source[starts[n].Index..to]);
        }
    }

    private static string ApiDirectory() => Path.Combine(SolutionRoot(), "Api");

    private static string EndpointsDirectory()
    {
        var endpoints = Path.Combine(ApiDirectory(), "Endpoints");
        Directory.Exists(endpoints).Should().BeTrue($"endpoints are expected at {endpoints}");
        return endpoints;
    }

    private static string SolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MotsSupplierPortal.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the check cannot find the backend solution root from the test binaries");
        return directory!.FullName;
    }
}
