// The development fixture: one account per persona, and enough data that no screen is empty for want of a row.
//
//
// WHY IT EXISTS
//
// The other two seeders create two personas out of eight, and nothing seeded domain data at all, so five of
// the six dashboards rendered as zeroes on a fresh database and could not be verified by looking at them.
//
// An empty dashboard proves nothing. A widget with a broken query and a widget with no rows look identical.
//
//
// DEVELOPMENT ONLY, AND THE REFUSAL IS IN HERE
//
// This creates accounts whose password is a constant published in the runbook. A seeded login nobody can use
// is not a seeded login, so the password is documented; it is not a secret in the sense the rule means,
// because it is only ever read from a method that refuses to run outside development.
//
// The caller already guards on the environment, and that was the whole protection. A second call added
// anywhere, or that block being widened, would silently seed known credentials into whatever environment is
// running. The dangerous knowledge lives in this file, so the refusal belongs here too, and it costs one
// parameter.
//
// Every step is idempotent: each checks for its own output first, so restarting the API duplicates nothing.
//
//
// STATES ARE FORCED IN STORAGE WHERE THE JOURNEY IS NOT THE POINT
//
// Driving a supplier to approved through the domain needs a complete profile, documents uploaded and scanned,
// and a reviewer's decision. That is what the end-to-end walk is for.
//
// Here the states are set directly, which is the same forced-transition pattern the integration suite already
// uses and documents. The domain is still the authority on transitions; this is a fixture, and it says so.
//
// Writing behind the change tracker has a consequence that has to be handled rather than hoped about: a
// tracked entity then holds a stale version and the next save loses to the application-managed concurrency
// guard. So entities are reloaded rather than reused, and an aggregate is saved once rather than mutated and
// saved twice.
//
//
// IT INVENTS NO VERDICT ON THE TENDER THE WALKTHROUGH FOLLOWS
//
// The curated evaluation is opened, assigned and part-scored. It is not consolidated, and the curated award
// stops at recommended and is not even routed for approval.
//
// The line matters. Recommending is a step somebody took and can be seen to have taken: it names a bid and a
// justification and waits for a manager. Consolidating ranks bids and approving names a winner, and both are
// outcomes a fixture has no standing to reach. Executing one would also emit the integration request.
//
// So the manager's approval queue has a real row to work, and no tender the walkthrough touches has a winner.
//
//
// THREE RANGES, AND WHY THEY ARE SEPARATE
//
// The CURATED rows are one supplier at each lifecycle position, one tender at each state, one bid in each
// shape. They are what the walkthrough follows and what the dashboards need in order to render anything.
//
// They are not what the product looks like in use. Six tenders fit on one screen with no scrollbar, every
// list is one page, no filter narrows anything, no ranking has a second place, and a table whose column would
// collapse under real width never gets the chance. A demonstration on that data shows the screens working and
// says nothing about whether they hold.
//
// So the BULK range adds suppliers and a live pipeline in quantity, and the HISTORY range adds tenders that
// were run, decided and closed over the past ten months. Their codes are separate ranges, so nothing in them
// can be mistaken for a curated row and a reader can tell at a glance which is which.
//
// The bulk range stays a PIPELINE, with drafts, reviews, approvals, open and closed tenders in the
// proportions a working queue carries. Pushing more of it to awarded to fill a chart would break the thing it
// was built to show, so the finished tenders are their own range.
//
// Without them the ministry's award charts drew "no figures available", the awarded-value column was empty on
// every row, and the ranked bars nobody had ever seen could not be seen. A demonstration of a procurement
// portal in which nothing has ever been procured shows the pipeline and hides the point of it.
//
// Lifecycle positions and states are assigned by index rather than chosen one at a time, so the proportions
// are visible in the code rather than something a reader has to count in the database.
//
//
// THE FIGURES ARE SPREAD ON PURPOSE
//
// Bid prices sit either side of a notional list price, so no two bids on one tender are equal, the cheapest is
// not always the first one invited, and a comparison has an order.
//
// Awards are spread backwards over ten uneven months, because a chart of one month is a chart of one bar and a
// chart whose bars are all the same height teaches nothing about reading one. The creation and award
// timestamps are init-only and domain-set, correctly, so they are rewritten in storage the same way the
// submission windows are.
//
// Every third tender carries two lines, so an award can touch two categories and the note under the
// by-category chart, that an award counts once per category its tender touched, describes something that
// actually happens in this data.
//
// Closing dates are staggered, so "closes in six days" differs from "closes in three weeks" and a list sorted
// by closing date has an order worth sorting.
//
//
// A BID WITH NO PRICED LINES IS WORTH NOTHING, EVERYWHERE
//
// A bid's total is derived from its lines and never stored, which is the domain's own invariant. So bids
// created without lines were zero on the comparison screen, zero in the awarded-value column and zero in the
// ministry's spend analytics. The screens were not wrong; there was nothing to show.
//
// Pricing them works WITH the draft-only edit rule rather than around it: the state flag is moved in storage,
// the aggregate does the pricing under its own guards, and the flag goes back. Moving that flag is how these
// bids were submitted in the first place, since the fixture has never had a supplier session to submit them
// from.
//
// That runs in a transaction, because the middle of it leaves the fixture WORSE than it found it: the bids are
// drafts between the two flips, and a failure in between would leave submitted bids sitting as drafts with
// their submission timestamps still on them. Not hypothetical; the first run of it did exactly that.
//
// The priced lines are added to the tracked set explicitly, which is what the production handler does on the
// line after its own call and for the same reason. A line carries a key it assigns itself, so a child
// discovered inside a tracked parent's collection is taken for an existing row. The first draft of this
// omitted it and the seeder emitted updates against rows that had never been inserted.
//
//
// THINGS FOUND BY WALKING THE PRODUCT, EACH FIXED HERE
//
// A SECOND manager, which is not padding. The written rule refuses an approver who is also the recommender,
// correctly, and with one manager seeded the award-approval step could not be reached at all: whoever
// recommended could neither approve nor reject, and no other account held the permission. Found by walking a
// tender to the approval gate and being unable to pass it in either direction.
//
// The queue-arrival audit rows. The counters said one submitted and one under review and the wait-time widget
// said "no open applications", and both were right: that widget reads the AUDIT LOG, because "how long has
// this been waiting" is a question about when the application entered the queue and the state column cannot
// answer it. Forcing a state in storage writes no audit row, so the fixture had a supplier in the queue with
// no record of arriving. These rows are not invented history; they say what the seeder actually did, backdated
// so the widget has a number and the ordering is visible. A queue where everything arrived this second is a
// queue nobody can prioritise.
//
// The criteria snapshot in the SHAPE THE HANDLER READS. The first version wrote a plausible-looking object
// that the handler cannot parse, so opening evaluation on any seeded tender failed with a serialisation error.
// Nothing caught it, because the seeded evaluation was built by constructing the aggregate directly rather
// than going through the handler: the fixture had quietly bypassed the code path it was meant to set up. It is
// now built by the same projection the production binding makes, so the fixture and production write one
// shape.
//
// The bid awaiting clarification needs its OWN tender rather than a second bid on an existing one, because the
// unique index over the tender and supplier for live states means one supplier gets one live bid per tender.
// Found by that constraint refusing the first version, which is the index doing its job.
//
//
// SMALL RULES THE FIXTURE HAS TO RESPECT
//
// A window has to be in the past before it can legally open, and a tender sent for review must have a window
// that has not opened yet, because a tender whose submissions opened before anyone approved it is not a
// reviewable draft. So the windows are moved after the transitions rather than chosen to suit them, which is
// what the integration suite's own helper does.
//
// Only suppliers a buyer could really invite are invited. Inviting a half-finished application would be a
// fixture asserting something the product refuses. Likewise a tender with no submitted bid has no winner to
// name.
//
// Scoring is refused from the assigned state, correctly, because an evaluator who has not opened the workspace
// has not seen the bidder list and the conflict declaration sits on that opening. So the fixture opens it.
//
// The winner of a historical tender is the cheapest bid on it, which is a rule this fixture can defend: it is
// not a judgement about quality, it is the only ordering the data carries. Recommending as the officer and
// approving as a manager is both what the segregation rule requires and what actually happens.
//
// The clarification tender is deliberately not moved to evaluation. A clarification is asked of a bid under
// review, and leaving a window open while one of its bids is being questioned would be a state the process
// does not produce.
//
//
// THE ORGANIZATION, AND THE ONE PERSONA WITHOUT ONE
//
// The buying body's identifier is read back rather than fixed, because the factory assigns its own and
// rewriting a primary key afterwards would leave every row that points at it pointing at nothing. It is
// looked up by name on re-runs, so the value is stable across restarts without being hard-coded.
//
// There is one buying body, because this demonstration has one. The ministry's by-buying-body chart is
// therefore a single row, which is the truth of this tenancy rather than a gap: a second organization would
// hide its own tenders from the officer, whose every list is scoped to the organization they belong to, and
// would strand the owner-eligibility check on transitions.
//
// Buyer-side scoping keys on the organization and supplier-side on the company, so a staff account with
// neither can sign in and then see an empty product, which is the failure this seeder exists to prevent.
//
// The ministry viewer is the deliberate exception and gets no organization at all. The written rule grants the
// ministry cross-organization aggregate access, and pinning it to one buying body would be a narrower grant
// wearing the same name. The consequence is real and handled rather than hidden: the organization-scoped
// report answers not-found for that persona, and the screen says so instead of rendering a broken panel.
//
//
// THE COMMERCIAL-VISIBILITY SWITCH LIVES HERE, NOT IN A MIGRATION
//
// It was a migration first, and that was wrong. A migration runs in every environment, so the same deployment
// step that creates the schema in production would have switched the disclosure on there too.
//
// The approval it rests on is explicitly bounded to demonstration data, where there are no real bidders and no
// real bid values, and a mechanism that ignores the boundary makes the approval mean something it does not
// say.
//
// So the switch sits behind the same gate the demonstration data does, in a seeder that refuses to run outside
// development. Everywhere else the flag keeps its seeded value, which is off, and turning it on there requires
// the written sign-off the decision names: a person, a date and the scope, recorded before the row changes.
//
// The step that fills the history range gates itself rather than relying on the caller, because it runs on
// databases that already exist. The bulk step returns early the moment its first supplier is found, which is
// right for what it does and useless for adding something that was never there.

