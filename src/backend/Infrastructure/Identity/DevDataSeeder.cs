using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// Development-only demo data: one account per persona, and enough domain data that no screen shows
/// an empty state for want of a row.
///
/// <para><b>Why this exists.</b> <see cref="AdminSeeder"/> and <see cref="ReviewerSeeder"/> seed two
/// personas out of eight, and nothing seeds domain data at all - so five of the six dashboards render
/// as zeroes on a fresh database and cannot be verified by looking at them. An empty dashboard proves
/// nothing: a widget with a broken query and a widget with no rows look identical.</para>
///
/// <para><b>Development only, and idempotent.</b> Every step checks for its own output first, so
/// restarting the API does not duplicate anything. Guarded at the call site by the same
/// <c>IsDevelopment()</c> block the other two seeders sit in.</para>
///
/// <para><b>Lifecycle states are forced in storage where the journey is not the point.</b> Driving a
/// supplier to Approved through the domain needs a complete profile, documents uploaded and scanned,
/// and a reviewer decision - that is what Step 4's end-to-end walk is for. Here the states are set
/// directly, which is the same forced-transition pattern the integration suite already uses and
/// documents (<c>OfferingBuyerSearchTests.ActiveSupplierAsync</c>: "these tests are about RFQ
/// invitations, not the onboarding journey"). The domain is still the authority on transitions; this
/// is a fixture, and it says so.</para>
///
/// <para><b>What it deliberately does not do:</b> it invents no tender outcome. Scores, consolidation
/// and award decisions that would need a rule (a tie-break, a threshold, a winner) are left for a
/// person to drive through the UI - the seeder stops at "an evaluation is open and part-scored" and
/// "an award is in flight", which are positions, not verdicts.</para>
/// </summary>
public static class DevDataSeeder
{
    /// <summary>The shared password every seeded demo account gets. Documented in RUNBOOK.md, because a
    /// seeded login nobody can use is not a seeded login.
    /// <para>Not a secret and not a credential in the sense the rule means: this constant is only ever
    /// read from <see cref="SeedAsync"/>, which refuses to run outside Development - see the guard there.
    /// Making it a configuration value would move the same string into appsettings and buy nothing.</para>
    /// </summary>
    public const string Password = "motsdemo2026"; // NOSONAR S2068 - see above: dev-only seed data, guarded by environment.

    private const string OrganizationNameEn = "MOT Procurement Body";

    /// <summary>email, full name, role, and whether the account belongs to the demo supplier.</summary>
    private static readonly (string Email, string FullName, string Role, bool IsSupplierUser)[] Personas =
    [
        ("officer@mots.local", "Demo Procurement Officer", Roles.ProcurementOfficer, false),
        ("manager@mots.local", "Demo Procurement Manager", Roles.ProcurementManager, false),
        // A SECOND manager, and it is not padding.
        //
        // §6.1's segregation of duties refuses an approver who is also the recommender - correctly, and the
        // refusal is explicit on screen ("The approver must differ from the recommender"). With one manager
        // seeded, that meant the award-approval step could not be reached at all: whoever recommended could
        // neither approve nor reject, and no other account held award.approve. Found by walking a tender to the
        // approval gate and being unable to pass it in either direction.
        ("manager2@mots.local", "Demo Procurement Manager (Second)", Roles.ProcurementManager, false),
        ("evaluator@mots.local", "Demo Evaluator", Roles.Evaluator, false),
        ("ministry@mots.local", "Demo Ministry Viewer", Roles.MinistryViewer, false),   // organization deliberately null - see below
        ("supplier@mots.local", "Demo Supplier Admin", Roles.SupplierAdmin, true),
        ("supplier.user@mots.local", "Demo Supplier User", Roles.SupplierUser, true),
    ];

