using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using Xunit;

namespace MotsSupplierPortal.Tests.Architecture;

/// <summary>
/// A-16 (batch 10): generates <c>PERMISSIONS.md</c> at the repository root and fails when it drifts.
///
/// <para><b>Why a test and not a script.</b> Every permission in this product is an invention against
/// codebase convention — no document ratifies a single one of them — so the doc owner needs the whole
/// set in one place to be able to answer at all. A hand-maintained list is the defect this project has
/// already fixed twice (the notification catalogue, the reference-data seeds): it drifts, and the drift
/// is invisible. Generated from <see cref="Permissions"/>, <see cref="Roles.DefaultPermissions"/> and
/// the actual <c>RequirePermission</c> call sites, so a renamed permission either regenerates the file
/// or turns this test red.</para>
///
/// <para>Set <c>UPDATE_PERMISSION_CATALOGUE=1</c> to rewrite the file instead of asserting.</para>
/// </summary>
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
        // Normalised on line endings only: the content itself must match exactly, because the point of
        // the file is that it is derived rather than curated.
        Normalise(File.ReadAllText(path)).Should().Be(Normalise(generated),
            "PERMISSIONS.md has drifted from the code. Re-run this test with UPDATE_PERMISSION_CATALOGUE=1.");
    }

    [Fact]
    public void Every_permission_constant_is_in_the_All_list()
    {
        // The catalogue is generated from All, so a constant missing from it would be invisible in the
        // file AND ungated by the roles map - the exact drift the generator exists to prevent, one
        // level up.
        var declared = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            // Roles.* style values are not permissions; permissions are resource.action.
            .Where(v => v.Contains('.', StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        declared.Should().NotBeEmpty();
        declared.Except(Permissions.All).Should().BeEmpty("every permission constant belongs in Permissions.All");
        Permissions.All.Except(declared).Should().BeEmpty("Permissions.All must not name a permission no constant declares");
    }

    [Fact]
    public void A_comment_between_a_gate_and_its_route_name_does_not_hide_the_gate()
    {
        // This was a real defect in the generated file, not a hypothetical. The gate/name pair bounds
        // the characters allowed between the two calls, and three routes had a long block comment
        // sitting there - explaining why `request-clarification` is a deliberate §8.1 exception, and
        // why ten RFQ child writes were guarded and four were not. The pair silently stopped matching,
        // and PERMISSIONS.md reported `rfq.clarify` as gating ONE route when it gates three, and
        // dropped `AddRfqItem` from `rfq.edit` entirely.
        //
        // A generated document that under-reports which routes a permission guards is worse than no
        // document: it is the artifact A-16 exists to let somebody ratify, and it was quietly wrong.
        // Blanking comments before matching is the fix; this asserts the fix rather than the file, so
        // it fails on the mechanism even if the catalogue happens to be regenerated.
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

        // The control, in the same test: the raw pair really is defeated by the comment, so the
        // assertion below is the fix working rather than a regex that would have matched anyway.
        GateAndName().Matches(withComment).Should().BeEmpty();

        var matched = GateAndName().Matches(CollapseWhitespace(WithoutComments(withComment)));

        matched.Should().ContainSingle();
        matched[0].Groups["name"].Value.Should().Be("AddRfqItem");
        matched[0].Groups["permission"].Value.Should().Be("RfqEdit");
    }

    [Fact]
    public void Stripping_comments_does_not_cut_a_string_containing_a_double_slash()
    {
        // The control, and the reason the stripper tracks string literals instead of using a regex: a
        // naive strip would cut `"https://..."` at the `//` and take the rest of the line with it -
        // removing a real gate while looking like it removed a comment. Both this codebase's route
        // patterns and its comments contain `//`.
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
                // Route names are code and are quoted; a prose note about where a check lives is not.
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

    /// <summary>
    /// Maps each permission to the endpoint NAMES it gates, read from the Api source.
    ///
    /// <para>Source text rather than reflection over the route table: building the route table needs
    /// the whole host (a database, Hangfire, a Sonar-clean startup), and an architecture test that
    /// boots the application is a test that fails for reasons unrelated to what it asserts. The
    /// pairing being read - a <c>RequirePermission</c> and the <c>WithName</c> that follows it - is
    /// mechanical and adjacent in every one of the 140-odd call sites.</para>
    /// </summary>
    private static Dictionary<string, List<string>> GateSites(string root)
    {
        var apiDir = Path.Combine(root, "src", "backend", "Api");
        var infrastructureDir = Path.Combine(root, "src", "backend", "Infrastructure");
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // A permission checked INSIDE a handler rather than on the route. `rfq.deadline.shorten` is the
        // whole reason this pass exists: BRULE-035 gives extension to the officer and shortening to the
        // manager, so the route cannot carry either permission and the handler decides from the
        // requested value (T-018). Reported as a handler check rather than as "no route", because "no
        // route" reads as dead code and this one guards a live rule.
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

            // Comments STRIPPED before matching, and this is not tidiness.
            //
            // The pair below allows a bounded run of characters between a RequirePermission and the
            // WithName it guards. A long block comment placed between them - explaining, say, why ten
            // routes were guarded and four were not - exceeds that bound, and the pair silently stops
            // matching: the catalogue then reports the route as ungated, which is the exact class of
            // artifact-asserting-something-untrue this file exists to prevent. It happened while
            // T-030 split (2) was being written, and the only reason it was noticed is that the
            // regenerated file was diffed by hand.
            //
            // Reading code with the prose removed also makes the bound mean what it looks like it
            // means: 600 characters of CODE between a gate and its name, not 600 characters of either.
            //
            // TWO forms, because two passes want different things. The group-gate scan below indexes
            // into the text and reads FORWARD from that offset, so it needs positions preserved -
            // comments are blanked in place, not removed. The gate/name pair does not care about
            // offsets and does care about length, so it reads a whitespace-collapsed copy: blanking a
            // 2,000-character comment to 2,000 spaces would leave the bound just as exceeded as the
            // comment did, which is how the first version of this fix still lost `AddRfqItem`.
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

            // A GROUP-level gate covers every route in the group, and the per-route WithName is what
            // names them. Missed by the pair above, and the first generated catalogue said "no route"
            // about `evaluation.template.manage`, which gates six live routes through exactly this
            // shape. A catalogue that reports a live permission as dead is the kind of artifact
            // asserting something untrue that this project keeps deleting, so the shape is read too.
            foreach (var group in GroupGate().Matches(text).Cast<Match>())
            {
                foreach (var name in RouteName().Matches(text[group.Index..]).Cast<Match>())
                {
                    Record(group.Groups["permission"].Value, name.Groups["name"].Value);
                }
            }

            // A route whose NAME is a variable rather than a literal - the supplier-lifecycle family
            // maps four routes from one loop, `.WithName(name)`. Recorded against the file, because
            // the name only exists at runtime and the honest answer is "this file's routes", not
            // "nothing".
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

    /// <summary>
    /// The same source with <c>//</c> and <c>/* */</c> comments blanked out, string literals intact.
    ///
    /// <para>Replaced with spaces rather than deleted, so every offset in the result still lines up
    /// with the original - the group-gate scan below indexes into this text and then reads forward
    /// from that position.</para>
    ///
    /// <para>String literals are tracked because this codebase's routes and comments both contain
    /// <c>//</c>: a naive strip would cut <c>"https://..."</c> in half and take the rest of the line
    /// with it, quietly removing real gates. Verbatim strings (<c>@"..."</c>) and raw string literals
    /// are not handled, and do not need to be - no route pattern or permission constant in Api uses
    /// one; if that changes, the pass below leaves them alone rather than mangling them, because an
    /// unrecognised quote simply starts an ordinary string.</para>
    /// </summary>
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
                // Newlines are kept so line-based reasoning about the file still holds.
                for (; i < stop; i++) if (output[i] != '\n') output[i] = ' ';
                i--;
            }
        }

        return new string(output);
    }

    /// <summary>Runs of whitespace to a single space. Offsets are NOT preserved - only the pass that
    /// bounds the distance between a gate and its name uses this.</summary>
    private static string CollapseWhitespace(string source) =>
        Whitespace().Replace(source, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>A RequirePermission and the WithName that names the route it guards, in that order.
    /// Non-greedy across the fluent calls between them, and bounded so it cannot pair a permission
    /// with a name from the NEXT endpoint when a route has no WithName of its own.</summary>
    [GeneratedRegex(@"RequirePermission\(Permissions\.(?<permission>\w+)\)(?<between>[^;]{0,600}?)\.WithName\(""(?<name>[^""]+)""\)",
        RegexOptions.Singleline)]
    private static partial Regex GateAndName();

    /// <summary>A group-level gate: <c>MapGroup(...)...RequirePermission(P)</c>, which covers every
    /// route mapped on that group.</summary>
    [GeneratedRegex(@"MapGroup\([^;]{0,400}?RequirePermission\(Permissions\.(?<permission>\w+)\)", RegexOptions.Singleline)]
    private static partial Regex GroupGate();

    /// <summary>Any literal route name, used to enumerate what a group-level gate covers.</summary>
    [GeneratedRegex(@"\.WithName\(""(?<name>[^""]+)""\)")]
    private static partial Regex RouteName();

    /// <summary>A permission consulted inside a handler: <c>HasPermission(Permissions.X)</c>.</summary>
    [GeneratedRegex(@"HasPermission\(Permissions\.(?<permission>\w+)\)|\?\s*Permissions\.(?<alternative>\w+)\s*:\s*Permissions\.\w+")]
    private static partial Regex HandlerCheck();

    /// <summary>A gate whose route name is a variable, e.g. the lifecycle family's `.WithName(name)`.</summary>
    [GeneratedRegex(@"RequirePermission\(Permissions\.(?<permission>\w+)\)\s*\.WithName\((?!"")\w+\)", RegexOptions.Singleline)]
    private static partial Regex GateAndVariableName();
}
