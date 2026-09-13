// Every handler that reads a row-scoped table either scopes it to the caller, or is named here with the reason
// it does not.
//
//
// WHY THIS CHECK EXISTS
//
// Row-scoping is the rule this product most depends on. The risk register's only critical entry names this exact
// leak: a supplier seeing another supplier's bid, or one buying body seeing another's tender.
//
// Its stated mitigation is "scoping asserted in a shared query pipeline, not per-endpoint ad hoc". There is no
// shared pipeline. There is no global query filter anywhere in the database context, no base handler, and no
// test over the organization.
//
// Seventy handlers get it right by hand, and a seventy-first that forgot would fail nothing at all: the build is
// green, every suite is green, and the rows simply come back.
//
//
// WHAT IT READS
//
// Source, matched syntactically, the same way the filter check reads endpoint lambdas: each handler class in the
// infrastructure layer, whether its body touches one of the row-scoped tables, and whether it mentions one of
// the scoping constructs anywhere. There is no semantic model, so the match is on spelling.
//
// The scoped tables are the ones whose rows belong to somebody. A read of one without a scope is a read of every
// organization's, or every supplier's.
//
// The supplier table is the subtle one. A supplier carries no organization at all, because the registry is
// national and the link to a buying body is a separate many-to-many. So a buyer-side handler reading it unscoped
// is usually correct, while a supplier-side one reading it unscoped is a cross-tenant leak. The list names the
// table; the exemptions carry that distinction, one handler at a time.
//
// The scoping vocabulary is three claims from the token, organization, supplier and user, plus the five named
// helpers that hold a scoping condition on behalf of the handlers that call them. The user claim counts because
// one family of handlers is scoped by ASSIGNMENT rather than by tenancy: an evaluator may belong to no
// organization at all, and their assignment list filters on the evaluator, which is a scope in every sense that
// matters.
//
//
// "MENTIONS A SCOPING CONSTRUCT ANYWHERE" IS DELIBERATELY WEAK
//
// The first draft of this check proved why it has to be. A rule of "references the organization claim" reported
// a handler as unscoped that is scoped, correctly, by delegating to a shared visibility rule which carries the
// condition in a class of its own.
//
// Delegation is the shape the good handlers use. A check that could not see through it would have reported the
// best-written code in the repository and been switched off within a week.
//
// So the rule is "reach for the scope". Proving WHICH rows a handler actually filtered would need dataflow
// analysis this project does not have.
//
// That weakness is worth stating plainly: this catches a handler that never scopes at all, which is the failure
// that has actually shipped elsewhere in this codebase. It does not catch a handler that scopes one query and
// forgets a second. The cross-organization integration tests are the other half, with two organizations where
// the second may not see the first's rows.
//
//
// THE EXEMPTIONS, IN FOUR GROUPS
//
// Typed out by hand, one entry per handler, for the reason every exemption list in this repository is: a pattern
// would let the next one join it silently. Every entry was read before it was written.
//
// Cross-organization by grant: the ministry's aggregate view across every buying body, since widened to named
// rows. Pinning these to one organization would not narrow a leak, it would break the feature.
//
// The supplier registry is national, as above, and scoping these to the caller's organization would hide every
// supplier from every reviewer.
//
// No caller to scope to: both of those run before an account exists.
//
// Platform administration, gated by permission rather than by scope and deliberately so, because an
// administrator belongs to no organization and a scope condition would return nothing.
//
//
// THREE CONTROLS
//
// Non-vacuity first, because a walk that matched nothing passes in silence, which is how the other instruments
// in this project came to measure nothing.
//
// A stale-exemption check, because the allow-list is the part that rots: an exemption for a handler that no
// longer exists, or that has since been scoped, is an exemption nobody is reading, and the next handler to take
// that name inherits a hole.
//
// And a revert-to-red, in the shape the real defect takes. Without it the two assertions above would pass just
// as well against a matcher that had stopped matching anything. It covers the delegation case the first draft
// got wrong, and the case the first RUN got wrong: a comment is not a scope.
//
//
// COMMENTS ARE STRIPPED BEFORE MATCHING, AND THAT IS NOT HOUSEKEEPING
//
// The syntax node's full text includes its leading trivia, and the first run of this check reported the
// governance overview handler as SCOPED, on the strength of its own doc comment, which read "no organization
// predicate, the inversion the governance overview documents".
//
// The handler is deliberately unscoped and says so, and the check read the saying as the doing.
//
// The per-file matcher is separate from the whole-tree scan so the revert-to-red control can run the same
// matcher over a sample rather than over a second implementation of it.

