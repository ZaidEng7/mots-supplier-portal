// Every audit action the server writes has a label on the administrator's dashboard, and every action the dashboard
// picks out by name is one the server writes.
//
//
// THE FAILURE THIS EXISTS FOR
//
// The dashboard's recent activity feed shows the latest things people and other systems did, whatever they were, and
// labels each row in Arabic and English from the list in src/frontend/src/api/dashboardAuditActions.ts. An action the
// server starts writing tomorrow reaches the feed the first time somebody does it, and without a label it prints its
// stored token on the screen. Nothing else notices: the row is right, the count is right, and the screen still shows
// text.
//
// The other direction matters as much. DashboardAuditActions names the security events the dashboard counts and the
// sensitive changes it lists. A name misspelt there fails no query; it counts nothing and lists nothing, and a zero on
// a security screen reads as good news.
//
// The frontend's own coverage test, src/i18n/dynamicKeyCoverage.test.ts, takes it from the list onwards: every action
// on it has a label in both languages. This file holds the list to the server.
//
//
// HOW THE WRITES ARE READ
//
// Syntactically, from source, with the C# parser: every call to LogAsync in the server's projects, and in each the
// argument that is the action, named or third. The action is read as what it can evaluate to: a string literal, a
// constant of SupplierAuditActions or SessionAuditActions, either branch of a conditional, and the reference-data
// handler's interpolated name over the six tables it accepts.
//
// Anything else is a variable, and the scanner cannot know what it holds. Those sites are listed below by file, each
// with the actions that reach it and why, and a variable in a file that is not listed fails the test until somebody
// lists it. A listed action must appear in that same file, as a literal or one of those constants, so the list cannot
// name an action the file never writes, and a listed file the scanner no longer finds a variable in fails too, so the
// list cannot outlive the code it describes.
//
// The frontend list is read as text: the string literals between its opening bracket and "] as const". It may hold an
// action the server does not write yet, which lets a change that adds an action land after the label for it; it may
// not miss one the server writes.
//
// A scanner that found nothing would pass every assertion after it, which is the failure of every source-reading
// check, so the number of writes and actions it found is asserted first.

namespace MotsSupplierPortal.Tests.Architecture;

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Suppliers;

public sealed partial class DashboardAuditActionLabelTests
{
    private static readonly (string File, string[] Actions, string Why)[] VariableSites =
    [
        ("LoginHandler.cs",
            ["login_succeeded", "login_failed", "login_locked_out", "login_mfa_failed", "login_blocked_mfa_enrollment_required"],
            "One helper writes and saves every sign-in outcome, handed the action by each branch."),
        ("RecommendAwardHandler.cs",
            ["award.recommended", "award.re_recommended"],
            "A first recommendation and a repeated one share the write after the action is chosen."),
        ("RunErpImportHandler.cs",
            ["ErpImportCompleted", "ErpImportFailed"],
            "One helper records the run's outcome, as completed or as failed."),
        ("SupplierErpPushJob.cs",
            ["supplier.erp_push_created", "supplier.erp_push_completed", "supplier.erp_push_attempt_failed", "supplier.erp_push_failed"],
            "One helper saves each step of the push with the row that records it."),
        ("PatchProposalHandler.cs",
            ["proposal_item_priced", "proposal_terms_updated", "proposal_narrative_updated", "proposal_requirement_answered"],
            "One patch writes a row for each kind of change it made."),
        ("SupplierLifecycleHandler.cs",
            ["supplier_suspended", "supplier_reactivated", "supplier_deactivated"],
            "Each person's decision on a supplier's standing goes through one transition, handed its action."),
    ];

    private static readonly Type[] ActionConstants = [typeof(SupplierAuditActions), typeof(SessionAuditActions)];

