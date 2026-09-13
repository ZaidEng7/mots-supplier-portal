// Generates the permission catalogue at the repository root, and fails when it drifts from the code.
//
//
// WHY A TEST AND NOT A SCRIPT
//
// Every permission in this product is an invention against codebase convention. No document ratifies a single
// one of them, so whoever owns that decision needs the whole set in one place to be able to answer at all.
//
// A hand-maintained list is the defect this project has already fixed twice, in the notification catalogue and
// the reference-data seeds: it drifts, and the drift is invisible.
//
// So the file is generated from the permission constants, the default role map, and the actual gate call sites.
// A renamed permission either regenerates the file or turns this test red. Setting the update environment
// variable rewrites the file instead of asserting.
//
// The comparison normalises line endings only. The content itself must match exactly, because the point of the
// file is that it is derived rather than curated.
//
//
// EVERY CONSTANT MUST BE IN THE PUBLISHED LIST
//
// The catalogue is generated from that list, so a constant missing from it would be invisible in the file AND
// ungated by the roles map, which is the exact drift the generator exists to prevent, one level up.
//
// Values without a dot are role names rather than permissions, and are excluded.
//
//
// THE GATES ARE READ FROM SOURCE, NOT FROM THE ROUTE TABLE
//
// Building the route table needs the whole host: a database, a job server, a clean startup. An architecture
// test that boots the application is a test that fails for reasons unrelated to what it asserts.
//
// The pairing being read, a permission requirement and the route name that follows it, is mechanical and
// adjacent in every one of the hundred-and-forty-odd call sites.
//
// Three other shapes are read as well, each because the first version of the catalogue got it wrong:
//
// A GROUP-level gate covers every route in the group and the per-route name is what names them. The pair above
// misses it, and the first generated catalogue said "no route" about a permission that gates six live routes
// through exactly that shape. A catalogue reporting a live permission as dead is the kind of artefact asserting
// something untrue that this project keeps deleting.
//
// A route whose NAME is a variable rather than a literal, where one loop maps four routes. It is recorded
// against the file, because the name only exists at runtime and the honest answer is "this file's routes"
// rather than "nothing".
//
// A permission checked INSIDE a handler rather than on a route. One rule is the whole reason that pass exists:
// extending a deadline belongs to the officer and shortening it to the manager, so the route cannot carry
// either permission and the handler decides from the requested value. It is reported as a handler check rather
// than as "no route", because "no route" reads as dead code and this one guards a live rule.
//
//
// COMMENTS ARE STRIPPED BEFORE MATCHING, AND THAT IS NOT TIDINESS
//
// The pair allows a bounded run of characters between a permission requirement and the route name it guards.
//
// A long block comment placed between them, explaining say why ten routes were guarded and four were not,
// exceeds that bound, and the pair silently stops matching. The catalogue then reports the route as ungated,
// which is the exact class of artefact-asserting-something-untrue this file exists to prevent.
//
// It happened while another change was being written, and the only reason it was noticed is that the
// regenerated file was diffed by hand. The catalogue reported one permission as gating a single route when it
// gates three, and dropped a route from another permission entirely. A generated document that under-reports
// which routes a permission guards is worse than no document: it is the artefact somebody is meant to ratify,
// and it was quietly wrong.
//
// Reading code with the prose removed also makes the bound mean what it looks like it means: characters of
// CODE between a gate and its name, not characters of either.
//
// TWO forms, because two passes want different things. The group-gate scan indexes into the text and reads
// forward from that offset, so it needs positions preserved and comments are blanked in place rather than
// removed, with newlines kept so line-based reasoning still holds. The gate-and-name pair does not care about
// offsets and does care about length, so it reads a whitespace-collapsed copy: blanking a two-thousand-character
// comment to two thousand spaces would leave the bound just as exceeded as the comment did, which is how the
// first version of this fix still lost a route.
//
// String literals are tracked while stripping, because this codebase's routes and its comments both contain a
// double slash: a naive strip would cut a URL in half and take the rest of the line with it, quietly removing
// real gates. Verbatim and raw string literals are not handled and do not need to be, because no route pattern
// or permission constant uses one; if that changes, the pass leaves them alone rather than mangling them,
// because an unrecognised quote simply starts an ordinary string.
//
//
// TWO CONTROLS, EACH ASSERTING THE MECHANISM RATHER THAN THE FILE
//
// The first builds a gate with an over-long comment between it and its route name, shows that the raw pair
// really is defeated by it, and then shows the stripped copy matching. So it fails on the mechanism even if
// the catalogue happens to have been regenerated.
//
// The second passes a route pattern containing a double slash and asserts that the pattern survives while the
// trailing comment does not, which is the reason the stripper tracks string literals instead of using a pattern
// match.