namespace MotsSupplierPortal.Infrastructure.Identity;

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

public static class DevDataSeeder
{
    public const string Password = "motsdemo2026"; // NOSONAR S2068 - see above: dev-only seed data, guarded by environment.

    private const string OrganizationNameEn = "MOT Procurement Body";

    private static readonly (string Email, string FullName, string Role, bool IsSupplierUser)[] Personas =
    [
        ("officer@mots.local", "Demo Procurement Officer", Roles.ProcurementOfficer, false),
        ("manager@mots.local", "Demo Procurement Manager", Roles.ProcurementManager, false),
        ("manager2@mots.local", "Demo Procurement Manager (Second)", Roles.ProcurementManager, false),
        ("evaluator@mots.local", "Demo Evaluator", Roles.Evaluator, false),
        ("ministry@mots.local", "Demo Ministry Viewer", Roles.MinistryViewer, false),   // organization deliberately null - see below
        ("supplier@mots.local", "Demo Supplier Admin", Roles.SupplierAdmin, true),
        ("supplier.user@mots.local", "Demo Supplier User", Roles.SupplierUser, true),
    ];

    public static async Task SeedAsync(
        AppDbContext db, UserManager<AppUser> userManager, IConfiguration configuration, IHostEnvironment environment)
    {
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

        await SeedQueueEntryAuditAsync(db, submitted.Id, "SUP-DEMO-0002", daysAgo: 9);
        await SeedQueueEntryAuditAsync(db, underReview.Id, "SUP-DEMO-0003", daysAgo: 3);

        return approved.Id;
    }

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

