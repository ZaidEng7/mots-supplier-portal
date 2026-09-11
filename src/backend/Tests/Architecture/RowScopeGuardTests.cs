using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MotsSupplierPortal.Tests.Architecture;

/// <summary>
/// Every handler that reads a row-scoped table either scopes it to the caller, or is named here with
/// the reason it does not.
///
/// <para><b>Why this check exists.</b> Row-scoping is the rule this product most depends on -
/// <c>RISK-004</c> is the risk register's only Critical entry, and it names this exact leak: "a
/// supplier sees another supplier's proposal, or one buying entity sees another's RFQ". Its stated
/// mitigation is "scoping asserted in a shared query pipeline, not per-endpoint ad hoc". There is no
/// shared pipeline. There is no <c>HasQueryFilter</c> anywhere in <c>AppDbContext</c>, no base
/// handler, and no test over <c>OrganizationId</c>. Seventy handlers get it right by hand, and a
/// seventy-first that forgot would fail nothing at all: the build is green, every suite is green, and
/// the rows simply come back.</para>
///
/// <para><b>What it reads.</b> Source, matched syntactically, the same way <c>FilterGuardTests</c>
/// reads endpoint lambdas: each <c>public sealed class *Handler*</c> in Infrastructure, whether its
/// body touches one of the row-scoped <see cref="ScopedSets"/>, and whether it mentions one of the
/// scoping constructs in <see cref="ScopingVocabulary"/> anywhere. There is no semantic model, so the
/// match is on spelling.</para>
///
/// <para><b>"Mentions a scoping construct anywhere" is deliberately weak</b>, and the first draft of
/// this check proved why it has to be. A rule of "references <c>scope.OrganizationId</c>" reported
/// <c>ListBuyerProposalsHandler</c> as unscoped - a handler that is scoped, correctly, by delegating
/// to <c>BuyerProposalVisibilityRule.ResolveAsync</c>, which carries
/// <c>r.OrganizationId == scope.OrganizationId</c> in a class of its own. Delegation is the shape the
/// good handlers use; a check that could not see through it would have reported the best-written code
/// in the repository and been switched off within a week. So the rule is "reach for the scope", and
/// proving WHICH rows a handler actually filtered would need dataflow analysis this project does not
/// have.</para>
///
/// <para>That weakness is worth stating plainly: this check catches a handler that never scopes at
/// all, which is the failure that has actually shipped elsewhere in this codebase. It does not catch
/// a handler that scopes one query and forgets a second. <c>ScopedQueryTests</c> in the integration
/// suite is the other half - two organisations, and the second may not see the first's rows.</para>
/// </summary>
public sealed class RowScopeGuardTests
{
    /// <summary>
    /// The tables whose rows belong to somebody. A read of one of these without a scope is a read of
    /// every organisation's, or every supplier's.
    ///
    /// <para><c>Suppliers</c> is on this list and it is the subtle one: a supplier has no
    /// <c>OrganizationId</c> at all - the registry is national, and <c>SupplierOrgLink</c> is a
    /// separate many-to-many. So a buyer-side handler reading <c>db.Suppliers</c> unscoped is usually
    /// correct, while a SUPPLIER-side one reading it unscoped is a cross-tenant leak. The list names
    /// the table; the exemptions below carry that distinction, one handler at a time.</para>
    /// </summary>
    private static readonly string[] ScopedSets =
    [
        "Rfqs", "Proposals", "Suppliers", "Offerings", "Invitations", "SupplierDocuments",
        "Awards", "Evaluations", "Addresses", "BankAccounts", "Contacts", "Branches",
        "Representatives", "CategoryLinks",
    ];

    /// <summary>
    /// What reaching for the caller's scope looks like in this codebase.
    ///
    /// <para>Three claims from the token - organisation, supplier, user - and the five named helpers
    /// that hold a scoping predicate on behalf of the handlers that call them. <c>scope.UserId</c>
    /// counts because one family of handlers is scoped by ASSIGNMENT rather than by tenancy: an
    /// evaluator may belong to no organisation at all, and <c>ListMyAssignmentsHandler</c> filters on
    /// <c>EvaluatorUserId == userId</c>, which is a scope in every sense that matters.</para>
    /// </summary>
    private static readonly string[] ScopingVocabulary =
    [
        "scope.OrganizationId", "scope.SupplierId", "scope.UserId",
        "LoadScopedAsync", "LoadScopedByOrgAsync", "LoadScopedByAssignmentAsync",
        "ScopedQuery", "SupplierCodeScope", "BuyerProposalVisibilityRule",
    ];