namespace MotsSupplierPortal.Tests.Architecture;

using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using Xunit;

public sealed partial class PermissionCatalogueTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BACKLOG-REMEDIATION.md")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the catalogue lives at the repository root, next to BACKLOG-REMEDIATION.md");
        return dir!.FullName;
    }

    [Fact]
    public void The_permission_catalogue_is_current()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "PERMISSIONS.md");
        var generated = Generate(root);

        if (Environment.GetEnvironmentVariable("UPDATE_PERMISSION_CATALOGUE") == "1")
        {
            File.WriteAllText(path, generated);
            return;
        }

        File.Exists(path).Should().BeTrue("PERMISSIONS.md is generated - run with UPDATE_PERMISSION_CATALOGUE=1");
        Normalise(File.ReadAllText(path)).Should().Be(Normalise(generated),
            "PERMISSIONS.md has drifted from the code. Re-run this test with UPDATE_PERMISSION_CATALOGUE=1.");
    }

    [Fact]
    public void Every_permission_constant_is_in_the_All_list()
    {
        var declared = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => v.Contains('.', StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        declared.Should().NotBeEmpty();
        declared.Except(Permissions.All).Should().BeEmpty("every permission constant belongs in Permissions.All");
        Permissions.All.Except(declared).Should().BeEmpty("Permissions.All must not name a permission no constant declares");
    }

    [Fact]
    public void A_comment_between_a_gate_and_its_route_name_does_not_hide_the_gate()
    {
        const string withComment = """
            group.MapPost("/{code}/items", Handler)
            .RequirePermission(Permissions.RfqEdit)
            /*
             * A comment long enough to exceed the bound the pair allows between the two calls, which is
             * exactly what a real explanation of a non-obvious guard looks like. Padding to be sure:
             * aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
             * aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
             * aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
             * aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
             * aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
             * aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
             * aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
             */
            .WithName("AddRfqItem");
            """;

        GateAndName().Matches(withComment).Should().BeEmpty();

        var matched = GateAndName().Matches(CollapseWhitespace(WithoutComments(withComment)));

        matched.Should().ContainSingle();
        matched[0].Groups["name"].Value.Should().Be("AddRfqItem");
        matched[0].Groups["permission"].Value.Should().Be("RfqEdit");
    }

    [Fact]
    public void Stripping_comments_does_not_cut_a_string_containing_a_double_slash()
    {
        const string withUrl = """
            group.MapGet("https://example.test/callback", Handler) // a trailing comment
            .RequirePermission(Permissions.RfqRead)
            .WithName("Callback");
            """;

        var stripped = WithoutComments(withUrl);

        stripped.Should().Contain("https://example.test/callback");
        stripped.Should().NotContain("a trailing comment");
        GateAndName().Matches(CollapseWhitespace(stripped)).Should().ContainSingle();
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

    private static string Generate(string root)
    {
        var gatedBy = GateSites(root);
        var holders = Permissions.All.ToDictionary(
            p => p,
            p => Roles.DefaultPermissions
                .Where(pair => pair.Value.Contains(p, StringComparer.Ordinal))
                .Select(pair => pair.Key)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("# Permission catalogue");
        sb.AppendLine();
        sb.AppendLine("**Generated. Do not edit.** Produced by `PermissionCatalogueTests` from `Permissions.All`,");
        sb.AppendLine("`Roles.DefaultPermissions` and the `RequirePermission` call sites; the test fails when this file");
        sb.AppendLine("drifts from the code. Regenerate with `UPDATE_PERMISSION_CATALOGUE=1 dotnet test`.");
        sb.AppendLine();
        sb.AppendLine("Every name here is an **invention against codebase convention** — no document in `docs/`");
        sb.AppendLine("ratifies a `resource.action` string. That is the point of this file: A-16 asks for one pass in");
        sb.AppendLine("which the whole set can be ratified or renamed, rather than each name staying provisional");
        sb.AppendLine("forever. A permission held by NO role is reachable by nobody, and one gating NO route is");
        sb.AppendLine("either dead or waiting for a surface — both are called out below.");
        sb.AppendLine();
        sb.AppendLine($"{Permissions.All.Count} permissions, {Roles.DefaultPermissions.Count} roles.");
        sb.AppendLine();
        sb.AppendLine("| Permission | Held by default | Gates |");
        sb.AppendLine("|---|---|---|");

        foreach (var permission in Permissions.All.OrderBy(p => p, StringComparer.Ordinal))
        {
            var roles = holders[permission].Length > 0
                ? string.Join(", ", holders[permission].Select(r => $"`{r}`"))
                : "**no role**";
            var routes = gatedBy.TryGetValue(permission, out var names) && names.Count > 0
                ? string.Join(", ", names.OrderBy(n => n, StringComparer.Ordinal)
                    .Select(n => n.StartsWith("checked in ", StringComparison.Ordinal) ? n : $"`{n}`"))
                : "**no route**";
            sb.AppendLine($"| `{permission}` | {roles} | {routes} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Roles");
        sb.AppendLine();
        sb.AppendLine("| Role | Permissions held by default |");
        sb.AppendLine("|---|---|");
        foreach (var (role, permissions) in Roles.DefaultPermissions.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var held = permissions.Length > 0
                ? string.Join(", ", permissions.OrderBy(p => p, StringComparer.Ordinal).Select(p => $"`{p}`"))
                : "**none** — deliberate for `ministry_viewer` before EPIC-18; see BRULE-086";
            sb.AppendLine($"| `{role}` | {held} |");
        }

        return sb.ToString();
    }

    private static Dictionary<string, List<string>> GateSites(string root)
    {
        var apiDir = Path.Combine(root, "src", "backend", "Api");
        var infrastructureDir = Path.Combine(root, "src", "backend", "Infrastructure");
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(infrastructureDir, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file)) continue;

            foreach (var match in HandlerCheck().Matches(File.ReadAllText(file)).Cast<Match>())
            {
                var constant = match.Groups["permission"].Success
                    ? match.Groups["permission"].Value
                    : match.Groups["alternative"].Value;
                var value = ValueOf(constant);
                if (value is null) continue;
                if (!result.TryGetValue(value, out var names)) result[value] = names = [];
                var label = $"checked in {Path.GetFileNameWithoutExtension(file)}, not on a route";
                if (!names.Contains(label, StringComparer.Ordinal)) names.Add(label);
            }
        }

        foreach (var file in Directory.EnumerateFiles(apiDir, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file)) continue;

            var text = WithoutComments(File.ReadAllText(file));
            var codeOnly = CollapseWhitespace(text);

            void Record(string constant, string name)
            {
                var value = ValueOf(constant);
                if (value is null) return;
                if (!result.TryGetValue(value, out var names)) result[value] = names = [];
                if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
            }

            foreach (var match in GateAndName().Matches(codeOnly).Cast<Match>())
            {
                Record(match.Groups["permission"].Value, match.Groups["name"].Value);
            }

            foreach (var group in GroupGate().Matches(text).Cast<Match>())
            {
                foreach (var name in RouteName().Matches(text[group.Index..]).Cast<Match>())
                {
                    Record(group.Groups["permission"].Value, name.Groups["name"].Value);
                }
            }

            foreach (var variable in GateAndVariableName().Matches(codeOnly).Cast<Match>())
            {
                Record(variable.Groups["permission"].Value,
                    $"{Path.GetFileNameWithoutExtension(file)} (name resolved at runtime)");
            }
        }

        return result;
    }

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string? ValueOf(string constantName) =>
        typeof(Permissions).GetField(constantName, BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true } field
            ? (string?)field.GetRawConstantValue()
            : null;

    private static string WithoutComments(string source)
    {
        var output = source.ToCharArray();
        var inString = false;
        var inChar = false;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (inString || inChar)
            {
                if (c == '\\') { i++; continue; }
                if (inString && c == '"') inString = false;
                else if (inChar && c == '\'') inChar = false;
                continue;
            }

            if (c == '"') { inString = true; continue; }
            if (c == '\'') { inChar = true; continue; }

            if (c != '/' || i + 1 >= source.Length) continue;

            if (source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') output[i++] = ' ';
                i--;
            }
            else if (source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? source.Length : end + 2;
                for (; i < stop; i++) if (output[i] != '\n') output[i] = ' ';
                i--;
            }
        }

        return new string(output);
    }

    private static string CollapseWhitespace(string source) =>
        Whitespace().Replace(source, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"RequirePermission\(Permissions\.(?<permission>\w+)\)(?<between>[^;]{0,600}?)\.WithName\(""(?<name>[^""]+)""\)",
        RegexOptions.Singleline)]
    private static partial Regex GateAndName();

    [GeneratedRegex(@"MapGroup\([^;]{0,400}?RequirePermission\(Permissions\.(?<permission>\w+)\)", RegexOptions.Singleline)]
    private static partial Regex GroupGate();

    [GeneratedRegex(@"\.WithName\(""(?<name>[^""]+)""\)")]
    private static partial Regex RouteName();

    [GeneratedRegex(@"HasPermission\(Permissions\.(?<permission>\w+)\)|\?\s*Permissions\.(?<alternative>\w+)\s*:\s*Permissions\.\w+")]
    private static partial Regex HandlerCheck();

    [GeneratedRegex(@"RequirePermission\(Permissions\.(?<permission>\w+)\)\s*\.WithName\((?!"")\w+\)", RegexOptions.Singleline)]
    private static partial Regex GateAndVariableName();
}