        supplier.MarkEmailVerified();
        supplier.UpdateCoreProfile($"{nameEn} - demo fixture", "https://example.com", null, "SYP");
        supplier.AddAddress(AddressKind.HeadOffice, "1 Demo Street", null, "Damascus", "DIM", "SY", null, 33.5131, 36.2925);
        supplier.LinkCategory("catering", isComplianceCritical: false);
        supplier.AcceptTerms("v1");

        db.Suppliers.Add(supplier);
        return supplier;
    }

    private static Task SetStateAsync(AppDbContext db, Guid id, SupplierOnboardingState onboarding, SupplierLifecycleState lifecycle) =>
        db.Suppliers.Where(s => s.Id == id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, onboarding)
            .SetProperty(s => s.LifecycleState, lifecycle));

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

        var draft = NewRfq(db, "RFQ-DEMO-0001", organizationId, "توريد مواد غذائية", "Catering supplies", officerId, template.Id, snapshot);
        var inReview = NewRfq(db, "RFQ-DEMO-0002", organizationId, "خدمات نظافة", "Cleaning services", officerId, template.Id, snapshot);
        var approved = NewRfq(db, "RFQ-DEMO-0003", organizationId, "أثاث مكتبي", "Office furniture", officerId, template.Id, snapshot);
        var open = NewRfq(db, "RFQ-DEMO-0004", organizationId, "تجهيزات مطابخ", "Kitchen equipment", officerId, template.Id, snapshot);
        var closed = NewRfq(db, "RFQ-DEMO-0005", organizationId, "صيانة مصاعد", "Lift maintenance", officerId, template.Id, snapshot);
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

        var drafted = Proposal.Create("PRP-DEMO-0001", open.Id, demoSupplierId);
        var submitted = Proposal.Create("PRP-DEMO-0002", closed.Id, demoSupplierId);
        var underClarification = Proposal.Create("PRP-DEMO-0003", clarifying.Id, demoSupplierId);
        db.Proposals.AddRange(drafted, submitted, underClarification);
        await db.SaveChangesAsync();
        await db.Proposals.Where(p => p.Id == submitted.Id || p.Id == underClarification.Id)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.State, ProposalState.Submitted)
                                      .SetProperty(x => x.SubmittedAt, DateTimeOffset.UtcNow.AddHours(-2)));

        db.ChangeTracker.Clear();
        var toClarify = await db.Proposals.FirstAsync(p => p.Id == underClarification.Id);
        toClarify.OpenForReview();
        toClarify.RequestClarification(
            "Please confirm whether the quoted lead time includes customs clearance, and restate the "
            + "warranty period in months.");
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        closed = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations).FirstAsync(r => r.Id == closed.Id);
        clarifying = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations).FirstAsync(r => r.Id == clarifying.Id);

        closed.CloseSubmissionWindow(reason: null, isEarlyClose: false);
        closed.OpenEvaluation();

        clarifying.CloseSubmissionWindow(reason: null, isEarlyClose: false);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var evaluation = Domain.Evaluation.Evaluation.Create(closed.Id, [
            new CriterionSnapshotInput(technical.NameAr, technical.NameEn, CriterionDimension.Technical, 60m, 10m, null, ScoringType.Numeric),
            new CriterionSnapshotInput(commercial.NameAr, commercial.NameEn, CriterionDimension.Commercial, 40m, 10m, null, ScoringType.Numeric),
        ]);
        evaluation.AssignEvaluators([evaluatorId]);

        evaluation.OpenScoring(evaluatorId);

        var firstCriterion = evaluation.Criteria.First(c => c.Dimension == CriterionDimension.Technical);
        evaluation.ScoreCriterion(evaluatorId, submitted.Id, firstCriterion.Id, 8m, null, null, new HashSet<Guid> { submitted.Id });

        db.Evaluations.Add(evaluation);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var award = Domain.Awards.Award.Recommend(
            closed.Id, submitted.Id,
            "أفضل عرض من حيث السعر والمواصفات الفنية.",
            "Best combination of price and technical specification.",
            officerId);
        db.Awards.Add(award);
        await db.SaveChangesAsync();
    }

    private static readonly string[] Categories =
        ["catering", "transport", "maintenance", "accommodation", "events", "tour_operations"];

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

    private static async Task SeedVolumeAsync(AppDbContext db, Guid organizationId)
    {
        if (await db.Suppliers.AnyAsync(s => s.ReferenceCode == "SUP-DEMO-0101")) return;

        var suppliers = new List<Supplier>();
        for (var i = 0; i < BulkCompanies.Length; i++)
        {
            var (nameAr, nameEn) = BulkCompanies[i];
            var code = $"SUP-DEMO-{101 + i:0000}";
            var supplier = Register(db, code, nameAr, nameEn, $"RC-DEMO-{101 + i:0000}");
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

            if (onboarding is SupplierOnboardingState.Submitted or SupplierOnboardingState.UnderReview)
            {
                await SeedQueueEntryAuditAsync(db, suppliers[i].Id, suppliers[i].ReferenceCode, daysAgo: 1 + (i * 3) % 42);
            }
        }

        await SeedVolumeTendersAsync(db, organizationId, suppliers);
    }

    private static async Task SeedVolumePricingAsync(AppDbContext db)
    {
        var unpriced = await db.Proposals.AsNoTracking()
            .Where(p => p.ReferenceCode.StartsWith("PRP-DEMO-01") && !db.ProposalItems.Any(i => i.ProposalId == p.Id))
            .Select(p => new { p.Id, p.State })
            .ToListAsync();
        if (unpriced.Count == 0) return;

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
                proposals[i].SetItemPricing(line.Id, line.Quantity, 120m + ((i * 37) % 260),
                    discount: null, leadTimeDays: 7 + (i % 21), notesAr: null, notesEn: null);

                db.ProposalItems.Add(proposals[i].Items.First(item => item.RfqItemId == line.Id));
            }
        }
        await db.SaveChangesAsync();

        await db.Proposals.Where(p => submitted.Contains(p.Id))
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.State, ProposalState.Submitted));

        await transaction.CommitAsync();
        db.ChangeTracker.Clear();
    }

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

    private static async Task SeedVolumeTendersAsync(AppDbContext db, Guid organizationId, List<Supplier> suppliers)
    {
        var officerId = await db.Users.Where(u => u.Email == "officer@mots.local").Select(u => u.Id).FirstAsync();
        var template = await db.EvaluationTemplates.Include(t => t.Criteria).FirstAsync();
        var snapshot = System.Text.Json.JsonSerializer.Serialize(template.Criteria.Select(c => new
        {
            c.NameAr, c.NameEn, Dimension = c.Dimension.ToString(), c.Weight, c.MaxScore, c.Threshold,
            ScoringType = c.ScoringType.ToString(), c.RequiresJustification,
        }));

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

        await db.Rfqs.Where(r => published.Contains(r.Id))
            .ExecuteUpdateAsync(p => p.SetProperty(r => r.SubmissionOpensAt, DateTimeOffset.UtcNow.AddHours(-1)));
        db.ChangeTracker.Clear();

        var toOpen = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations)
            .Where(r => published.Contains(r.Id)).ToListAsync();
        foreach (var rfq in toOpen) rfq.OpenSubmissionWindow();
        await db.SaveChangesAsync();

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

        var submittedIds = bids.Where((_, i) => i % 5 != 0).Select(b => b.Id).ToList();
        await db.Proposals.Where(p => submittedIds.Contains(p.Id))
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.State, ProposalState.Submitted)
                                      .SetProperty(x => x.SubmittedAt, DateTimeOffset.UtcNow.AddHours(-6)));

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

        var ids = created.Select(r => r.Id).ToList();
        await db.Rfqs.Where(r => ids.Contains(r.Id))
            .ExecuteUpdateAsync(p => p.SetProperty(r => r.SubmissionOpensAt, DateTimeOffset.UtcNow.AddHours(-1)));
        db.ChangeTracker.Clear();

        var tenders = await db.Rfqs.AsSplitQuery().Include(r => r.Approvals).Include(r => r.Invitations).Include(r => r.Items)
            .Where(r => ids.Contains(r.Id)).OrderBy(r => r.ReferenceCode).ToListAsync();
        foreach (var rfq in tenders) rfq.OpenSubmissionWindow();
        await db.SaveChangesAsync();

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
            var monthsAgo = position % 10;
            var when = DateTimeOffset.UtcNow.AddMonths(-monthsAgo).AddDays(-(position * 5 % 27));
            await db.Awards.Where(a => a.Id == awardId)
                .ExecuteUpdateAsync(p => p.SetProperty(a => a.CreatedAt, when)
                                          .SetProperty(a => a.AwardedAt, when));
        }
    }

    private static async Task SeedVolumeAwardsAsync(AppDbContext db)
    {
        if (await db.Awards.AnyAsync(a => a.State == Domain.Awards.AwardState.Awarded)) return;

        var officerId = await db.Users.Where(u => u.Email == "officer@mots.local").Select(u => u.Id).FirstAsync();
        var template = await db.EvaluationTemplates.Include(t => t.Criteria).FirstAsync();
        var snapshot = System.Text.Json.JsonSerializer.Serialize(template.Criteria.Select(c => new
        {
            c.NameAr, c.NameEn, Dimension = c.Dimension.ToString(), c.Weight, c.MaxScore, c.Threshold,
            ScoringType = c.ScoringType.ToString(), c.RequiresJustification,
        }));

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
                SupplierId = isSupplierUser ? demoSupplierId : null,
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