    [Fact]
    public void Every_action_the_server_writes_has_a_place_on_the_dashboards_label_list()
    {
        var written = WrittenActions();

        written.Sites.Should().BeGreaterThanOrEqualTo(180, "the scanner must find the server's audit writes before it judges them");
        written.Actions.Count.Should().BeGreaterThanOrEqualTo(200);

        var listed = FrontendList();
        listed.Should().Contain("supplier.erp_push_retried", "the frontend list must be read before it is judged");

        written.Actions.Except(listed).Order(StringComparer.Ordinal).Should().BeEmpty(
            "every action the server writes can reach the dashboard's recent activity feed, and these have no label "
            + "in src/frontend/src/api/dashboardAuditActions.ts");
    }

    [Fact]
    public void Every_action_the_dashboard_picks_out_by_name_is_one_the_server_writes()
    {
        var written = WrittenActions().Actions;

        var named = DashboardAuditActions.SecurityEvents
            .Concat(DashboardAuditActions.SignInAttempts)
            .Concat(DashboardAuditActions.SensitiveChanges)
            .Append(DashboardAuditActions.SensitiveWhenStartedByAPerson)
            .Concat(DashboardAuditActions.LeftOutOfTheFeed);

        named.Except(written).Should().BeEmpty(
            "an action no handler writes would count nothing and list nothing, and a zero reads as good news");
    }

    [Fact]
    public void The_scanner_reads_what_an_action_can_be_and_refuses_a_variable_it_was_not_told_about()
    {
        const string source = """
            class Sample
            {
                async Task Run(bool flag, string action)
                {
                    await audit.LogAsync("Supplier", id, "plain_literal", ct: ct);
                    await audit.LogAsync("Supplier", id, flag ? SupplierAuditActions.ErpImportRun : "other_literal");
                    await audit.LogAsync(aggregateType: "Supplier", aggregateId: id, action: $"reference.{command.Table}.created");
                    await audit.LogAsync("Supplier", id, action);
                }
            }
            """;

        var (actions, variables) = Scan(source);

        actions.Should().Contain(["plain_literal", "ErpImportRun", "other_literal", "reference.incoterms.created"]);
        actions.Should().HaveCount(3 + ReferenceTables.All.Length);
        variables.Should().Be(1, "a bare variable is a site the scanner cannot read, and must be listed by file");
    }

    private static (int Sites, HashSet<string> Actions) WrittenActions()
    {
        var actions = new HashSet<string>(StringComparer.Ordinal);
        var sites = 0;
        var variableFiles = new HashSet<string>(StringComparer.Ordinal);
        var unlisted = new List<string>();

        foreach (var file in ServerSourceFiles())
        {
            var name = Path.GetFileName(file);
            var source = File.ReadAllText(file);
            var (found, variables) = Scan(source);

            sites += CountWrites(source);
            actions.UnionWith(found);

            if (variables == 0) continue;

            var listed = VariableSites.Where(s => s.File == name).ToArray();
            if (listed.Length == 0)
            {
                unlisted.Add(name);
                continue;
            }

            variableFiles.Add(name);
            var named = NamedInFile(source);
            foreach (var action in listed.SelectMany(s => s.Actions))
            {
                named.Should().Contain(action, $"{name} is listed as writing {action}, so the file must name it");
                actions.Add(action);
            }
        }

        unlisted.Should().BeEmpty(
            "these files write an audit action held in a variable; list them in VariableSites with the actions it can hold");
        VariableSites.Select(s => s.File).Except(variableFiles).Should().BeEmpty(
            "a listed file in which the scanner no longer finds a variable action is a stale entry");

        return (sites, actions);
    }

    private static (HashSet<string> Actions, int Variables) Scan(string source)
    {
        var actions = new HashSet<string>(StringComparer.Ordinal);
        var variables = 0;

        foreach (var call in Writes(source))
        {
            var argument = ActionArgument(call);
            if (argument is null)
            {
                variables++;
                continue;
            }

            if (!Resolve(argument, actions))
            {
                variables++;
            }
        }

        return (actions, variables);
    }