    public static async Task SeedAsync(
        AppDbContext db, UserManager<AppUser> userManager, IConfiguration configuration, IHostEnvironment environment)
    {
        // Refused outside Development, in the seeder rather than only at the call site.
        //
        // This creates eight accounts whose password is a compile-time constant published in RUNBOOK.md.
        // The caller already guards on IsDevelopment(), and that was the whole protection: a second call
        // added anywhere, or that block being widened, silently seeds known credentials into whatever
        // environment is running. The dangerous knowledge lives in this file, so the refusal belongs here
        // too - and it costs one parameter.
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"DevDataSeeder seeds accounts with a published password and must never run outside "
                + $"Development. Current environment: {environment.EnvironmentName}.");
        }

        var password = configuration["DevSeed:DemoPassword"] ?? Password;

        var organizationId = await SeedOrganizationAsync(db);
        var demoSupplierId = await SeedSuppliersAsync(db);
        await SeedUsersAsync(userManager, password, organizationId, demoSupplierId);
        await SeedTendersAsync(db, organizationId, demoSupplierId);
        await SeedVolumeAsync(db, organizationId);
        await SeedVolumePricingAsync(db);
        await SeedVolumeAwardsAsync(db);
        await SeedHistoricAwardsAsync(db, organizationId);
        await EnableCommercialVisibilityAsync(db);
    }

    /// <summary>
    /// D-66/D-57: the Ministry's commercial visibility, on for the DEMONSTRATION data and nowhere else.
    ///
    /// <para><b>Why this lives in the seeder rather than in a migration.</b> It was a migration first, and
    /// that was wrong: a migration runs in every environment, so the same deployment step that creates the
    /// schema in production would have switched the disclosure on there too. The approval it rests on is
    /// explicitly bounded - "demonstration data only, there are no real bidders and no real bid values" -
    /// and a mechanism that ignores the boundary makes the approval mean something it does not say.</para>
    ///
    /// <para>So the switch now lives where the demonstration data itself lives: behind the same
    /// <c>DevSeed:Enabled</c> gate, in a seeder that refuses to run outside Development. In every other
    /// environment the flag keeps its seeded value, which is OFF (D-6/BRULE-087), and turning it on there
    /// requires the written sign-off D-57 names - a person, a date, and the scope - recorded before the
    /// row is changed.</para>
    /// </summary>
    private static async Task EnableCommercialVisibilityAsync(AppDbContext db)
    {
        var updated = await db.SupplierFieldConfigs
            .Where(c => c.Category == FieldConfigCategory.GovernanceVisibility && c.FieldCode == "commercialValues")
            .ExecuteUpdateAsync(p => p.SetProperty(c => c.IsEnabled, true));

        if (updated == 0)
        {
            db.SupplierFieldConfigs.Add(new SupplierFieldConfig
            {
                Id = Guid.CreateVersion7(),
                Category = FieldConfigCategory.GovernanceVisibility,
                FieldCode = "commercialValues",
                IsEnabled = true,
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// The buying organization every staff persona belongs to.
    ///
    /// <para>The Id is READ BACK rather than fixed: <c>Organization.Create</c> assigns its own
    /// GUIDv7, and rewriting a primary key after the fact would leave every row that already points
    /// at it pointing at nothing. Looked up by name on re-runs so the value is stable across
    /// restarts without being hard-coded.</para>
    /// </summary>
    private static async Task<Guid> SeedOrganizationAsync(AppDbContext db)
    {
        var existing = await db.Organizations
            .Where(o => o.LegalNameEn == OrganizationNameEn)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync();
        if (existing is { } id) return id;

        var organization = Organization.Create(
            await Registrations.ReferenceCodeGenerator.NextCodeAsync(db, "ORG", CancellationToken.None),
            "وزارة السياحة - هيئة المشتريات", OrganizationNameEn, OrganizationType.MotBody,
            "procurement@mots.local", "+963110000000");
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        return organization.Id;
    }

    /// <summary>
    /// Suppliers at four lifecycle positions, so the reviewer's queue, the compliance report and the
    /// supplier directory each have more than one kind of row to render.
    /// </summary>
    private static async Task<Guid> SeedSuppliersAsync(AppDbContext db)
    {
        var existing = await db.Suppliers.FirstOrDefaultAsync(s => s.ReferenceCode == "SUP-DEMO-0001");
        if (existing is not null) return existing.Id;

        var approved = Register(db, "SUP-DEMO-0001", "شركة العرض للتوريدات", "Demo Supplies Co", "RC-DEMO-0001");
        var submitted = Register(db, "SUP-DEMO-0002", "مؤسسة النخيل التجارية", "Palm Trading Est", "RC-DEMO-0002");
        var underReview = Register(db, "SUP-DEMO-0003", "شركة الشام للخدمات", "Sham Services Co", "RC-DEMO-0003");
        var suspended = Register(db, "SUP-DEMO-0004", "شركة الفرات للمقاولات", "Euphrates Contracting", "RC-DEMO-0004");
        var draft = Register(db, "SUP-DEMO-0005", "شركة البادية للتجهيزات", "Badia Equipment Co", "RC-DEMO-0005");

        await db.SaveChangesAsync();

        await SetStateAsync(db, approved.Id, SupplierOnboardingState.Approved, SupplierLifecycleState.Active);
        await SetStateAsync(db, submitted.Id, SupplierOnboardingState.Submitted, SupplierLifecycleState.None);
        await SetStateAsync(db, underReview.Id, SupplierOnboardingState.UnderReview, SupplierLifecycleState.None);
        await SetStateAsync(db, suspended.Id, SupplierOnboardingState.Approved, SupplierLifecycleState.Suspended);
        await SetStateAsync(db, draft.Id, SupplierOnboardingState.ProfileInProgress, SupplierLifecycleState.None);

        // The queue-entry audit rows for the two suppliers that are actually in the review queue.
        //
        // Found by reading SCR-300 as the reviewer: the counters said one Submitted and one UnderReview, and
        // the wait-time widget said "no open applications". Both were right. That widget does not read the
        // supplier row - it reads the AUDIT LOG, because "how long has this been waiting" is a question about
        // when the application entered the queue, and the state column cannot answer it (ReviewDashboardHandler
        // says so in its own comment). Forcing the state with ExecuteUpdateAsync writes no audit row, so the
        // fixture had a supplier in the queue with no record of arriving.
        //
        // These rows are not invented history: they say the thing the seeder actually did. Backdated by three
        // and nine days so the widget has a number to show and the ordering is visible - a queue where
        // everything arrived this second is a queue nobody can prioritise.
        await SeedQueueEntryAuditAsync(db, submitted.Id, "SUP-DEMO-0002", daysAgo: 9);
        await SeedQueueEntryAuditAsync(db, underReview.Id, "SUP-DEMO-0003", daysAgo: 3);

        return approved.Id;
    }

    /// <summary>
    /// One "application_submitted" row, which is what SCR-300's wait-time widget and the review queue's own
    /// age column both measure from.
    ///
    /// <para>Written directly rather than through IAuditLogger because the seeder has no request scope and no
    /// actor - and the actor is honestly the system here, which is what ActorKind.System says.</para>
    /// </summary>
    private static async Task SeedQueueEntryAuditAsync(AppDbContext db, Guid supplierId, string referenceCode, int daysAgo)
    {
        if (await db.AuditLogs.AnyAsync(a => a.AggregateId == supplierId && a.Action == "application_submitted")) return;

        db.AuditLogs.Add(new Domain.Audit.AuditLog
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
            ActorKind = Domain.Audit.AuditActorKind.System,
            ActorLabel = "dev-seed",
            AggregateType = "Supplier",
            AggregateId = supplierId,
            ReferenceCode = referenceCode,
            Action = "application_submitted",
            ToState = SupplierOnboardingState.Submitted.ToString(),
        });
        await db.SaveChangesAsync();
    }

    private static Supplier Register(AppDbContext db, string code, string nameAr, string nameEn, string registration)
    {
        var supplier = Supplier.Register(code, nameAr, nameEn, registration,
            $"{nameEn} Representative", $"rep.{code.ToLowerInvariant()}@example.com", "+963900000000");

        // The five profile items Submit() requires, so a seeded supplier is one a reviewer can act on
        // rather than one that fails its own gate the moment somebody clicks Approve.
        supplier.MarkEmailVerified();
        supplier.UpdateCoreProfile($"{nameEn} - demo fixture", "https://example.com", null, "SYP");
        supplier.AddAddress(AddressKind.HeadOffice, "1 Demo Street", null, "Damascus", "DM", "SY", null, null, null);
        supplier.LinkCategory("catering", isComplianceCritical: false);
        supplier.AcceptTerms("v1");

        db.Suppliers.Add(supplier);
        return supplier;
    }

    private static Task SetStateAsync(AppDbContext db, Guid id, SupplierOnboardingState onboarding, SupplierLifecycleState lifecycle) =>
        db.Suppliers.Where(s => s.Id == id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, onboarding)
            .SetProperty(s => s.LifecycleState, lifecycle));

    /// <summary>
    /// One RFQ at each stage a buyer screen renders differently, plus the proposals, evaluation and
    /// award that hang off the late ones.
    ///
    /// <para><b>Stops short of any verdict.</b> The evaluation is opened, assigned and part-scored;
    /// it is not consolidated, and no award is approved or issued. Consolidation ranks bids and an
    /// award names a winner - both are outcomes, and a fixture that invents one puts a tender result
    /// in the database that nobody decided. The screens that show those states are reached by
    /// driving them in the UI, which is what Step 4's walk does.</para>
    /// </summary>
    private static async Task SeedTendersAsync(AppDbContext db, Guid organizationId, Guid demoSupplierId)
    {
        if (await db.Rfqs.AnyAsync(r => r.ReferenceCode.StartsWith("RFQ-DEMO"))) return;

        var officerId = await db.Users.Where(u => u.Email == "officer@mots.local").Select(u => u.Id).FirstOrDefaultAsync();
        var evaluatorId = await db.Users.Where(u => u.Email == "evaluator@mots.local").Select(u => u.Id).FirstOrDefaultAsync();

        var template = EvaluationTemplate.Create("قالب التقييم التجريبي", "Demo Evaluation Template");
        var technical = template.AddCriterion("الجودة الفنية", "Technical quality", CriterionDimension.Technical,
            weight: 60m, maxScore: 10m, threshold: null, ScoringType.Numeric, null, null);
        var commercial = template.AddCriterion("السعر", "Price", CriterionDimension.Commercial,
            weight: 40m, maxScore: 10m, threshold: null, ScoringType.Numeric, null, null);
        template.Activate();
        db.EvaluationTemplates.Add(template);
        await db.SaveChangesAsync();

        // The snapshot in the SHAPE THE HANDLER READS, which the first version of this fixture got wrong.
        //
        // It wrote { "criteria": ["Technical", "Commercial"] } - a plausible-looking object that
        // OpenEvaluationHandler cannot parse: it deserialises this column into a LIST of criterion records, so
        // opening evaluation on any seeded RFQ answered 500 with a JsonException. Nothing caught it, because
        // the seeded evaluation on RFQ-DEMO-0005 was built by constructing the aggregate directly rather than
        // going through the handler - the fixture had quietly bypassed the code path it was meant to set up.
        // Found by walking the tender in the browser: "Open evaluation" 500'd.
        //
        // Built by projecting the template's own criteria, the same projection BindEvaluationTemplateHandler
        // makes, so the fixture and production write one shape.
        var snapshot = System.Text.Json.JsonSerializer.Serialize(template.Criteria.Select(c => new
        {
            c.Id,
            c.NameAr,
            c.NameEn,
            Dimension = c.Dimension.ToString(),
            c.Weight,
            c.MaxScore,
            c.Threshold,
            ScoringType = c.ScoringType.ToString(),
            c.RequiresJustification,
        }));

        // Draft / InternalReview / Approved / SubmissionOpen / SubmissionClosed - the five positions
        // the buyer's list, board and workspace each render differently.
        var draft = NewRfq(db, "RFQ-DEMO-0001", organizationId, "توريد مواد غذائية", "Catering supplies", officerId, template.Id, snapshot);
        var inReview = NewRfq(db, "RFQ-DEMO-0002", organizationId, "خدمات نظافة", "Cleaning services", officerId, template.Id, snapshot);
        var approved = NewRfq(db, "RFQ-DEMO-0003", organizationId, "أثاث مكتبي", "Office furniture", officerId, template.Id, snapshot);
        var open = NewRfq(db, "RFQ-DEMO-0004", organizationId, "تجهيزات مطابخ", "Kitchen equipment", officerId, template.Id, snapshot);
        var closed = NewRfq(db, "RFQ-DEMO-0005", organizationId, "صيانة مصاعد", "Lift maintenance", officerId, template.Id, snapshot);
        // A SIXTH, carrying the proposal that sits in ClarificationRequested. It needs its own RFQ
        // rather than a second proposal on RFQ-DEMO-0005: IX_proposal_RfqId_SupplierId is unique over
        // (RfqId, SupplierId) filtered to the live states, so one supplier gets one live bid per RFQ.
        // Found by the constraint refusing the first version of this fixture, which is the index doing
        // its job - see AppDbContext's own note on why Withdrawn/Lapsed/Cancelled are excluded.
        var clarifying = NewRfq(db, "RFQ-DEMO-0006", organizationId, "قرطاسية", "Stationery", officerId, template.Id, snapshot);
        await db.SaveChangesAsync();

        inReview.SubmitForReview();
        db.RfqApprovals.Add(inReview.Approvals.Single(a => a.Decision is null));

        approved.SubmitForReview();
        db.RfqApprovals.Add(approved.Approvals.Single(a => a.Decision is null));
        approved.Approve(officerId);

        open.SubmitForReview();
        db.RfqApprovals.Add(open.Approvals.Single(a => a.Decision is null));
        open.Approve(officerId);
        open.Publish();

        closed.SubmitForReview();
        db.RfqApprovals.Add(closed.Approvals.Single(a => a.Decision is null));
        closed.Approve(officerId);
        closed.Publish();

        clarifying.SubmitForReview();
        db.RfqApprovals.Add(clarifying.Approvals.Single(a => a.Decision is null));
        clarifying.Approve(officerId);
        clarifying.Publish();
        await db.SaveChangesAsync();

        // Now that both are Published, move their windows into the past so OpenSubmissionWindow is
        // a legal transition rather than a lie about the clock.
        await db.Rfqs.Where(r => r.Id == open.Id || r.Id == closed.Id || r.Id == clarifying.Id)
            .ExecuteUpdateAsync(p => p.SetProperty(r => r.SubmissionOpensAt, DateTimeOffset.UtcNow.AddHours(-1)));
        db.ChangeTracker.Clear();

        open = await db.Rfqs.Include(r => r.Approvals).FirstAsync(r => r.Id == open.Id);
        closed = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations).FirstAsync(r => r.Id == closed.Id);
        clarifying = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations).FirstAsync(r => r.Id == clarifying.Id);
        open.OpenSubmissionWindow();
        closed.OpenSubmissionWindow();
        clarifying.OpenSubmissionWindow();
        await db.SaveChangesAsync();

        // A drafted proposal on the open RFQ, a submitted one on the RFQ about to close, and one
        // waiting on a clarification, so the supplier's list has all three shapes and the buyer's
        // received-proposals list has rows to open.
        var drafted = Proposal.Create("PRP-DEMO-0001", open.Id, demoSupplierId);
        var submitted = Proposal.Create("PRP-DEMO-0002", closed.Id, demoSupplierId);
        var underClarification = Proposal.Create("PRP-DEMO-0003", clarifying.Id, demoSupplierId);
        db.Proposals.AddRange(drafted, submitted, underClarification);
        await db.SaveChangesAsync();
        await db.Proposals.Where(p => p.Id == submitted.Id || p.Id == underClarification.Id)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.State, ProposalState.Submitted)
                                      .SetProperty(x => x.SubmittedAt, DateTimeOffset.UtcNow.AddHours(-2)));

        // SCR-155 needs a proposal actually sitting in ClarificationRequested. Submitted was forced
        // with ExecuteUpdateAsync because Submit() checks the submission window and this RFQ's has
        // closed; from there the two transitions have no window dependency, so they run as the real
        // domain methods and the seeded row is one the aggregate would accept.
        db.ChangeTracker.Clear();
        var toClarify = await db.Proposals.FirstAsync(p => p.Id == underClarification.Id);
        toClarify.OpenForReview();
        toClarify.RequestClarification(
            "Please confirm whether the quoted lead time includes customs clearance, and restate the "
            + "warranty period in months.");
        await db.SaveChangesAsync();

        // ExecuteUpdateAsync writes behind the change tracker, so anything still tracked now holds a
        // stale RowVersion and the next SaveChanges loses to the app-managed concurrency guard - a
        // DbUpdateConcurrencyException the seeder cannot recover from. Reload rather than reuse.
        db.ChangeTracker.Clear();
        closed = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations).FirstAsync(r => r.Id == closed.Id);
        clarifying = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations).FirstAsync(r => r.Id == clarifying.Id);

        closed.CloseSubmissionWindow(reason: null, isEarlyClose: false);
        closed.OpenEvaluation();

        // Closed too, and deliberately NOT moved to evaluation. A clarification is asked of a
        // proposal under review; leaving this RFQ's window open while one of its bids is being
        // questioned would be a state the process does not produce.
        clarifying.CloseSubmissionWindow(reason: null, isEarlyClose: false);
        await db.SaveChangesAsync();

        // Part-scored: one criterion of one bid, by one evaluator. Enough for the evaluator's
        // workspace and the officer's progress panel to have something real; short of a submitted
        // evaluation, which would start ranking.
        // Nothing else needs to stay tracked past this point, and leaving the RFQ tracked made the
        // evaluation's own save lose to the app-managed version guard on a row it had not touched.
        db.ChangeTracker.Clear();

        var evaluation = Domain.Evaluation.Evaluation.Create(closed.Id, [
            new CriterionSnapshotInput(technical.NameAr, technical.NameEn, CriterionDimension.Technical, 60m, 10m, null, ScoringType.Numeric),
            new CriterionSnapshotInput(commercial.NameAr, commercial.NameEn, CriterionDimension.Commercial, 40m, 10m, null, ScoringType.Numeric),
        ]);
        evaluation.AssignEvaluators([evaluatorId]);

        // Assigned -> InProgress. Scoring is refused from Assigned, correctly: an evaluator who has
        // not opened the workspace has not seen the bidder list, and A-8 puts the conflict
        // declaration on that opening.
        evaluation.OpenScoring(evaluatorId);

        var firstCriterion = evaluation.Criteria.First(c => c.Dimension == CriterionDimension.Technical);
        evaluation.ScoreCriterion(evaluatorId, submitted.Id, firstCriterion.Id, 8m, null, null, new HashSet<Guid> { submitted.Id });

        // ONE save for the whole aggregate. Adding it, saving, then mutating and saving again made
        // the second UPDATE match zero rows: the app-managed version column is assigned on insert and
        // the tracked entity's copy did not carry forward, so the guard refused its own write.
        db.Evaluations.Add(evaluation);
        await db.SaveChangesAsync();

        // An award IN FLIGHT, which the brief asks for by name: Recommended, and nothing beyond it.
        //
        // The line this stops at matters. Recommending is a step somebody takes and can be seen to have
        // taken - it names a proposal and a justification and waits for a manager. Approving it, or
        // executing it, produces a RESULT: an approved award is a decision this fixture has no standing
        // to make, and an executed one emits the ERP request. So the manager's approval queue has a real
        // row to work, SCR-723 has a real row to show, and no tender in this database has a winner.
        //
        // Not routed for approval either: routing is the recommender handing it on, and leaving it
        // un-routed is what makes it visibly "in flight" rather than waiting on someone specific.
        db.ChangeTracker.Clear();
        var award = Domain.Awards.Award.Recommend(
            closed.Id, submitted.Id,
            "أفضل عرض من حيث السعر والمواصفات الفنية.",
            "Best combination of price and technical specification.",
            officerId);
        db.Awards.Add(award);
        await db.SaveChangesAsync();
    }

    // ── Volume ───────────────────────────────────────────────────────────────────────────────────
    //
    // Everything above is a CURATED fixture: one supplier at each lifecycle position, one tender at
    // each state, one proposal in each shape. It is what the walkthrough follows and what the
    // dashboards need in order to render something other than a zero.
    //
    // It is not what the product looks like in use. Six tenders fit on one screen with no scrollbar,
    // every list is one page, no filter narrows anything, no ranking has a second place, and a table
    // whose column would collapse under real width never gets the chance. A demonstration on that data
    // shows the screens working and says nothing about whether they hold.
    //
    // So this adds bulk beside it. The codes are a separate range - SUP-DEMO-01xx and RFQ-DEMO-01xx -
    // so nothing here can be mistaken for the curated rows the walkthrough names, and so a reader can
    // tell at a glance which is which.

    /// <summary>Category codes seeded by <c>AppDbContext</c>, so a linked category always resolves.</summary>
    private static readonly string[] Categories =
        ["catering", "transport", "maintenance", "accommodation", "events", "tour_operations"];

    /// <summary>Company names that read like companies, so a directory of them can be scanned.</summary>
    private static readonly (string Ar, string En)[] BulkCompanies =
    [
        ("شركة بردى للتوريدات", "Barada Supplies"), ("مؤسسة الياسمين التجارية", "Yasmine Trading"),
        ("شركة قاسيون للنقل", "Qasioun Transport"), ("شركة الميادين للمقاولات", "Mayadeen Contracting"),
        ("مؤسسة العاصي للتجهيزات", "Orontes Equipment"), ("شركة تدمر للضيافة", "Tadmur Hospitality"),
        ("شركة اللاذقية البحرية", "Latakia Marine"), ("مؤسسة حلب الصناعية", "Aleppo Industrial"),
        ("شركة حمص للصيانة", "Homs Maintenance"), ("شركة درعا الزراعية", "Daraa Agricultural"),
        ("مؤسسة السويداء للخدمات", "Suwayda Services"), ("شركة طرطوس للشحن", "Tartus Freight"),
        ("شركة دير الزور للطاقة", "Deir ez-Zor Energy"), ("مؤسسة الرقة للبناء", "Raqqa Construction"),
        ("شركة إدلب للتغليف", "Idlib Packaging"), ("شركة الحسكة للحبوب", "Hasakah Grain"),
        ("مؤسسة القنيطرة اللوجستية", "Quneitra Logistics"), ("شركة صافيتا للأثاث", "Safita Furniture"),
        ("شركة مصياف للمعدات", "Masyaf Machinery"), ("مؤسسة جبلة للتبريد", "Jableh Refrigeration"),
        ("شركة السلمية للطباعة", "Salamiyah Printing"), ("شركة معلولا للترجمة", "Maaloula Translation"),
        ("مؤسسة عفرين للزيوت", "Afrin Oils"), ("شركة الزبداني للمياه", "Zabadani Water"),
    ];

    /// <summary>
    /// Suppliers and tenders in quantity, so every list has more rows than fit on a screen.
    ///
    /// <para>Idempotent on its own first row, like every other step here: re-running the API adds
    /// nothing. Lifecycle positions are spread by index rather than chosen one at a time, so the
    /// proportions are visible in the code - roughly half approved and active, which is what a working
    /// register looks like, and the rest spread across the states a reviewer actually sees.</para>
    /// </summary>
    private static async Task SeedVolumeAsync(AppDbContext db, Guid organizationId)
    {
        if (await db.Suppliers.AnyAsync(s => s.ReferenceCode == "SUP-DEMO-0101")) return;

        var suppliers = new List<Supplier>();
        for (var i = 0; i < BulkCompanies.Length; i++)
        {
            var (nameAr, nameEn) = BulkCompanies[i];
            var code = $"SUP-DEMO-{101 + i:0000}";
            var supplier = Register(db, code, nameAr, nameEn, $"RC-DEMO-{101 + i:0000}");
            // A second category on every third one, so the coverage report has rows that differ and
            // the directory's category filter narrows to something rather than to everything.
            supplier.LinkCategory(Categories[i % Categories.Length], isComplianceCritical: i % 4 == 0);
            suppliers.Add(supplier);
        }
        await db.SaveChangesAsync();

        for (var i = 0; i < suppliers.Count; i++)
        {
            var (onboarding, lifecycle) = (i % 8) switch
            {
                0 => (SupplierOnboardingState.Submitted, SupplierLifecycleState.None),
                1 => (SupplierOnboardingState.UnderReview, SupplierLifecycleState.None),
                2 => (SupplierOnboardingState.ProfileInProgress, SupplierLifecycleState.None),
                3 => (SupplierOnboardingState.Approved, SupplierLifecycleState.Suspended),
                _ => (SupplierOnboardingState.Approved, SupplierLifecycleState.Active),
            };
            await SetStateAsync(db, suppliers[i].Id, onboarding, lifecycle);

            // The ones actually in the queue get their arrival recorded, spread over six weeks, because
            // the wait-time widget and the queue's age column both measure from that audit row rather
            // than from the supplier - and a queue where everything arrived at once cannot be ordered.
            if (onboarding is SupplierOnboardingState.Submitted or SupplierOnboardingState.UnderReview)
            {
                await SeedQueueEntryAuditAsync(db, suppliers[i].Id, suppliers[i].ReferenceCode, daysAgo: 1 + (i * 3) % 42);
            }
        }

        await SeedVolumeTendersAsync(db, organizationId, suppliers);
    }

    /// <summary>
    /// Every bulk bid gets priced lines.
    ///
    /// <para><b>A bid with no lines is worth nothing, everywhere.</b> A proposal's total is derived from
    /// its lines and never stored - that is the domain's own invariant - so eighty-odd bids created
    /// without any were zero on the comparison screen, zero in the awarded-value column, and zero in the
    /// Ministry's spend analytics. The screens were not wrong; there was nothing to show.</para>
    ///
    /// <para><b>Why it moves the state and puts it back.</b> Draft is the only state a proposal accepts
    /// an edit in, and that is the right rule, so this works with it rather than around it: the state
    /// flag is moved in storage, the aggregate does the pricing under its own guards, and the flag goes
    /// back. Moving that flag in storage is how these proposals were submitted in the first place - the
    /// fixture has never had a supplier session to submit them from.</para>
    /// </summary>
    private static async Task SeedVolumePricingAsync(AppDbContext db)
    {
        var unpriced = await db.Proposals.AsNoTracking()
            .Where(p => p.ReferenceCode.StartsWith("PRP-DEMO-01") && !db.ProposalItems.Any(i => i.ProposalId == p.Id))
            .Select(p => new { p.Id, p.State })
            .ToListAsync();
        if (unpriced.Count == 0) return;

        // In a transaction, because the middle of this leaves the fixture WORSE than it found it: the
        // proposals are Draft between the two flips, and a failure in between would leave sixteen
        // submitted bids sitting as drafts with their submission timestamps still on them. That is not
        // a hypothetical - the first run of this method threw on SaveChanges and did exactly that.
        await using var transaction = await db.Database.BeginTransactionAsync();

        var submitted = unpriced.Where(p => p.State == ProposalState.Submitted).Select(p => p.Id).ToList();
        await db.Proposals.Where(p => submitted.Contains(p.Id))
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.State, ProposalState.Draft));

        db.ChangeTracker.Clear();
        var ids = unpriced.Select(p => p.Id).ToList();
        var proposals = await db.Proposals.Include(p => p.Items).Where(p => ids.Contains(p.Id)).ToListAsync();
        var lines = await db.RfqItems.AsNoTracking()
            .Where(i => proposals.Select(p => p.RfqId).Contains(i.RfqId))
            .Select(i => new { i.Id, i.RfqId, i.Quantity })
            .ToListAsync();
        var byRfq = lines.GroupBy(l => l.RfqId).ToDictionary(g => g.Key, g => g.ToList());

        for (var i = 0; i < proposals.Count; i++)
        {
            if (!byRfq.TryGetValue(proposals[i].RfqId, out var rfqLines)) continue;
            foreach (var line in rfqLines)
            {
                // Spread either side of a notional list price, so no two bids on one tender are equal,
                // the cheapest is not always the first one invited, and a comparison has an order.
                proposals[i].SetItemPricing(line.Id, line.Quantity, 120m + ((i * 37) % 260),
                    discount: null, leadTimeDays: 7 + (i % 21), notesAr: null, notesEn: null);

                // Added to the set explicitly, which is what ManageProposalItemHandler does on the line
                // after its own SetItemPricing call and for the same reason. A ProposalItem carries a
                // key it assigns itself, so a child discovered inside a tracked parent's collection is
                // attached as Modified rather than Added - EF reads "has a key" as "already exists".
                // The first draft of this omitted it, and the seeder emitted twenty-one UPDATEs against
                // rows that had never been inserted: zero rows affected, and a
                // DbUpdateConcurrencyException that named nothing useful.
                db.ProposalItems.Add(proposals[i].Items.First(item => item.RfqItemId == line.Id));
            }
        }
        await db.SaveChangesAsync();

        await db.Proposals.Where(p => submitted.Contains(p.Id))
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.State, ProposalState.Submitted));

        await transaction.CommitAsync();
        db.ChangeTracker.Clear();
    }

    /// <summary>Tender subjects a Ministry of Transport would actually run.</summary>
    private static readonly (string Ar, string En)[] BulkTenders =
    [
        ("صيانة الطرق السريعة", "Motorway maintenance"), ("توريد إطارات الحافلات", "Bus fleet tyre supply"),
        ("خدمات النظافة في المرافئ", "Port cleaning services"), ("تجديد لافتات الطرق", "Road signage renewal"),
        ("توريد وقود الأسطول", "Fleet fuel supply"), ("صيانة حواجز السكك", "Rail crossing barriers"),
        ("خدمات الإطعام في المحطات", "Station catering services"), ("تأمين مواقف الشاحنات", "Truck park security"),
        ("توريد قطع غيار المصاعد", "Lift spare parts"), ("مسح أعماق الميناء", "Harbour dredging survey"),
        ("تجهيزات مكاتب المديرية", "Directorate office fit-out"), ("خدمات الترجمة الفورية", "Interpretation services"),
        ("تنظيم مؤتمر النقل", "Transport conference"), ("توريد أنظمة التذاكر", "Ticketing systems"),
        ("صيانة أنظمة التكييف", "Air conditioning maintenance"), ("خدمات الإقامة للوفود", "Delegation accommodation"),
        ("توريد معدات السلامة", "Safety equipment supply"), ("تدقيق أسطول المركبات", "Vehicle fleet audit"),
    ];

    /// <summary>
    /// Tenders across every state, most of them with bids on them.
    ///
    /// <para>The states are assigned by index for the same reason the supplier ones are: the shape of
    /// the pipeline is then a thing a reader can check against the code rather than a thing they have
    /// to count in the database. Every published tender invites between three and eight suppliers, so
    /// the invitation list scrolls and the "invited" count on the tab strip is a number worth reading.</para>
    /// </summary>
    private static async Task SeedVolumeTendersAsync(AppDbContext db, Guid organizationId, List<Supplier> suppliers)
    {
        var officerId = await db.Users.Where(u => u.Email == "officer@mots.local").Select(u => u.Id).FirstAsync();
        var template = await db.EvaluationTemplates.Include(t => t.Criteria).FirstAsync();
        var snapshot = System.Text.Json.JsonSerializer.Serialize(template.Criteria.Select(c => new
        {
            c.NameAr, c.NameEn, Dimension = c.Dimension.ToString(), c.Weight, c.MaxScore, c.Threshold,
            ScoringType = c.ScoringType.ToString(), c.RequiresJustification,
        }));

        // Only suppliers a buyer could really invite. Inviting a half-finished application would be a
        // fixture asserting something the product refuses.
        var invitable = suppliers
            .Where((_, i) => (i % 8) is not (0 or 1 or 2 or 3))
            .Select(s => s.Id)
            .ToList();

        var created = new List<(Rfq Rfq, int Index)>();
        for (var i = 0; i < BulkTenders.Length; i++)
        {
            var (titleAr, titleEn) = BulkTenders[i];
            var rfq = Rfq.Create($"RFQ-DEMO-{101 + i:0000}", organizationId, titleAr, titleEn, null, null, "SYP",
                publishAt: null,
                submissionOpensAt: DateTimeOffset.UtcNow.AddHours(1),
                // Staggered, so "closes in six days" differs from "closes in three weeks" and a list
                // sorted by closing date has an order worth sorting.
                submissionClosesAt: DateTimeOffset.UtcNow.AddDays(3 + (i * 5) % 40),
                clarificationDeadlineAt: null, evaluationTargetDate: null, ownerUserId: officerId);
            rfq.AddItem(titleAr, titleEn, null, null, Categories[i % Categories.Length],
                10m + (i * 37 % 500), "unit", isUnitPrice: true, isOptional: false);
            rfq.BindEvaluationTemplate(template.Id, 1, snapshot);
            foreach (var supplierId in invitable.Skip(i % 4).Take(3 + i % 6)) rfq.InviteSupplier(supplierId);
            db.Rfqs.Add(rfq);
            created.Add((rfq, i));
        }
        await db.SaveChangesAsync();

        // Draft, internal review, approved, published-and-open, closed. Same five positions the curated
        // fixture has one of each of, in the proportions a real pipeline carries.
        var published = new List<Guid>();
        foreach (var (rfq, i) in created)
        {
            var stage = i % 5;
            if (stage == 0) continue;

            rfq.SubmitForReview();
            db.RfqApprovals.Add(rfq.Approvals.Single(a => a.Decision is null));
            if (stage == 1) continue;

            rfq.Approve(officerId);
            if (stage == 2) continue;

            rfq.Publish();
            published.Add(rfq.Id);
        }
        await db.SaveChangesAsync();

        // The window has to be in the past before it can legally open, which is the same move the
        // curated fixture makes and the same one the integration suite documents.
        await db.Rfqs.Where(r => published.Contains(r.Id))
            .ExecuteUpdateAsync(p => p.SetProperty(r => r.SubmissionOpensAt, DateTimeOffset.UtcNow.AddHours(-1)));
        db.ChangeTracker.Clear();

        var toOpen = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations)
            .Where(r => published.Contains(r.Id)).ToListAsync();
        foreach (var rfq in toOpen) rfq.OpenSubmissionWindow();
        await db.SaveChangesAsync();

        // Bids. Every open tender gets several, from the suppliers it invited, so the received-bids list
        // has rows to compare and the tab strip's count means something. One bid per supplier per tender:
        // the unique index over (RfqId, SupplierId) for live states says so, and it is right to.
        var proposalNumber = 101;
        var bids = new List<Proposal>();
        foreach (var rfq in toOpen)
        {
            var invitedIds = rfq.Invitations.Select(i => i.SupplierId).ToList();
            foreach (var supplierId in invitedIds.Take(2 + proposalNumber % 4))
            {
                bids.Add(Proposal.Create($"PRP-DEMO-{proposalNumber++:0000}", rfq.Id, supplierId));
            }
        }
        db.Proposals.AddRange(bids);
        await db.SaveChangesAsync();

        // Most submitted, a few left in draft - a bidder who started and has not finished is a state the
        // buyer's screens have to render and the supplier's own list has to explain.
        var submittedIds = bids.Where((_, i) => i % 5 != 0).Select(b => b.Id).ToList();
        await db.Proposals.Where(p => submittedIds.Contains(p.Id))
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.State, ProposalState.Submitted)
                                      .SetProperty(x => x.SubmittedAt, DateTimeOffset.UtcNow.AddHours(-6)));

        // Half the open tenders close, so the pipeline is not entirely front-loaded and the evaluation
        // side of the product has tenders to work on. No winner is chosen here, for the same reason the
        // curated fixture chooses none: an award is a verdict, and a fixture has no standing to reach one.
        db.ChangeTracker.Clear();
        var closing = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations)
            .Where(r => published.Contains(r.Id)).ToListAsync();
        for (var i = 0; i < closing.Count; i += 2)
        {
            closing[i].CloseSubmissionWindow(reason: null, isEarlyClose: false);
            closing[i].OpenEvaluation();
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Tenders carried all the way to Awarded, in the bulk range only.
    ///
    /// <para><b>Why this exists, and why it stops where it does.</b> The curated fixture deliberately
    /// leaves its one award at Recommended - "an award is a verdict, and a fixture has no standing to
    /// reach one" - and that is still true of the tender the walkthrough follows. It is not true of the
    /// bulk range, which exists precisely so the screens have something to render: with no award
    /// anywhere in the database, the Ministry's three award charts drew "no figures available to chart",
    /// the awarded-value column was empty on every row, and the ranked bars nobody had ever seen could
    /// not be seen. A demonstration of a procurement portal in which nothing has ever been procured
    /// shows the pipeline and hides the point of it.</para>
    ///
    /// <para>So the curated award stays in flight, the manager's approval queue keeps its row, and these
    /// are separate: RFQ-DEMO-01xx only.</para>
    ///
    /// <para><b>The dates are moved backwards afterwards.</b> Every award created in one run carries one
    /// timestamp, and a spend-by-month chart of a single month is a chart of one bar. `CreatedAt` is
    /// init-only and `AwardedAt` is set by the aggregate, correctly - neither is something a caller
    /// should be able to choose - so they are rewritten in storage the same way the submission window
    /// is, which is the move this seeder and the integration suite already make for the same reason.</para>
    /// </summary>

    /// <summary>Subjects for the tenders that have already been decided.</summary>
    private static readonly (string Ar, string En)[] HistoricTenders =
    [
        ("توريد حافلات المدينة", "City bus supply"), ("صيانة جسر النهر", "River bridge maintenance"),
        ("خدمات الإطعام للمقر", "Headquarters catering"), ("تأمين إقامة البعثات", "Mission accommodation"),
        ("تنظيم معرض النقل", "Transport expo"), ("رحلات الوفود الرسمية", "Official delegation tours"),
        ("توريد زيوت المحركات", "Engine oil supply"), ("تجديد أرصفة المحطة", "Station platform renewal"),
        ("خدمات الغسيل للأسطول", "Fleet laundry services"), ("توريد أجهزة اللاسلكي", "Radio equipment supply"),
        ("صيانة أنظمة الإنذار", "Alarm system maintenance"), ("خدمات النقل المدرسي", "School transport services"),
        ("توريد مواد التنظيف", "Cleaning consumables"), ("تدريب سائقي الحافلات", "Bus driver training"),
    ];

    /// <summary>
    /// A procurement history: tenders that were run, decided and closed, spread over the past ten months.
    ///
    /// <para><b>Why a separate range rather than more of the bulk one.</b> The bulk tenders are a
    /// PIPELINE - four drafts, four in review, four approved, three open, three under evaluation - and
    /// those proportions are a deliberate statement about what a working queue looks like. Pushing more
    /// of them to Awarded to fill a chart would break the thing they were built to show. These are
    /// RFQ-DEMO-02xx, they are all finished, and they exist for the screens that read backwards:
    /// award analytics, spend by month, spend by category, the coverage report's awarded column.</para>
    ///
    /// <para><b>Ten months, fourteen tenders, six categories.</b> A chart of one month is a chart of one
    /// bar, and a ranked chart of two categories has nothing to rank. The months are uneven because a
    /// procurement year is uneven.</para>
    ///
    /// <para>One buying body, because this demonstration has one. The Ministry's "by buying body" chart
    /// is therefore a single row - which is the truth of this tenancy rather than a gap: a second
    /// organisation would hide its own tenders from the officer, whose every list is scoped to the
    /// organisation they belong to, and would strand the owner-eligibility check on transitions.</para>
    /// </summary>
    private static async Task SeedHistoricAwardsAsync(AppDbContext db, Guid organizationId)
    {
        if (await db.Rfqs.AnyAsync(r => r.ReferenceCode == "RFQ-DEMO-0201")) return;

        var officerId = await db.Users.Where(u => u.Email == "officer@mots.local").Select(u => u.Id).FirstAsync();
        var approverId = await db.Users.Where(u => u.Email == "manager@mots.local").Select(u => u.Id).FirstAsync();
        var template = await db.EvaluationTemplates.Include(t => t.Criteria).FirstAsync();
        var snapshot = System.Text.Json.JsonSerializer.Serialize(template.Criteria.Select(c => new
        {
            c.NameAr, c.NameEn, Dimension = c.Dimension.ToString(), c.Weight, c.MaxScore, c.Threshold,
            ScoringType = c.ScoringType.ToString(), c.RequiresJustification,
        }));

        // Only suppliers who could really have been invited - approved, and able to trade.
        var invitable = await db.Suppliers
            .Where(s => s.OnboardingState == SupplierOnboardingState.Approved
                        && s.LifecycleState == SupplierLifecycleState.Active)
            .OrderBy(s => s.ReferenceCode)
            .Select(s => s.Id)
            .ToListAsync();
        if (invitable.Count == 0) return;

        var created = new List<Rfq>();
        for (var i = 0; i < HistoricTenders.Length; i++)
        {
            var (titleAr, titleEn) = HistoricTenders[i];
            var rfq = Rfq.Create($"RFQ-DEMO-{201 + i:0000}", organizationId, titleAr, titleEn, null, null, "SYP",
                publishAt: null,
                submissionOpensAt: DateTimeOffset.UtcNow.AddHours(1),
                submissionClosesAt: DateTimeOffset.UtcNow.AddDays(2),
                clarificationDeadlineAt: null, evaluationTargetDate: null, ownerUserId: officerId);
            // Two lines on every third one, so an award can touch two categories and the note under the
            // by-category chart - "an award counts once per category its tender touched" - describes
            // something that actually happens in this data.
            rfq.AddItem(titleAr, titleEn, null, null, Categories[i % Categories.Length],
                20m + (i * 13 % 180), "unit", isUnitPrice: true, isOptional: false);
            if (i % 3 == 0)
            {
                rfq.AddItem(titleAr, titleEn, null, null, Categories[(i + 2) % Categories.Length],
                    15m + (i * 7 % 120), "unit", isUnitPrice: true, isOptional: false);
            }
            rfq.BindEvaluationTemplate(template.Id, 1, snapshot);
            foreach (var supplierId in invitable.Skip(i % 3).Take(3)) rfq.InviteSupplier(supplierId);
            rfq.SubmitForReview();
            db.RfqApprovals.Add(rfq.Approvals.Single(a => a.Decision is null));
            rfq.Approve(officerId);
            rfq.Publish();
            db.Rfqs.Add(rfq);
            created.Add(rfq);
        }
        await db.SaveChangesAsync();

        // The window has to be in the past before it can legally open - the same move the bulk tenders
        // make, and the same one the integration suite documents.
        var ids = created.Select(r => r.Id).ToList();
        await db.Rfqs.Where(r => ids.Contains(r.Id))
            .ExecuteUpdateAsync(p => p.SetProperty(r => r.SubmissionOpensAt, DateTimeOffset.UtcNow.AddHours(-1)));
        db.ChangeTracker.Clear();

        var tenders = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations).Include(r => r.Items)
            .Where(r => ids.Contains(r.Id)).OrderBy(r => r.ReferenceCode).ToListAsync();
        foreach (var rfq in tenders) rfq.OpenSubmissionWindow();
        await db.SaveChangesAsync();

        // Bids, priced. A bid with no lines is worth nothing everywhere the product asks what a tender
        // cost, which is the whole reason these tenders exist.
        var proposalNumber = 201;
        var bids = new List<Proposal>();
        foreach (var rfq in tenders)
        {
            foreach (var supplierId in rfq.Invitations.Select(inv => inv.SupplierId))
            {
                var bid = Proposal.Create($"PRP-DEMO-{proposalNumber:0000}", rfq.Id, supplierId);
                foreach (var line in rfq.Items)
                {
                    bid.SetItemPricing(line.Id, line.Quantity, 140m + ((proposalNumber * 29) % 320),
                        discount: null, leadTimeDays: 5 + (proposalNumber % 25), notesAr: null, notesEn: null);
                }
                bids.Add(bid);
                proposalNumber++;
            }
        }
        db.Proposals.AddRange(bids);
        await db.SaveChangesAsync();

        var bidIds = bids.Select(b => b.Id).ToList();
        await db.Proposals.Where(p => bidIds.Contains(p.Id))
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.State, ProposalState.Submitted)
                                      .SetProperty(x => x.SubmittedAt, DateTimeOffset.UtcNow.AddDays(-1)));
        db.ChangeTracker.Clear();

        // Closed, evaluated, decided. The winner is the cheapest bid on the tender, which is a rule this
        // fixture can defend: it is not a judgement about quality, it is the only ordering the data
        // carries.
        var totals = await db.ProposalItems.AsNoTracking()
            .Where(i => bidIds.Contains(i.ProposalId))
            .Select(i => new { i.ProposalId, i.Quantity, i.UnitPrice })
            .ToListAsync();
        var totalByProposal = totals.GroupBy(t => t.ProposalId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Quantity * t.UnitPrice));
        var bidsByRfq = bids.GroupBy(b => b.RfqId).ToDictionary(g => g.Key, g => g.ToList());

        var decided = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations)
            .Where(r => ids.Contains(r.Id)).OrderBy(r => r.ReferenceCode).ToListAsync();
        var awarded = new List<(Guid AwardId, int Index)>();
        for (var i = 0; i < decided.Count; i++)
        {
            var rfq = decided[i];
            if (!bidsByRfq.TryGetValue(rfq.Id, out var candidates) || candidates.Count == 0) continue;
            var winner = candidates.OrderBy(b => totalByProposal.GetValueOrDefault(b.Id)).First();

            rfq.CloseSubmissionWindow(reason: null, isEarlyClose: false);
            rfq.OpenEvaluation();
            rfq.BeginShortlisting();
            rfq.RecordRecommendation();
            rfq.EnterAwardApproval();
            rfq.MarkAwarded();

            var award = Domain.Awards.Award.Recommend(
                rfq.Id, winner.Id,
                "أفضل عرض من حيث السعر والمواصفات الفنية.",
                "Best combination of price and technical specification.",
                officerId);
            award.RouteForApproval();
            award.Approve(approverId);
            award.ExecuteAward(snapshot);
            db.Awards.Add(award);
            awarded.Add((award.Id, i));
        }
        await db.SaveChangesAsync();

        foreach (var (awardId, position) in awarded)
        {
            // Ten months, unevenly filled: two of the months carry three awards and one carries none,
            // because a spend chart whose bars are all the same height teaches nothing about reading one.
            var monthsAgo = position % 10;
            var when = DateTimeOffset.UtcNow.AddMonths(-monthsAgo).AddDays(-(position * 5 % 27));
            await db.Awards.Where(a => a.Id == awardId)
                .ExecuteUpdateAsync(p => p.SetProperty(a => a.CreatedAt, when)
                                          .SetProperty(a => a.AwardedAt, when));
        }
    }

    private static async Task SeedVolumeAwardsAsync(AppDbContext db)
    {
        // Self-gating rather than gated by the caller, because this runs on databases that already
        // exist. SeedVolumeAsync returns early the moment its first supplier is found, which is right
        // for what it does and useless for adding something that was never there.
        if (await db.Awards.AnyAsync(a => a.State == Domain.Awards.AwardState.Awarded)) return;

        var officerId = await db.Users.Where(u => u.Email == "officer@mots.local").Select(u => u.Id).FirstAsync();
        var template = await db.EvaluationTemplates.Include(t => t.Criteria).FirstAsync();
        var snapshot = System.Text.Json.JsonSerializer.Serialize(template.Criteria.Select(c => new
        {
            c.NameAr, c.NameEn, Dimension = c.Dimension.ToString(), c.Weight, c.MaxScore, c.Threshold,
            ScoringType = c.ScoringType.ToString(), c.RequiresJustification,
        }));

        // Segregation of duties: §6.1 refuses an approver who is also the recommender, and the second
        // manager exists for exactly this. Recommending as the officer and approving as a manager is
        // also what actually happens.
        var approverId = await db.Users.Where(u => u.Email == "manager@mots.local").Select(u => u.Id).FirstAsync();

        db.ChangeTracker.Clear();
        var evaluating = await db.Rfqs
            .Where(r => r.ReferenceCode.StartsWith("RFQ-DEMO-01") && r.State == RfqState.UnderEvaluation)
            .OrderBy(r => r.ReferenceCode)
            .ToListAsync();

        var submittedByRfq = await db.Proposals.AsNoTracking()
            .Where(p => p.State == ProposalState.Submitted)
            .Select(p => new { p.Id, p.RfqId })
            .ToListAsync();
        var winners = submittedByRfq.GroupBy(p => p.RfqId).ToDictionary(g => g.Key, g => g.First().Id);

        var awarded = new List<(Guid AwardId, int Index)>();
        var index = 0;
        foreach (var rfq in evaluating)
        {
            // A tender with no submitted bid has no winner to name, and inventing one would be the
            // fixture asserting something the product refuses.
            if (!winners.TryGetValue(rfq.Id, out var winningProposalId)) continue;

            rfq.BeginShortlisting();
            rfq.RecordRecommendation();
            rfq.EnterAwardApproval();
            rfq.MarkAwarded();

            var award = Domain.Awards.Award.Recommend(
                rfq.Id, winningProposalId,
                "أفضل عرض من حيث السعر والمواصفات الفنية.",
                "Best combination of price and technical specification.",
                officerId);
            award.RouteForApproval();
            award.Approve(approverId);
            award.ExecuteAward(snapshot);
            db.Awards.Add(award);
            awarded.Add((award.Id, index++));
        }
        await db.SaveChangesAsync();

        // Spread backwards over eight months. The analytics group by month, so this is what gives the
        // spend chart an axis rather than a single column - and the months are uneven on purpose,
        // because a procurement year is.
        foreach (var (awardId, position) in awarded)
        {
            var monthsAgo = position % 8;
            var when = DateTimeOffset.UtcNow.AddMonths(-monthsAgo).AddDays(-(position * 3 % 25));
            await db.Awards.Where(a => a.Id == awardId)
                .ExecuteUpdateAsync(p => p.SetProperty(a => a.CreatedAt, when)
                                          .SetProperty(a => a.AwardedAt, when));
        }
    }

    private static Rfq NewRfq(AppDbContext db, string code, Guid organizationId, string titleAr, string titleEn,
        Guid ownerUserId, Guid templateId, string snapshot)
    {
        var rfq = Rfq.Create(code, organizationId, titleAr, titleEn, null, null, "SYP",
            publishAt: null,
            // Future, because SubmitForReview refuses a window that has already opened - correctly:
            // an RFQ whose submissions opened before anyone approved it is not a reviewable draft.
            // The two that need an OPEN window have it moved back in storage after the transitions,
            // which is what SubmissionWindowTestHelper does in the integration suite.
            submissionOpensAt: DateTimeOffset.UtcNow.AddHours(1),
            submissionClosesAt: DateTimeOffset.UtcNow.AddDays(14),
            clarificationDeadlineAt: null, evaluationTargetDate: null, ownerUserId: ownerUserId);
        rfq.AddItem(titleAr, titleEn, null, null, "catering", 10m, "unit", isUnitPrice: true, isOptional: false);
        rfq.BindEvaluationTemplate(templateId, 1, snapshot);
        rfq.InviteSupplier(db.Suppliers.First(s => s.ReferenceCode == "SUP-DEMO-0001").Id);
        db.Rfqs.Add(rfq);
        return rfq;
    }

    private static async Task SeedUsersAsync(UserManager<AppUser> userManager, string password, Guid organizationId, Guid demoSupplierId)
    {
        foreach (var (email, fullName, role, isSupplierUser) in Personas)
        {
            if (await userManager.FindByEmailAsync(email) is not null) continue;

            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true,
                IsActive = true,
                // Buyer-side row-scoping keys on OrganizationId and supplier-side on SupplierId.
                // A staff account with neither can sign in and then see an empty product, which is
                // the failure mode this seeder exists to prevent.
                SupplierId = isSupplierUser ? demoSupplierId : null,
                // ministry_viewer gets NO organization: BRULE-086 grants the Ministry
                // cross-organization aggregate access, and pinning it to one buying body would be a
                // narrower grant wearing the same name. The consequence is real and handled rather
                // than hidden - the org-scoped procurement report answers 404 for this persona
                // (§9.2), and SCR-605 says so instead of rendering a broken panel.
                OrganizationId = isSupplierUser || role == Roles.MinistryViewer ? null : organizationId,
            };

            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not seed the demo {role} user: " +
                    string.Join(", ", created.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(user, role);
        }
    }
}
