using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
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
    public const string Password = "motsdemo2026";

    private const string OrganizationNameEn = "MOT Procurement Body";

    /// <summary>email, full name, role, and whether the account belongs to the demo supplier.</summary>
    private static readonly (string Email, string FullName, string Role, bool IsSupplierUser)[] Personas =
    [
        ("officer@mots.local", "Demo Procurement Officer", Roles.ProcurementOfficer, false),
        ("manager@mots.local", "Demo Procurement Manager", Roles.ProcurementManager, false),
        ("evaluator@mots.local", "Demo Evaluator", Roles.Evaluator, false),
        ("ministry@mots.local", "Demo Ministry Viewer", Roles.MinistryViewer, false),   // organization deliberately null - see below
        ("supplier@mots.local", "Demo Supplier Admin", Roles.SupplierAdmin, true),
        ("supplier.user@mots.local", "Demo Supplier User", Roles.SupplierUser, true),
    ];

    public static async Task SeedAsync(AppDbContext db, UserManager<AppUser> userManager, IConfiguration configuration)
    {
        var password = configuration["DevSeed:DemoPassword"] ?? Password;

        var organizationId = await SeedOrganizationAsync(db);
        var demoSupplierId = await SeedSuppliersAsync(db);
        await SeedUsersAsync(userManager, password, organizationId, demoSupplierId);
        await SeedTendersAsync(db, organizationId, demoSupplierId);
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
        closed = await db.Rfqs.Include(r => r.Approvals).Include(r => r.Invitations).FirstAsync(r => r.Id == closed.Id);
        clarifying = await db.Rfqs.Include(r => r.Approvals).Include(r => r.Invitations).FirstAsync(r => r.Id == clarifying.Id);
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
        closed = await db.Rfqs.Include(r => r.Approvals).Include(r => r.Invitations).FirstAsync(r => r.Id == closed.Id);
        clarifying = await db.Rfqs.Include(r => r.Approvals).Include(r => r.Invitations).FirstAsync(r => r.Id == clarifying.Id);

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