    private static int CountWrites(string source) => Writes(source).Count();

    private static IEnumerable<InvocationExpressionSyntax> Writes(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "LogAsync" });

    private static ExpressionSyntax? ActionArgument(InvocationExpressionSyntax call)
    {
        var arguments = call.ArgumentList.Arguments;
        var named = arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "action");
        if (named is not null) return named.Expression;

        var positional = arguments.Where(a => a.NameColon is null).ToArray();
        return positional.Length >= 3 ? positional[2].Expression : null;
    }

    // Adds what the expression can evaluate to, and answers false when some part of it is a value the scanner cannot
    // read, so that a conditional with one readable branch and one variable branch is still reported as a variable.
    private static bool Resolve(ExpressionSyntax expression, HashSet<string> actions)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                actions.Add(literal.Token.ValueText);
                return true;

            case ParenthesizedExpressionSyntax parenthesized:
                return Resolve(parenthesized.Expression, actions);

            case ConditionalExpressionSyntax conditional:
                var whenTrue = Resolve(conditional.WhenTrue, actions);
                var whenFalse = Resolve(conditional.WhenFalse, actions);
                return whenTrue && whenFalse;

            case MemberAccessExpressionSyntax member when ConstantValue(member) is { } value:
                actions.Add(value);
                return true;

            case InterpolatedStringExpressionSyntax interpolated when ReferenceTableAction(interpolated) is { } suffix:
                actions.UnionWith(ReferenceTables.All.Select(table => $"reference.{table}.{suffix}"));
                return true;

            default:
                return false;
        }
    }

    private static string? ConstantValue(MemberAccessExpressionSyntax member)
    {
        var owner = ActionConstants.FirstOrDefault(t => t.Name == member.Expression.ToString());
        return owner?.GetField(member.Name.Identifier.ValueText, BindingFlags.Public | BindingFlags.Static) is
            { IsLiteral: true } field
            ? (string)field.GetRawConstantValue()!
            : null;
    }

    // The reference-data handler names its rows reference.{table}.{what}, over the tables it accepts; any other
    // interpolated action is not read and counts as a variable.
    private static string? ReferenceTableAction(InterpolatedStringExpressionSyntax interpolated)
    {
        var match = ReferenceActionPattern().Match(interpolated.ToString());
        return match.Success ? match.Groups["what"].Value : null;
    }

    private static HashSet<string> NamedInFile(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var named = root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(l => l.Token.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        named.UnionWith(root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Select(ConstantValue)
            .OfType<string>());

        return named;
    }

    private static HashSet<string> FrontendList()
    {
        var path = Path.Combine(SolutionRoot(), "..", "frontend", "src", "api", "dashboardAuditActions.ts");
        File.Exists(path).Should().BeTrue($"the dashboard's label list is expected at {path}");

        var text = File.ReadAllText(path);
        var start = text.IndexOf("DASHBOARD_AUDIT_ACTIONS = [", StringComparison.Ordinal);
        var end = text.IndexOf("] as const", start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);

        return ListEntry().Matches(text[start..end])
            .Select(m => m.Groups["action"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> ServerSourceFiles() =>
        new[] { "Api", "Application", "Domain", "Infrastructure" }
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(SolutionRoot(), project), "*.cs", SearchOption.AllDirectories))
            .Where(file =>
            {
                var parts = file.Split(Path.DirectorySeparatorChar);
                return !parts.Contains("bin") && !parts.Contains("obj") && !parts.Contains("Migrations");
            });

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

    [GeneratedRegex(@"^\$""reference\.\{command\.Table\}\.(?<what>[a-z]+)""$")]
    private static partial Regex ReferenceActionPattern();

    [GeneratedRegex(@"^\s*'(?<action>[^']+)',\s*$", RegexOptions.Multiline)]
    private static partial Regex ListEntry();
}