    /// <summary>
    /// The handlers that read a scoped table without scoping it, each with the reason that is right.
    ///
    /// <para>Typed out by hand, one entry per handler, for the reason every exemption list in this
    /// repository is: a pattern would let the next one join it silently. Every entry below was read
    /// before it was written, and they fall into four groups.</para>
    /// </summary>
    private static readonly Dictionary<string, string> DeliberatelyUnscoped = new(StringComparer.Ordinal)
    {
        // ── Cross-organisation by grant. BRULE-086 gives the Ministry an aggregate view across every
        // buying body, and D-66 widened it to named rows. Pinning these to one organisation would not
        // narrow a leak, it would break the feature.
        ["GetGovernanceOverviewHandler"] = "BRULE-086: the Ministry's view is cross-organisation by grant",
        ["ListMinistryRfqsHandler"] = "BRULE-086/D-66: every tender in the country, whoever is running it",
        ["ListMinistrySuppliersHandler"] = "BRULE-086: the national supplier registry, not one buyer's list",
        ["GetMinistryAwardAnalyticsHandler"] = "BRULE-086: award analytics across every buying body",
        ["GetMinistryRfqDetailHandler"] = "D-66: one tender read-only with every bid, cross-organisation",
        ["GetCategoryCoverageHandler"] = "SCR-604: category coverage of the national registry",

        // ── The supplier registry is national. A Supplier carries no OrganizationId; the link to a
        // buying body is a separate many-to-many that these handlers do not read. Scoping them to the
        // caller's organisation would hide every supplier from every reviewer.
        ["ListSupplierDirectoryHandler"] = "SCR-601: the national registry; a supplier belongs to no buying body",
        ["ListComplianceDirectoryHandler"] = "compliance is assessed nationally, not per buying body",
        ["ListSupplierDocumentsPagedHandler"] = "reviewer surface over the national registry; the route names the supplier",
        ["GetReviewerSupplierViewHandler"] = "reviewer surface over the national registry; the route names the supplier",
        ["SearchBuyerOfferingsHandler"] = "the offering catalogue is national; any buyer may search any supplier's",
        ["ComplianceReportHandler"] = "states it on screen: these counts cover every registered supplier",

        // ── No caller to scope to. Both run before an account exists.
        ["RegisterSupplierHandler"] = "anonymous: it creates the supplier the scope would have named",
        ["VerifyEmailHandler"] = "anonymous: a token is the only identity the caller has",

        // ── Platform administration. Gated by permission rather than by scope, and deliberately so:
        // an administrator belongs to no organisation, so a scope predicate would return nothing.
        ["StorageSettingsHandler"] = "system_admin: storage totals for the deployment, not for a tenant",
        ["GetErpSyncMonitorHandler"] = "system_admin: the ERP queue is one queue for the deployment",
    };

    private sealed record HandlerScan(string File, string Name, bool Scoped, string[] Tables);

    [Fact]
    public void Every_handler_that_reads_a_scoped_table_reaches_for_the_callers_scope()
    {
        var found = Scan();

        // Non-vacuity, first, because a walk that matched nothing passes in silence - which is how
        // the other instruments in this project came to measure nothing.
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
        // The allow-list is the part that rots. An exemption for a handler that no longer exists, or
        // that has since been scoped, is an exemption nobody is reading - and the next handler to take
        // that name inherits a hole. Failing on a STALE entry is what stops the list growing quietly.
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
        // Revert-to-red, in the shape the real defect takes. Without this the two tests above would
        // pass just as well against a matcher that had stopped matching anything at all.
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

        // And the delegation case, which the first draft of this check got wrong: a handler that
        // scopes through a named helper is scoped.
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

        // And a comment is not a scope. This is the case the check got wrong on its first run.
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

    /// <summary>
    /// One file's handler classes. Separate from <see cref="Scan"/> so the revert-to-red control can
    /// run the same matcher over a sample rather than over a second implementation of it.
    /// </summary>
    private static List<HandlerScan> ScanSource(string fileName, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var results = new List<HandlerScan>();

        foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var name = declaration.Identifier.ValueText;
            if (!name.Contains("Handler", StringComparison.Ordinal)) continue;

            // Comments first, and this is not housekeeping. `ToFullString()` includes leading trivia,
            // and the first run of this check reported GetGovernanceOverviewHandler as SCOPED - on the
            // strength of its own doc comment, which reads "No organization predicate - the same
            // inversion the governance overview documents". The handler is deliberately unscoped and
            // says so, and the check read the saying as the doing.
            //
            // Three other guards in this repository have made the identical mistake: a contrast sweep
            // that measured a colour quoted in prose, a heading sweep that matched the tag it forbids
            // inside its own comment, and a motion sweep that found a class name in the sentence
            // explaining its deletion. A parser that cannot tell a usage from a mention of one is not
            // reading the code.
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

    /// <summary>Source with every comment removed, so prose about scoping cannot pass for scoping.</summary>
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

    /// <summary>
    /// Walks up from the test binaries to the repository, so this works from `dotnet test`, from an
    /// IDE, and in CI without any of them agreeing on a working directory.
    /// </summary>
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
