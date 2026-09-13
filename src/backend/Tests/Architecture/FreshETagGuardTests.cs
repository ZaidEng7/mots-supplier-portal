// Every mutating endpoint on a version-guarded aggregate must answer with a FRESH version header.
//
//
// THE FAILURE THIS EXISTS FOR
//
// One route of twenty-three lacked it. It changed the supplier and returned no new version, so every client
// went on asserting the version it had read BEFORE the write, and the next guarded save on the same page came
// back refused with nothing on screen to explain it.
//
// A supplier filled in their legal details, chose a currency, pressed save, and the currency was silently gone
// after a reload.
//
//
// WHY A CHECK RATHER THAN JUST THE FIX
//
// The fix is one line, and one line is exactly what gets forgotten when a route is added next to twenty-two
// that already have it.
//
// Nothing else notices: the endpoint answers successfully, its own integration test passes, and the damage
// lands on the NEXT write by the same caller.
//
//
// THE SCOPE IS THE WHOLE POINT OF THIS CHECK BEING HONEST
//
// It asserts the rule on the SUPPLIER endpoints only.
//
// A first draft asserted it across every file that used the fresh-version helper anywhere. It reported
// fourteen tender routes, and they are not a backlog: that file splits twelve child-collection writes that DO
// carry a fresh version from fourteen state transitions that do not, which is a distinction somebody drew
// rather than one somebody forgot. Fixing them to satisfy this test would have been inventing a convention and
// calling it a defect.
//
// On the supplier aggregate the convention IS observable: twenty-two of twenty-three routes carried it and the
// twenty-third was reproducibly broken. That is the rule this asserts, and it asserts it only where the
// evidence for it exists.
//
// Whether the tender transitions should carry one is a real question, and it is logged as one rather than
// answered here. An aggregate that never joined the scheme is out of scope by design.
//
//
// THE ONE EXEMPTION, AND THE CONTROL
//
// Revealing an unmasked bank account number is a post because the value must not sit in a URL or a log, not
// because it writes anything, so there is no new version to hand back.
//
// Exemptions are named individually rather than pattern-matched: a post that does not mutate is unusual enough
// that each one should have to be argued for, and a new one must not join the list by resembling an existing
// member.
//
// And a matcher that found nothing would pass the assertion while checking nothing, which is the failure mode
// of every source-reading check, so the count of what was matched is asserted too.
//
// Read syntactically from source, like the filter check: each mutating mapping call and the source between it
// and the next one, matched on spelling. A route that obtained a fresh version by some other means would be
// reported here and should be exempted by name, with its reason.

namespace MotsSupplierPortal.Tests.Architecture;

using FluentAssertions;

public sealed class FreshETagGuardTests
{
    private static readonly (string File, string Route, string Why)[] Exempt =
    [
        ("SupplierEndpoints.cs", "/me/bank-accounts/{bankAccountId:guid}/reveal",
            "Reads the unmasked account number under a permission check. A POST because it must not sit in a URL or a log, not because it writes."),
    ];

    [Fact]
    public void Every_mutating_endpoint_returns_a_fresh_etag()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(EndpointsDirectory(), "*.cs"))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            if (name != "SupplierEndpoints.cs") continue;

            foreach (var (route, block) in MapCalls(source))
            {
                if (Exempt.Any(e => e.File == name && e.Route == route)) continue;
                if (!block.Contains("WithFreshETag")) offenders.Add($"{name} {route}");
            }
        }

        offenders.Should().BeEquivalentTo(Array.Empty<string>(),
            "a mutating endpoint that returns no fresh ETag leaves every client asserting a version the "
            + "write has already moved on from, and the 412 lands on their NEXT save");
    }

    [Fact]
    public void The_guard_can_see_the_routes_it_is_checking()
    {
        var supplier = File.ReadAllText(Path.Combine(EndpointsDirectory(), "SupplierEndpoints.cs"));
        var calls = MapCalls(supplier).ToList();

        calls.Should().HaveCountGreaterThan(15);
        calls.Should().Contain(c => c.Route == "/me/legal-info");
    }

    private static IEnumerable<(string Route, string Block)> MapCalls(string source)
    {
        var verbs = new[] { "MapPost", "MapPut", "MapPatch", "MapDelete" };
        var starts = new List<(int Index, string Route)>();

        for (var i = 0; i < source.Length; i++)
        {
            foreach (var verb in verbs)
            {
                var token = $"Map{verb[3..]}(\"";
                if (i + token.Length > source.Length || !source.AsSpan(i).StartsWith($"group.{token}")) continue;
                var quote = i + $"group.{token}".Length;
                var end = source.IndexOf('"', quote);
                if (end > quote) starts.Add((i, source[quote..end]));
            }
        }

        for (var n = 0; n < starts.Count; n++)
        {
            var from = starts[n].Index;
            var to = n + 1 < starts.Count ? starts[n + 1].Index : source.Length;
            yield return (starts[n].Route, source[from..to]);
        }
    }

    private static string EndpointsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MotsSupplierPortal.slnx")))
        {
            directory = directory.Parent;
        }

        var endpoints = Path.Combine(directory!.FullName, "Api", "Endpoints");
        Directory.Exists(endpoints).Should().BeTrue($"endpoints are expected at {endpoints}");
        return endpoints;
    }
}