namespace MotsSupplierPortal.Tests.Architecture;

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed class RowScopeGuardTests
{
    private static readonly string[] ScopedSets =
    [
        "Rfqs", "Proposals", "Suppliers", "Offerings", "Invitations", "SupplierDocuments",
        "Awards", "Evaluations", "Addresses", "BankAccounts", "Contacts", "Branches",
        "Representatives", "CategoryLinks",
    ];

    private static readonly string[] ScopingVocabulary =
    [
        "scope.OrganizationId", "scope.SupplierId", "scope.UserId",
        "LoadScopedAsync", "LoadScopedByOrgAsync", "LoadScopedByAssignmentAsync",
        "ScopedQuery", "SupplierCodeScope", "BuyerProposalVisibilityRule",
    ];

    private static readonly Dictionary<string, string> DeliberatelyUnscoped = new(StringComparer.Ordinal)
    {
        ["GetGovernanceOverviewHandler"] = "BRULE-086: the Ministry's view is cross-organisation by grant",
        ["ListMinistryRfqsHandler"] = "BRULE-086/D-66: every tender in the country, whoever is running it",
        ["ListMinistrySuppliersHandler"] = "BRULE-086: the national supplier registry, not one buyer's list",
        ["GetMinistryAwardAnalyticsHandler"] = "BRULE-086: award analytics across every buying body",
        ["GetMinistryRfqDetailHandler"] = "D-66: one tender read-only with every bid, cross-organisation",
        ["GetCategoryCoverageHandler"] = "SCR-604: category coverage of the national registry",

        ["ListSupplierDirectoryHandler"] = "SCR-601: the national registry; a supplier belongs to no buying body",
        ["ListComplianceDirectoryHandler"] = "compliance is assessed nationally, not per buying body",
        ["ListSupplierDocumentsPagedHandler"] = "reviewer surface over the national registry; the route names the supplier",
        ["GetReviewerSupplierViewHandler"] = "reviewer surface over the national registry; the route names the supplier",
        ["SearchBuyerOfferingsHandler"] = "the offering catalogue is national; any buyer may search any supplier's",
        ["ComplianceReportHandler"] = "states it on screen: these counts cover every registered supplier",

        ["RegisterSupplierHandler"] = "anonymous: it creates the supplier the scope would have named",
        ["VerifyEmailHandler"] = "anonymous: a token is the only identity the caller has",

        ["StorageSettingsHandler"] = "system_admin: storage totals for the deployment, not for a tenant",
        ["GetErpSyncMonitorHandler"] = "system_admin: the ERP queue is one queue for the deployment",
    };

    private sealed record HandlerScan(string File, string Name, bool Scoped, string[] Tables);

    [Fact]
    public void Every_handler_that_reads_a_scoped_table_reaches_for_the_callers_scope()
    {
        var found = Scan();

        found.Should().HaveCountGreaterThan(50,
            "the walk must actually be finding handlers; a check that inspects nothing always passes");
        found.Count(h => h.Scoped).Should().BeGreaterThan(40,
            "most handlers do scope; if almost none appear to, the vocabulary has stopped matching");

        var unscoped = found
            .Where(h => !h.Scoped)
            .Where(h => !DeliberatelyUnscoped.ContainsKey(h.Name))
            .ToList();

        unscoped.Should().BeEmpty(
            "a handler that reads a row-scoped table without reaching for the caller's scope returns "
            + "every organisation's rows, and nothing else in this repository would notice:\n"
            + string.Join("\n", unscoped.Select(u => $"  {u.File}  {u.Name} -> db.{string.Join(", db.", u.Tables)}")));
    }

    [Fact]
    public void Every_exemption_still_names_a_handler_that_reads_a_scoped_table()
    {
        var scanned = Scan();
        var unscopedNames = scanned.Where(h => !h.Scoped).Select(h => h.Name).ToHashSet(StringComparer.Ordinal);

        var stale = DeliberatelyUnscoped.Keys.Where(name => !unscopedNames.Contains(name)).ToList();

        stale.Should().BeEmpty(
            "these exemptions no longer name a handler that reads a scoped table unscoped, so they "
            + $"exempt nothing and hide whatever takes those names next: {string.Join(", ", stale)}");
    }

    [Fact]
    public void The_check_can_fail()
    {
        const string leaks = """
            public sealed class ListEverythingHandler(AppDbContext db)
            {
                public async Task<List<Rfq>> HandleAsync(CancellationToken ct) =>
                    await db.Rfqs.AsNoTracking().ToListAsync(ct);
            }
            """;

        const string scopes = """
            public sealed class ListOursHandler(AppDbContext db, IScopeContext scope)
            {
                public async Task<List<Rfq>> HandleAsync(CancellationToken ct) =>
                    await db.Rfqs.AsNoTracking().Where(r => r.OrganizationId == scope.OrganizationId).ToListAsync(ct);
            }
            """;

        ScanSource("Leaks.cs", leaks).Should().ContainSingle().Which.Scoped.Should().BeFalse();
        ScanSource("Scopes.cs", scopes).Should().ContainSingle().Which.Scoped.Should().BeTrue();

        const string delegates = """
            public sealed class ListViaRuleHandler(AppDbContext db, IScopeContext scope)
            {
                public async Task<object?> HandleAsync(string code, CancellationToken ct)
                {
                    var resolved = await BuyerProposalVisibilityRule.ResolveAsync(db, scope, code, ct);
                    return await db.Proposals.AsNoTracking().Where(p => p.RfqId == resolved!.Value.Rfq.Id).ToListAsync(ct);
                }
            }
            """;
        ScanSource("Delegates.cs", delegates).Should().ContainSingle().Which.Scoped.Should().BeTrue();

        const string saysButDoesNot = """
            /// <summary>No organization predicate - the inversion the governance overview documents.</summary>
            public sealed class ListEverythingAnywayHandler(AppDbContext db)
            {
                // scope.OrganizationId is deliberately not read here.
                public async Task<List<Rfq>> HandleAsync(CancellationToken ct) =>
                    await db.Rfqs.AsNoTracking().ToListAsync(ct);
            }
            """;
        ScanSource("Prose.cs", saysButDoesNot).Should().ContainSingle().Which.Scoped.Should().BeFalse();
    }

    private static List<HandlerScan> Scan()
    {
        var results = new List<HandlerScan>();

        foreach (var file in Directory.EnumerateFiles(InfrastructureDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            results.AddRange(ScanSource(Path.GetFileName(file), File.ReadAllText(file)));
        }

        return results;
    }

    private static List<HandlerScan> ScanSource(string fileName, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var results = new List<HandlerScan>();

        foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var name = declaration.Identifier.ValueText;
            if (!name.Contains("Handler", StringComparison.Ordinal)) continue;

            var body = StripComments(declaration.ToFullString());

            var tables = ScopedSets
                .Where(set => body.Contains($"db.{set}", StringComparison.Ordinal))
                .ToArray();
            if (tables.Length == 0) continue;

            var scoped = ScopingVocabulary.Any(term => body.Contains(term, StringComparison.Ordinal));
            results.Add(new HandlerScan(fileName, name, scoped, tables));
        }

        return results;
    }

    private static string StripComments(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var comments = root.DescendantTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia)
                        || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
                        || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                        || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            .ToList();

        return root.ReplaceTrivia(comments, (_, _) => default).ToFullString();
    }

    private static string InfrastructureDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MotsSupplierPortal.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the check cannot find the backend solution root from the test binaries");

        var infrastructure = Path.Combine(directory!.FullName, "Infrastructure");
        Directory.Exists(infrastructure).Should().BeTrue($"handlers are expected under {infrastructure}");
        return infrastructure;
    }
}
