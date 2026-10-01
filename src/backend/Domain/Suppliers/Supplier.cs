// A supplier company: its profile, the people and places attached to it, where it is in registration
// and review, and whether it may currently trade.
//
// A supplier that registered here is the portal's record until the portal creates it in the ERP after
// approval. One that came from the ministry's ERP, or that the portal has created there, is refreshed from
// the ERP on every sync, on the fields the ERP sends and within the rules of ApplyErpSnapshot, and
// everything else about it is the portal's; see THE ERP below.
//
// This record, not the API and not the interface, is the only authority on which state changes are
// legal.
//
//
// THE STATE, and the question each part answers
//
// OnboardingState is how far the company has got through registration and review, from draft to
// approved or rejected.
//
// LifecycleState is whether an approved supplier may currently trade: active, suspended or
// deactivated, and none at all before approval. People move it, and so do three automatic paths: the
// document expiry job suspends, the automatic reinstatement after a renewal reactivates, and the ERP sync
// suspends a supplier the ERP no longer returns or turns away, and releases one it suspended only while it
// waited for the ERP's approval.
//
// ErpDisabledState, and the last two values of SyncStatus, are the ERP sync's memory of what it has
// already done about this supplier, so that it acts on the lifecycle once and a person's decision after
// that stands. They never decide eligibility themselves; THE ERP below lists them.
//
// ErpPushStatus is how far the portal has got creating a supplier that registered here in the ERP. It is
// kept apart from SyncStatus so that the push cannot write over the sync's memory, and it decides nothing
// about eligibility either.
//
// IsEligibleToParticipate is the single answer to "may this supplier be invited to a tender or submit a
// bid?", and it requires both of the first two. Onboarding must have reached approved, because an applicant
// mid-review is not eligible however healthy its lifecycle looks, and the lifecycle must be active,
// because suspended and deactivated are both excluded from new selection while existing obligations are
// handled by policy elsewhere.
//
// It lives here on the record so that the tender side and the bid side cannot each invent their own
// eligibility rule and drift apart. Neither had consumers when it was written, which is exactly why its
// tests enumerate every combination of the two states rather than the interesting ones: a rule with no
// callers is trivially correct, and only exhaustive assertions stand in for the real callers.
//
//
// THE THREE EDITING GATES
//
// EnsureEditable is the strict gate: editing is allowed while the company is filling in its profile, or
// when a reviewer has asked for something, and not once it is approved.
//
// EnsureContactDetailsEditable is the looser gate, for how to reach the company. Every child collection
// used to share the strict gate, and that gate stops at approval, so an approved supplier could not
// change a contact, a representative, an address or a branch. People leave and offices move. A buyer
// whose only named contact has left the company cannot ask a question, and the supplier had no way to
// fix it. That was never a rule anybody wrote; it was the registration gate applied to data
// registration was never about.
//
// What stays behind the strict gate is deliberate. Legal identity is what the reviewer approved, and
// bank details are where an award gets paid. Changing either after approval is a claim that needs
// checking rather than a correction, so both keep the strict gate until somebody decides what re-review
// such a change should trigger.
//
// EnsureEditableForComplianceField is the third gate, for the fields the ministry has marked as
// compliance-critical: legal identity, bank accounts and category links. Those stay editable after
// approval, because otherwise a legitimately changed bank account could never be updated at all. Editing
// one while approved sends the application back to review rather than silently accepting the change, and
// the lifecycle stays active throughout, so the supplier is not suspended merely for having an edit
// pending. Which fields count is configuration the caller resolves and passes in, because this record
// knows nothing about configuration. When a field's re-trigger is switched off, it behaves like any other
// profile field and is simply blocked once approved.
//
// UpdateLegalInfo, the bank account methods and the category methods return whether the edit re-triggered
// review, by passing on what that gate itself answered. The caller used to work it out afterwards by
// comparing the state before and after, which was correct only by coincidence.
//
//
// THE PROFILE
//
// Register creates a prospective supplier. The legal identifiers are captured generically, with no
// invented Syrian format rules. The legal identity is seeded with the trading name as an initial legal
// name, which the supplier can correct later, and the registration number lives on the legal identity
// rather than being a parameter here.
//
// UpdatedAt is stamped by the persistence layer wherever this record's version is advanced, rather than
// by each of the thirty-two handlers that write a supplier.
//
// It equals CreatedAt on a supplier nobody has edited, rather than being blank. "Never modified" and
// "modified at the moment it was created" are the same fact to a reader deciding whether their copy is
// stale, and a blank would make every consumer write the same fallback. Both come from one reading of the
// clock, not two: a supplier whose update time is a few ticks after its creation time reads as edited
// since creation to anything comparing them.
//
// UpdateCoreProfile takes the description, website, group and currency. Callers pass already-merged
// values: for a partial save the handler resolves each field to either the supplied value or the current
// one, so this method never has to know which were left out. Which individual fields a supplier may touch
// while a reviewer has asked for something is enforced by the handler, which can read the reviewer's open
// request, and is covered by its own tests rather than asserted here. An earlier comment in this file
// claimed an enforcement that did not exist.
//
//
// THE CHILD COLLECTIONS
//
// A new representative is never the primary one by construction; the caller has to set the primary
// explicitly if that is what they want.
//
// Exactly one representative is primary at all times, and that holds continuously rather than only at
// registration. The last remaining representative can never be removed, because there would be nobody
// left to be primary, and removing the primary while others remain promotes the next one automatically.
//
// A branch's address, when given, must be one of this supplier's own addresses, otherwise a branch could
// point at another supplier's address or at nothing.
//
// A bank account arrives already encrypted and masked, because the encryption service is infrastructure
// the domain does not depend on. The first account added is automatically the default, and exactly one
// account is the default whenever any exist. When the account number is not being changed the caller
// passes nothing for it, and only sends new encrypted and masked values when it actually changes.
// SetDefaultBankAccount lets the supplier pick, on top of the automatic first-added and
// promote-on-removal behaviour.
//
// The six collection caps are safety belts rather than business rules, and none of these collections had
// any cap before. All are realistically small by nature, since a company has a handful of
// representatives, addresses, branches and accounts rather than thousands, unlike the review queue's
// genuinely unbounded growth, so real paging was scoped out in favour of these caps plus tests that
// assert them. They are generous on purpose: high enough that no legitimate supplier ever meets one, low
// enough that a bug or an abuse generating rows in a loop fails loudly instead of growing a response
// forever.
//
//
// COMPLETENESS
//
// RequiredProfileFieldCodes is the full checklist GetMissingProfileFields walks: the core profile fields,
// a minimum of one address and one category link, and accepted terms.
//
// The whole list is exposed so that a completeness percentage has a denominator that cannot drift from
// its numerator. Counting what is missing is easy; counting how many there were in total is where a
// second implementation would appear, and the two would disagree the first time a field was added to the
// checklist and not to the count.
//
// Every entry except accepted terms refers to a profile field code directly rather than a string that
// merely happens to match one. This list feeds what the interface shows as missing, and the interface
// compares those strings against the same vocabulary reviewers flag against. That agreement used to be
// maintained by two independent sets of literals happening to match, so renaming a code would have
// silently desynchronised them, with nothing to catch it until a supplier's "missing" indicator stopped
// matching what a reviewer could actually flag. Referring to the constants makes that a compile error
// instead.
//
// Accepted terms stays a plain string on purpose. It is a submit-gate concept rather than something a
// reviewer can flag for correction, so there is no shared vocabulary to refer to.
//
// AcceptTerms records the version and the moment, which is the consent the submit gate looks for.
// Accepting again, after a later version ships, simply overwrites it, because only the latest acceptance
// needs to be current when the supplier submits.
//
// The terms version is a placeholder until the business owns the content and its versioning.
//
//
// REVIEW
//
// Submit refuses if the profile checklist is incomplete, or if any required document type has no
// satisfying uploaded version. The interface cannot get round it. The list of missing document types is
// computed by the handler, which owns the document query this record has no access to.
//
// PickUpForReview is a reviewer taking the application.
//
// AssignReviewer and UnassignReviewer are a reviewer claiming a queue item and letting it go. Claiming is
// manual self-claim rather than round-robin or manager-assigned, chosen as the simplest model that
// satisfies the requirement without inventing a workflow nobody asked for; nothing in the product
// documents specifies one.
//
// Assignment is independent of the onboarding state. A submitted, under-review or info-requested item can
// all be claimed, and claiming does not itself change the state, so the state machine below never touches
// it.
//
// Approve admits the supplier and makes it active. Its blocking-documents argument is the approval gate.
// The outbound event that announces the approval is written by ApproveApplicationHandler, not here.
//
// Approve also asks for the supplier to be created in the ERP, but only when it has no ExternalId, so
// neither a supplier from the ERP nor one approved again after the push linked it is created twice. THE ERP
// below has the push.
//
// The product owner's decision stands unchanged: approval does not require every document to be
// individually approved, and a document still waiting on a reviewer must not block.
//
// What that decision never covered was later fixed. It had been implemented as "refused only if a
// document was rejected, failed its scan or expired", which also let missing and unscanned required
// documents through. The decision was about not requiring approval; the implementation was about not
// requiring presence. Those are different claims, and only the first was ever decided.
//
// Reject needs a reason.
//
// RequestInfo asks the supplier for something. The record of what was asked, the reason and the flagged
// sections and documents, is created by the handler.
//
// Resubmit is the supplier answering. It is an intermediate state that is audited on its own before the
// handler immediately moves the application back to review for the next pass.
//
// Resubmit is gated exactly as Submit is. It used to take no argument at all, which made it a second
// entrance to review with no gate on it. Uploads are permitted while information has been requested and
// versioning only ever adds, so a supplier could re-upload a required document, superseding the approved
// version and leaving the latest one unscanned, come back through this ungated path, and be approved
// holding a required document nobody had looked at. Two entrances to the same state with different guards
// is where the next defect hides, so the argument is required rather than optional: a caller cannot
// forget it, because it will not compile.
//
// Unlike Submit, Resubmit is scoped to what the open request actually asked for rather than to every
// required item. It only ever runs from the info-requested state, so that scoping is safe, and it closes
// a real deadlock rather than a hypothetical one.
//
// The deadlock happened. A reviewer independently rejected one document through the separate
// per-document decision, without also flagging it in the information request. The old unscoped check
// demanded that document be fixed too, but re-uploading it was refused because it was not flagged, and a
// second information request to flag it was refused because the application was not back under review
// yet. The one door out of the info-requested state required walking through a door locked from the far
// side. Scoping to what was actually flagged closes that loop: an unrelated rejected document no longer
// blocks resolving what the ministry actually asked for. If the ministry wants that document fixed too,
// the fix is to flag it, not for the completeness gate to demand it unconditionally.
//
//
// AFTER APPROVAL
//
// Suspend blocks participation, is reversible, and needs a reason. It is not a data change: the supplier
// keeps its profile, its documents and its history, and the requirement to retain historical records
// applies from there on. What it loses is eligibility, which is answered in one place.
//
// Suspend has a second case, for a supplier the ERP sync is holding only while the ERP approves it: the
// lifecycle stays suspended and the suspension becomes a person's, so the ERP's approval no longer lifts
// it. The comment above Suspend says why.
//
// Reactivate is the reverse, and it needs a reason too, so the record says why participation was restored
// and not only why it was removed.
//
// Deactivate is final. There is deliberately no way out, not even back to suspended.
//
// It is reachable only from suspended, so deactivation is always a two-step decision. A direct path from
// active would make an irreversible action a single click on a live supplier; requiring suspension first
// means participation has already stopped and somebody has already written down why.
//
// The rule also requires the supplier's users to lose access, and that is not done here: revoking
// sign-ins and killing refresh-token families is an identity concern this record has no reach into. It
// belongs to the handler, and the tests assert it end to end rather than trusting a state field to imply
// it. A deactivated supplier whose users can still refresh their way to a valid session is the same class
// of defect as a second factor that never challenges anybody.
//
//
// THE ERP
//
// The ministry's ERP is the master for the fields it sends about the suppliers it holds. The hourly ERP
// sync, which is the same import an administrator can start by hand, creates and updates those suppliers
// through the methods below: RunErpImportHandler calls them, and the rules that decide are in
// Application/Integration. The sync's state below is written only by the sync, except that the push also
// sets ExternalId once, and the push's state only by Approve and the push; no API endpoint sets either
// directly. The fields the ERP sends can also be edited
// in the portal, and the next run overwrites them wherever the ERP has a value - ApplyErpSnapshot has the
// exceptions.
//
// THE PUSH GOES THE OTHER WAY. A supplier that registered here is created in the ERP once a reviewer
// approves it: its Supplier record there, then its address, its contact and a website user, and the links
// between them. Approve asks for it when the supplier has no ExternalId, and the push methods record how
// far it got. The ERP's name for the new supplier is saved as ExternalId as soon as the ERP returns it,
// and from then on the import matches the supplier by it like any other ERP supplier. The ERP creates a
// supplier in its own first state, which on the real ERP is Draft and not approved, so the next run
// suspends the portal supplier as SuspendedAsPending and releases it once the ERP approves it - the same
// rule as for every ERP supplier waiting for approval. SupplierErpPushJob acts on a request, and writes to
// the ERP only while the write switch on the ERP connection, CreateSuppliersInErp, is on; it is off by
// default.
//
// The state the sync keeps:
//   ExternalId         the supplier's identifier in the ERP, which every run matches on; the import sets it,
//                      or the push when it creates the supplier there
//   LastSyncedAt       the last time the ERP changed or re-linked it, not the last time a run looked
//   SyncStatus         whether the ERP has it, and the sync's memory of it leaving - read the warning on
//                      SupplierSyncStatus before writing it
//   ErpDisabledState   the sync's memory of the ERP disabling it or not approving it yet
//
// The methods:
//   ImportFromErp             creates a supplier the ERP has and the portal does not, approved without review
//   ApplyErpSnapshot          copies the fields the ERP sends onto a supplier the portal already has
//   RecordErpStanding         suspends, marks or releases it by whether the ERP lets it be used
//   ErpDisabledChangeFor      what RecordErpStanding would do, for the preview and the plan to forecast
//   IsMarkedAsUnwantedByErp   whether a mark holds the automatic reinstatement back
//   SuspendAsRemovedFromErp   suspends an active supplier the ERP no longer returns, and remembers why
//   MarkRemovedFromErp        marks one already out of service that the ERP no longer returns
//   MarkSynced                records that the ERP has it, which also clears the memory of it leaving
//   EndErpPendingHold         hands the sync's wait-for-approval suspension to a person once one acts
//   AllowsContactEdits        whether the import may add an address in this onboarding state
//
// The state the push keeps, never in SyncStatus:
//   ErpPushStatus          how far the push has got - read SupplierErpPushStatus
//   ErpPushRequestedAt     when approval asked for it
//   ErpPushStartedAt       the in-flight marker: set while an attempt is under way, and left behind by one
//                          that never finished
//   ErpPushAttempts        the failed attempts since it was last requested or retried
//   ErpPushNextAttemptAt   when the next attempt is due
//   ErpPushLastError       what the last failed attempt said
//
// The push methods:
//   BeginErpPush                 marks an attempt as under way
//   RecordErpSupplierCreated     saves the ERP's name for the supplier as ExternalId, as soon as it exists
//   CompleteErpPush              records that the address, contact and user are in the ERP too
//   RecordErpPushAttemptFailed   counts a failed attempt and says when to try again
//   FailErpPush                  stops the push until a person retries it
//   RetryErpPush                 a person starts a failed push again
//   IsInServiceForErpPush        whether the push may work on it: not while a person has it out of service

namespace MotsSupplierPortal.Domain.Suppliers;

using MotsSupplierPortal.Domain.Common;

// Whether the ERP has this supplier, and what the ERP sync remembers about it leaving the ERP.
//
//   Pending                not linked to the ERP. The default, which a supplier that registered here keeps.
//   Synced                 the ERP has it. Written by MarkSynced, when the import creates the supplier and on each
//                          run that finds it in the ERP.
//   Failed                 nothing writes it. The push to the ERP keeps its own state, in SupplierErpPushStatus.
//   RemovedFromErp         the sync suspended it because the ERP no longer returns it. Written by
//                          SuspendAsRemovedFromErp; a person's reinstatement after that stands.
//   MarkedRemovedFromErp   the ERP stopped returning it while it was already out of service here, so the sync only
//                          marked it. Written by MarkRemovedFromErp; it holds the automatic reinstatement back.
//
// THE LAST TWO ARE THE SYNC'S MEMORY OF AN ABSENCE, NOT A PUSH STATUS, and a push to the ERP must never overwrite
// them. RemovedFromErp is what stops the hourly job suspending again a supplier a person reinstated, and
// MarkedRemovedFromErp is what keeps the automatic reinstatement away from a supplier the ERP no longer has.
// MarkSynced sets Synced whatever the column held: that is how the memory clears when the ERP returns the supplier,
// and it is also how a push would wipe it. A push that called MarkSynced, or wrote Failed, for a supplier still
// missing from the ERP would have the next run suspend again somebody a person had reinstated. That is why the push
// has a status of its own, SupplierErpPushStatus.
//
// THE VALUES ARE STORED BY NAME in a varchar(20) column (SupplierConfiguration), and MarkedRemovedFromErp is already
// 20 characters. A longer new value compiles and passes the unit tests, and fails only when the sync saves it, so it
// needs a migration that widens the column first; renaming a value needs one that rewrites the stored rows.
public enum SupplierSyncStatus
{
    Pending,
    Synced,
    Failed,
    RemovedFromErp,
    MarkedRemovedFromErp,
}

// How far the portal has got creating, in the ERP, a supplier that registered here. It is the push's own state, kept
// apart from SupplierSyncStatus so that the push never writes over the sync's memory of a supplier leaving the ERP.
//
//   NotRequested   the portal has not asked for it. The default, which every supplier from the ERP keeps, and which a
//                  supplier that registered here keeps until a reviewer approves it.
//   Requested      a reviewer approved it while it had no ExternalId, and the ERP's Supplier record is still to be
//                  created. Written by Approve, and by RetryErpPush for a push that failed before the ERP had it.
//   Linked         the ERP has the Supplier record and its name is saved as ExternalId, but the address, the contact
//                  and the website user are not all created and linked yet. Written by RecordErpSupplierCreated, and
//                  by RetryErpPush for a push that failed after that.
//   Created        everything the push creates is in the ERP. Written by CompleteErpPush, and nothing moves it on:
//                  the supplier now has an ExternalId, so a later approval does not ask again.
//   Failed         the push has stopped and waits for a person. Written by FailErpPush; RetryErpPush starts it again.
//
// CREATED SAYS NOTHING ABOUT THE ERP'S APPROVAL. Whether the ERP lets the supplier be used is the sync's to read and
// record, in ErpDisabledState, as for any ERP supplier.
//
// Stored by name in a varchar(20) column, like SupplierSyncStatus; NotRequested is 12 characters.
public enum SupplierErpPushStatus
{
    NotRequested,
    Requested,
    Linked,
    Created,
    Failed,
}

// What the ERP sync has already done about the ERP turning this supplier away, so that it acts once and a person's
// decision after that stands. RecordErpStanding sets it, and its comment has the moves between the values.
//
// "DISABLED" IN THESE NAMES ALSO MEANS "NOT YET APPROVED IN THE ERP". MarkedDisabled is written for either reason.
// The sync writes SuspendedAsDisabled for a disable, but it is also what EndErpPendingHold leaves when a person acts
// on a supplier the sync was holding for the ERP's approval, so an active supplier the ERP never disabled can carry it.
//
//   NotDisabled           the ERP lets it be used, or has never turned it away, so a later refusal counts as new.
//                         The default; ImportFromErp and RecordErpStanding write it.
//   MarkedDisabled        the ERP turned it away while it was already out of service, so the sync only marked it.
//                         Written by RecordErpStanding; it holds the automatic reinstatement back.
//   SuspendedAsDisabled   the sync suspended it once for being turned away, and a person's reinstatement after that
//                         stands. Written by ImportFromErp, RecordErpStanding and EndErpPendingHold.
//   SuspendedAsPending    the sync suspended it only while the ERP approves it, and nothing has changed its lifecycle
//                         since. Written by ImportFromErp and RecordErpStanding; the only one the ERP's approval lifts.
//
// Stored by name in a varchar(20) column, like SupplierSyncStatus; SuspendedAsDisabled is 19 characters.
public enum SupplierErpDisabledState
{
    NotDisabled,
    MarkedDisabled,
    SuspendedAsDisabled,
    SuspendedAsPending,
}

// What one call of RecordErpStanding did. The run turns it into an audit row and a note on its report, and
// ErpDisabledChangeFor forecasts it for the preview and the plan. It is never stored. "Disabled" covers "not yet
// approved in the ERP" here too.
//
//   None                       nothing to record: nothing to do, or the sync already acted on an earlier run. The
//                              memory still returns to NotDisabled when the ERP lets the supplier be used.
//   Suspended                  an active supplier the ERP turns away was suspended.
//   Marked                     a supplier already out of service was turned away, so it was only marked.
//   Cleared                    the ERP lets a marked supplier be used again, so the run asks the automatic
//                              reinstatement again.
//   Released                   the ERP approved a supplier suspended only while it waited for that; it is active again.
//   ReleaseWaitsForDocuments   the same, but an award-critical document expired meanwhile and has no approved renewal,
//                              so it stays suspended and the next run asks again.
//   ReleaseWithdrawn           the ERP disabled a supplier that was waiting for its approval, so that approval will no
//                              longer lift the suspension.
public enum ErpDisabledChange
{
    None,
    Suspended,
    Marked,
    Cleared,
    Released,
    ReleaseWaitsForDocuments,
    ReleaseWithdrawn,
}

// Whether the ERP lets a supplier be used, worked out from its ERP record on every run by
// ErpImportAdmission.StandingOf. It is never stored; SupplierErpDisabledState is what the portal remembers of it.
//
//   Usable             enabled, and approved or with no approval workflow state at all
//   AwaitingApproval   enabled, but its workflow state is something other than "Approved", such as a pending approval
//   Disabled           the ERP's own disabled flag is set, which wins over a pending approval
public enum ErpStanding
{
    Usable,
    AwaitingApproval,
    Disabled,
}

public sealed class Supplier : IVersionedAggregate, ILastModified
{
    private readonly List<Representative> _representatives = [];
    private readonly List<Address> _addresses = [];
    private readonly List<Contact> _contacts = [];
    private readonly List<Branch> _branches = [];
    private readonly List<BankAccount> _bankAccounts = [];
    private readonly List<CategoryLink> _categoryLinks = [];

    public Guid Id { get; private init; }
    public string ReferenceCode { get; private init; } = null!;
    public string DisplayNameAr { get; private set; } = null!;
    public string DisplayNameEn { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Website { get; private set; }
    public string? LogoStorageKey { get; private set; }
    public string? SupplierGroup { get; private set; }
    public string? CurrencyCode { get; private set; }
    public LegalInfo? LegalInfo { get; private set; }
    public SupplierOnboardingState OnboardingState { get; private set; }
    public SupplierLifecycleState LifecycleState { get; private set; } = SupplierLifecycleState.None;
    public string? ExternalId { get; private set; }
    public SupplierSyncStatus SyncStatus { get; private set; } = SupplierSyncStatus.Pending;
    public SupplierErpDisabledState ErpDisabledState { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    // THE PUSH TO THE ERP, apart from SyncStatus. Approve writes the request and the push methods at the end of this
    // record write the rest; nothing else touches these.
    //
    //   ErpPushStatus          how far the push has got; SupplierErpPushStatus has the values.
    //   ErpPushRequestedAt     when approval first asked for it. Empty for a supplier the push was never asked for.
    //                          A later approval keeps it rather than moving it on, because the push's look for a
    //                          create whose answer was lost reaches back from here, and it has to reach the first
    //                          attempt, whichever approval that followed.
    //   ErpPushStartedAt       the in-flight marker: set when an attempt begins, and cleared when the push completes or
    //                          an attempt is recorded as failed. One still set when the next attempt begins means the
    //                          last one stopped part-way, perhaps after its request reached the ERP. The ERP makes a
    //                          second supplier for a second request, so that attempt looks in the ERP before it
    //                          creates.
    //   ErpPushAttempts        the failed attempts since it was last requested or retried, counted once each by
    //                          RecordErpPushAttemptFailed or FailErpPush.
    //   ErpPushNextAttemptAt   when the next attempt is due. Empty once the push is created or has failed, because
    //                          nothing is due then.
    //   ErpPushLastError       what the last failed attempt said, cut to ErpPushLastErrorMaxLength characters. It stays
    //                          through a retry, so the screen can still say why it failed, and clears once the push
    //                          completes.
    public const int ErpPushLastErrorMaxLength = 500;

    public SupplierErpPushStatus ErpPushStatus { get; private set; } = SupplierErpPushStatus.NotRequested;
    public DateTimeOffset? ErpPushRequestedAt { get; private set; }
    public DateTimeOffset? ErpPushStartedAt { get; private set; }
    public int ErpPushAttempts { get; private set; }
    public DateTimeOffset? ErpPushNextAttemptAt { get; private set; }
    public string? ErpPushLastError { get; private set; }

    public string? TermsAcceptedVersion { get; private set; }
    public DateTimeOffset? TermsAcceptedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public Guid? AssignedReviewerId { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }

    public const string CurrentTermsVersion = "1.0";

    public IReadOnlyList<Representative> Representatives => _representatives;
    public IReadOnlyList<Address> Addresses => _addresses;
    public IReadOnlyList<Contact> Contacts => _contacts;
    public IReadOnlyList<Branch> Branches => _branches;
    public IReadOnlyList<BankAccount> BankAccounts => _bankAccounts;
    public IReadOnlyList<CategoryLink> CategoryLinks => _categoryLinks;

    private Supplier() { }

    public static Supplier Register(
        string referenceCode,
        string displayNameAr,
        string displayNameEn,
        string? registrationNumber,
        string primaryRepresentativeName,
        string primaryRepresentativeEmail,
        string? primaryRepresentativePhone = null)
    {
        var now = DateTimeOffset.UtcNow;
        var supplier = new Supplier
        {
            Id = Guid.CreateVersion7(),
            ReferenceCode = referenceCode,
            DisplayNameAr = displayNameAr,
            DisplayNameEn = displayNameEn,
            OnboardingState = SupplierOnboardingState.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };

        supplier.LegalInfo = Domain.Suppliers.LegalInfo.Create(
            displayNameAr, displayNameEn, registrationNumber, taxId: null, SupplierLegalType.Company, establishedOn: null);

        supplier._representatives.Add(new Representative
        {
            Id = Guid.CreateVersion7(),
            SupplierId = supplier.Id,
            FullName = primaryRepresentativeName,
            Email = primaryRepresentativeEmail,
            Phone = primaryRepresentativePhone,
            IsPrimary = true,
        });

        return supplier;
    }

    public void MarkEmailVerified()
    {
        if (OnboardingState != SupplierOnboardingState.Draft)
        {
            throw new DomainException(
                $"Cannot mark email verified from state '{OnboardingState}'; only 'Draft' is valid.");
        }

        OnboardingState = SupplierOnboardingState.EmailVerified;
    }

    public bool IsEmailVerifiedOrLater =>
        OnboardingState is not SupplierOnboardingState.Draft;

    // Whether contact details - addresses, contacts, representatives - may be changed in this onboarding state. Public
    // so the ERP import can ask before it adds an address, rather than find out by the change being refused.
    public static bool AllowsContactEdits(SupplierOnboardingState state) =>
        state is not (SupplierOnboardingState.Draft or SupplierOnboardingState.Submitted
            or SupplierOnboardingState.UnderReview or SupplierOnboardingState.Rejected);

    private void EnsureContactDetailsEditable()
    {
        if (!AllowsContactEdits(OnboardingState))
        {
            throw new DomainException(
                $"Cannot edit contact details from state '{OnboardingState}'.");
        }
    }

    private void EnsureEditable()
    {
        if (OnboardingState is not (SupplierOnboardingState.EmailVerified or SupplierOnboardingState.ProfileInProgress or SupplierOnboardingState.InfoRequested))
        {
            throw new DomainException(
                $"Cannot edit profile from state '{OnboardingState}'; only 'EmailVerified', 'ProfileInProgress', or 'InfoRequested' allow edits.");
        }
    }

    private void AdvancePastEmailVerified()
    {
        if (OnboardingState == SupplierOnboardingState.EmailVerified)
        {
            OnboardingState = SupplierOnboardingState.ProfileInProgress;
        }
    }

    private bool EnsureEditableForComplianceField(bool isComplianceCritical)
    {
        if (isComplianceCritical && OnboardingState == SupplierOnboardingState.Approved)
        {
            OnboardingState = SupplierOnboardingState.UnderReview;
            return true;
        }

        EnsureEditable();
        return false;
    }

    public void UpdateCoreProfile(string? description, string? website, string? supplierGroup, string? currencyCode)
    {
        EnsureContactDetailsEditable();
        Description = description;
        Website = website;
        SupplierGroup = supplierGroup;
        CurrencyCode = currencyCode;
        AdvancePastEmailVerified();
    }

    public void SetLogo(string storageKey)
    {
        EnsureContactDetailsEditable();
        LogoStorageKey = storageKey;
    }

    public bool UpdateLegalInfo(string legalNameAr, string legalNameEn, string? registrationNumber, string? taxId, SupplierLegalType supplierType, DateOnly? establishedOn, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        var sameNumber = string.Equals(
            LegalInfo?.RegistrationNumber?.Trim(), registrationNumber?.Trim(), StringComparison.Ordinal);
        LegalInfo = Domain.Suppliers.LegalInfo.Create(
            legalNameAr,
            legalNameEn,
            registrationNumber,
            taxId,
            supplierType,
            establishedOn,
            sameNumber ? LegalInfo?.RegistrationType : null);
        AdvancePastEmailVerified();
        return reTriggered;
    }

    private const int MaxRepresentatives = 20;
    private const int MaxAddresses = 20;
    private const int MaxContacts = 20;
    private const int MaxBranches = 50;
    private const int MaxBankAccounts = 10;
    private const int MaxCategoryLinks = 50;

    public Representative AddRepresentative(string fullName, string email, string? phone, string? position)
    {
        EnsureContactDetailsEditable();
        if (_representatives.Count >= MaxRepresentatives)
        {
            throw new DomainException($"A supplier may have at most {MaxRepresentatives} representatives.");
        }
        var representative = new Representative
        {
            Id = Guid.CreateVersion7(),
            SupplierId = Id,
            FullName = fullName,
            Email = email,
            Phone = phone,
            Position = position,
            IsPrimary = false,
        };
        _representatives.Add(representative);
        return representative;
    }

    public void UpdateRepresentative(Guid representativeId, string fullName, string email, string? phone, string? position)
    {
        EnsureContactDetailsEditable();
        var representative = _representatives.FirstOrDefault(r => r.Id == representativeId) ?? throw new DomainException("Representative not found.");
        representative.FullName = fullName;
        representative.Email = email;
        representative.Phone = phone;
        representative.Position = position;
    }

    public void RemoveRepresentative(Guid representativeId)
    {
        EnsureContactDetailsEditable();
        var representative = _representatives.FirstOrDefault(r => r.Id == representativeId) ?? throw new DomainException("Representative not found.");
        if (_representatives.Count == 1)
        {
            throw new DomainException("Cannot remove the last remaining representative - a supplier must always have at least one.");
        }

        _representatives.Remove(representative);
        if (representative.IsPrimary)
        {
            _representatives[0].IsPrimary = true;
        }
    }

    public void SetPrimaryRepresentative(Guid representativeId)
    {
        EnsureContactDetailsEditable();
        var representative = _representatives.FirstOrDefault(r => r.Id == representativeId) ?? throw new DomainException("Representative not found.");
        foreach (var r in _representatives) r.IsPrimary = false;
        representative.IsPrimary = true;
    }

    public Address AddAddress(AddressKind kind, string line1, string? line2, string city, string regionCode, string country, string? postalCode, double? latitude, double? longitude)
    {
        EnsureContactDetailsEditable();
        if (_addresses.Count >= MaxAddresses)
        {
            throw new DomainException($"A supplier may have at most {MaxAddresses} addresses.");
        }
        var address = new Address
        {
            Id = Guid.CreateVersion7(),
            SupplierId = Id,
            Kind = kind,
            Line1 = line1,
            Line2 = line2,
            City = city,
            RegionCode = regionCode,
            Country = country,
            PostalCode = postalCode,
            Latitude = latitude,
            Longitude = longitude,
            IsPrimary = _addresses.Count == 0,
        };
        _addresses.Add(address);
        AdvancePastEmailVerified();
        return address;
    }

    public void UpdateAddress(Guid addressId, AddressKind kind, string line1, string? line2, string city, string regionCode, string country, string? postalCode, double? latitude, double? longitude)
    {
        EnsureContactDetailsEditable();
        var address = _addresses.FirstOrDefault(a => a.Id == addressId) ?? throw new DomainException("Address not found.");
        address.Kind = kind;
        address.Line1 = line1;
        address.Line2 = line2;
        address.City = city;
        address.RegionCode = regionCode;
        address.Country = country;
        address.PostalCode = postalCode;
        address.Latitude = latitude;
        address.Longitude = longitude;
    }

    public void RemoveAddress(Guid addressId)
    {
        EnsureContactDetailsEditable();
        var address = _addresses.FirstOrDefault(a => a.Id == addressId) ?? throw new DomainException("Address not found.");
        _addresses.Remove(address);
        if (address.IsPrimary && _addresses.Count > 0)
        {
            _addresses[0].IsPrimary = true;
        }
    }

    public Contact AddContact(string fullName, string email, string? phone, string? role)
    {
        EnsureContactDetailsEditable();
        if (_contacts.Count >= MaxContacts)
        {
            throw new DomainException($"A supplier may have at most {MaxContacts} contacts.");
        }
        var contact = new Contact { Id = Guid.CreateVersion7(), SupplierId = Id, FullName = fullName, Email = email, Phone = phone, Role = role };
        _contacts.Add(contact);
        return contact;
    }

    public void UpdateContact(Guid contactId, string fullName, string email, string? phone, string? role)
    {
        EnsureContactDetailsEditable();
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId) ?? throw new DomainException("Contact not found.");
        contact.FullName = fullName;
        contact.Email = email;
        contact.Phone = phone;
        contact.Role = role;
    }

    public void RemoveContact(Guid contactId)
    {
        EnsureContactDetailsEditable();
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId) ?? throw new DomainException("Contact not found.");
        _contacts.Remove(contact);
    }

    private void EnsureAddressBelongsToThisSupplier(Guid? addressId)
    {
        if (addressId is not null && !_addresses.Any(a => a.Id == addressId))
        {
            throw new DomainException("AddressId does not belong to this supplier.");
        }
    }

    public Branch AddBranch(string nameAr, string nameEn, Guid? addressId)
    {
        EnsureContactDetailsEditable();
        if (_branches.Count >= MaxBranches)
        {
            throw new DomainException($"A supplier may have at most {MaxBranches} branches.");
        }
        EnsureAddressBelongsToThisSupplier(addressId);
        var branch = new Branch { Id = Guid.CreateVersion7(), SupplierId = Id, NameAr = nameAr, NameEn = nameEn, AddressId = addressId };
        _branches.Add(branch);
        return branch;
    }

    public void UpdateBranch(Guid branchId, string nameAr, string nameEn, Guid? addressId, bool isActive)
    {
        EnsureContactDetailsEditable();
        EnsureAddressBelongsToThisSupplier(addressId);
        var branch = _branches.FirstOrDefault(b => b.Id == branchId) ?? throw new DomainException("Branch not found.");
        branch.NameAr = nameAr;
        branch.NameEn = nameEn;
        branch.AddressId = addressId;
        branch.IsActive = isActive;
    }

    public void RemoveBranch(Guid branchId)
    {
        EnsureContactDetailsEditable();
        var branch = _branches.FirstOrDefault(b => b.Id == branchId) ?? throw new DomainException("Branch not found.");
        _branches.Remove(branch);
    }

    public (BankAccount Account, bool ReTriggered) AddBankAccount(string accountHolderName, string bankName, string? branchName, byte[] encryptedAccountNumber, string maskedAccountNumber, string? swiftBic, string currencyCode, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        if (_bankAccounts.Count >= MaxBankAccounts)
        {
            throw new DomainException($"A supplier may have at most {MaxBankAccounts} bank accounts.");
        }
        var account = new BankAccount
        {
            Id = Guid.CreateVersion7(),
            SupplierId = Id,
            AccountHolderName = accountHolderName,
            BankName = bankName,
            BranchName = branchName,
            EncryptedAccountNumber = encryptedAccountNumber,
            MaskedAccountNumber = maskedAccountNumber,
            SwiftBic = swiftBic,
            CurrencyCode = currencyCode,
            IsDefault = _bankAccounts.Count == 0,
        };
        _bankAccounts.Add(account);
        return (account, reTriggered);
    }

    public bool UpdateBankAccount(Guid bankAccountId, string accountHolderName, string bankName, string? branchName, byte[]? encryptedAccountNumber, string? maskedAccountNumber, string? swiftBic, string currencyCode, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        var account = _bankAccounts.FirstOrDefault(b => b.Id == bankAccountId) ?? throw new DomainException("Bank account not found.");
        account.AccountHolderName = accountHolderName;
        account.BankName = bankName;
        account.BranchName = branchName;
        account.SwiftBic = swiftBic;
        account.CurrencyCode = currencyCode;
        if (encryptedAccountNumber is not null && maskedAccountNumber is not null)
        {
            account.EncryptedAccountNumber = encryptedAccountNumber;
            account.MaskedAccountNumber = maskedAccountNumber;
        }
        return reTriggered;
    }

    public bool RemoveBankAccount(Guid bankAccountId, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        var account = _bankAccounts.FirstOrDefault(b => b.Id == bankAccountId) ?? throw new DomainException("Bank account not found.");
        _bankAccounts.Remove(account);
        if (account.IsDefault && _bankAccounts.Count > 0)
        {
            _bankAccounts[0].IsDefault = true;
        }
        return reTriggered;
    }

    public void SetDefaultBankAccount(Guid bankAccountId)
    {
        EnsureEditable();
        var account = _bankAccounts.FirstOrDefault(b => b.Id == bankAccountId) ?? throw new DomainException("Bank account not found.");
        foreach (var b in _bankAccounts) b.IsDefault = false;
        account.IsDefault = true;
    }

    public (CategoryLink? Link, bool ReTriggered) LinkCategory(string categoryCode, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        if (_categoryLinks.Any(l => l.CategoryCode == categoryCode)) return (null, reTriggered);
        if (_categoryLinks.Count >= MaxCategoryLinks)
        {
            throw new DomainException($"A supplier may link at most {MaxCategoryLinks} categories.");
        }
        var link = new CategoryLink
        {
            Id = Guid.CreateVersion7(),
            SupplierId = Id,
            CategoryCode = categoryCode,
            IsPrimary = _categoryLinks.Count == 0,
        };
        _categoryLinks.Add(link);
        return (link, reTriggered);
    }

    public bool UnlinkCategory(string categoryCode, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        var link = _categoryLinks.FirstOrDefault(l => l.CategoryCode == categoryCode);
        if (link is null) return reTriggered;

        _categoryLinks.Remove(link);
        if (link.IsPrimary && _categoryLinks.Count > 0)
        {
            _categoryLinks[0].IsPrimary = true;
        }
        return reTriggered;
    }

    public bool SetPrimaryCategory(string categoryCode, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        var link = _categoryLinks.FirstOrDefault(l => l.CategoryCode == categoryCode)
            ?? throw new DomainException("That category is not linked to this supplier.");

        foreach (var l in _categoryLinks) l.IsPrimary = false;
        link.IsPrimary = true;
        return reTriggered;
    }

    public string? PrimaryCategoryCode => _categoryLinks.FirstOrDefault(l => l.IsPrimary)?.CategoryCode;

    public static readonly IReadOnlyList<string> RequiredProfileFieldCodes =
    [
        ProfileFieldCodes.LegalInfo,
        ProfileFieldCodes.CurrencyCode,
        ProfileFieldCodes.Address,
        ProfileFieldCodes.CategoryLink,
        ProfileFieldCodes.PrimaryContactPhone,
        "termsAccepted",
    ];

    public IReadOnlyList<string> GetMissingProfileFields()
    {
        var missing = new List<string>();
        if (LegalInfo is null || string.IsNullOrWhiteSpace(LegalInfo.LegalNameAr) || string.IsNullOrWhiteSpace(LegalInfo.LegalNameEn))
        {
            missing.Add(ProfileFieldCodes.LegalInfo);
        }
        if (string.IsNullOrWhiteSpace(CurrencyCode)) missing.Add(ProfileFieldCodes.CurrencyCode);
        if (!_addresses.Any(a => a.Kind == AddressKind.HeadOffice)) missing.Add(ProfileFieldCodes.Address);
        if (_categoryLinks.Count == 0) missing.Add(ProfileFieldCodes.CategoryLink);
        if (_representatives.Any(r => r.IsPrimary && string.IsNullOrWhiteSpace(r.Phone)) || _representatives.All(r => !r.IsPrimary))
        {
            missing.Add(ProfileFieldCodes.PrimaryContactPhone);
        }
        if (TermsAcceptedAt is null) missing.Add("termsAccepted");
        return missing;
    }

    public void AcceptTerms(string version)
    {
        if (OnboardingState is SupplierOnboardingState.Draft)
        {
            throw new DomainException("Cannot accept terms before the email is verified.");
        }

        TermsAcceptedVersion = version;
        TermsAcceptedAt = DateTimeOffset.UtcNow;
    }

    public void Submit(IReadOnlyList<string> missingRequiredDocumentTypeCodes)
    {
        if (OnboardingState != SupplierOnboardingState.ProfileInProgress)
        {
            throw new DomainException(
                $"Cannot submit from state '{OnboardingState}'; only 'ProfileInProgress' is valid.");
        }

        var missing = GetMissingProfileFields().Concat(missingRequiredDocumentTypeCodes).ToList();
        if (missing.Count > 0)
        {
            throw new DomainException($"Cannot submit: missing required items: {string.Join(", ", missing)}.");
        }

        OnboardingState = SupplierOnboardingState.Submitted;
    }

    public void PickUpForReview()
    {
        if (OnboardingState is not (SupplierOnboardingState.Submitted or SupplierOnboardingState.Resubmitted))
        {
            throw new DomainException(
                $"Cannot pick up for review from state '{OnboardingState}'; only 'Submitted' or 'Resubmitted' is valid.");
        }

        OnboardingState = SupplierOnboardingState.UnderReview;
    }

    public void AssignReviewer(Guid reviewerId)
    {
        AssignedReviewerId = reviewerId;
        AssignedAt = DateTimeOffset.UtcNow;
    }

    public void UnassignReviewer()
    {
        AssignedReviewerId = null;
        AssignedAt = null;
    }

    public void Approve(IReadOnlyList<string> blockingRequiredDocumentTypeCodes)
    {
        if (OnboardingState != SupplierOnboardingState.UnderReview)
        {
            throw new DomainException(
                $"Cannot approve from state '{OnboardingState}'; only 'UnderReview' is valid.");
        }

        if (blockingRequiredDocumentTypeCodes.Count > 0)
        {
            throw new DomainException(
                $"Cannot approve: required documents need attention: {string.Join(", ", blockingRequiredDocumentTypeCodes)}.");
        }

        OnboardingState = SupplierOnboardingState.Approved;
        LifecycleState = SupplierLifecycleState.Active;
        EndErpPendingHold();
        RequestErpPush();
    }

    // Approval asks for the supplier to be created in the ERP only while it has no ExternalId. A supplier from the ERP
    // has one from the start, and one approved again after a compliance edit has one once the push has linked it, so
    // neither is created a second time.
    //
    // One approved again before that - still waiting, or failed before the ERP had it - is asked for afresh, with the
    // count started again and the next attempt due at once, because a person has just approved it. Its in-flight marker
    // is left as it is: an attempt may already have reached the ERP, and the next one has to look before it creates.
    //
    // THE FIRST REQUEST'S TIME IS KEPT. SupplierErpPushJob looks for an earlier create among the ERP's records made
    // since the request, and an attempt after the first approval may have made one whose answer was lost. Moved on to
    // this approval, the look would start after that record and the push would post a second supplier.
    private void RequestErpPush()
    {
        if (ExternalId is not null) return;

        var now = DateTimeOffset.UtcNow;
        ErpPushStatus = SupplierErpPushStatus.Requested;
        ErpPushRequestedAt ??= now;
        ErpPushAttempts = 0;
        ErpPushNextAttemptAt = now;
    }

    // A supplier the sync suspended only while the ERP approves it may be suspended again by a person, without its
    // lifecycle changing: that is how they make the suspension theirs, so the ERP's approval no longer lifts it. It
    // already shows as suspended, and without this the only ways to keep it out were a reinstatement nobody meant, or
    // deactivation, which cannot be undone.
    public void Suspend(string reason)
    {
        if (LifecycleState == SupplierLifecycleState.Suspended
            && ErpDisabledState == SupplierErpDisabledState.SuspendedAsPending
            && !string.IsNullOrWhiteSpace(reason))
        {
            EndErpPendingHold();
            return;
        }

        if (LifecycleState != SupplierLifecycleState.Active)
        {
            throw new DomainException(
                $"Cannot suspend from lifecycle state '{LifecycleState}'; only 'Active' is valid.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A suspension reason is required.");
        }

        LifecycleState = SupplierLifecycleState.Suspended;
        EndErpPendingHold();
    }

    public void Reactivate(string reason)
    {
        if (LifecycleState != SupplierLifecycleState.Suspended)
        {
            throw new DomainException(
                $"Cannot reactivate from lifecycle state '{LifecycleState}'; only 'Suspended' is valid.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reactivation reason is required.");
        }

        LifecycleState = SupplierLifecycleState.Active;
        EndErpPendingHold();
    }

    public void Deactivate(string reason)
    {
        if (LifecycleState != SupplierLifecycleState.Suspended)
        {
            throw new DomainException(
                $"Cannot deactivate from lifecycle state '{LifecycleState}'; only 'Suspended' is valid. " +
                "Deactivation is terminal and is reachable only via suspension.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A deactivation reason is required.");
        }

        LifecycleState = SupplierLifecycleState.Deactivated;
        EndErpPendingHold();
    }

    // Once a person - or a rule acting for one - has acted on this supplier, a suspension the sync made while the ERP
    // was approving it is no longer the sync's to lift. It is still remembered as handled, so the ERP's pending state
    // does not suspend it a second time. That is why SuspendedAsDisabled also stands for suppliers the ERP never
    // disabled. Approve, Suspend, Reactivate and Deactivate all call it.
    private void EndErpPendingHold()
    {
        if (ErpDisabledState == SupplierErpDisabledState.SuspendedAsPending)
        {
            ErpDisabledState = SupplierErpDisabledState.SuspendedAsDisabled;
        }
    }

    public bool IsEligibleToParticipate =>
        OnboardingState == SupplierOnboardingState.Approved
        && LifecycleState == SupplierLifecycleState.Active;

    public void Reject(string reason)
    {
        if (OnboardingState != SupplierOnboardingState.UnderReview)
        {
            throw new DomainException(
                $"Cannot reject from state '{OnboardingState}'; only 'UnderReview' is valid.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A rejection reason is required.");
        }

        OnboardingState = SupplierOnboardingState.Rejected;
    }

    public void RequestInfo()
    {
        if (OnboardingState != SupplierOnboardingState.UnderReview)
        {
            throw new DomainException(
                $"Cannot request info from state '{OnboardingState}'; only 'UnderReview' is valid.");
        }

        OnboardingState = SupplierOnboardingState.InfoRequested;
    }

    public void Resubmit(
        IReadOnlyList<string> missingRequiredDocumentTypeCodes,
        IReadOnlyList<string> flaggedProfileFields,
        IReadOnlyList<string> flaggedDocumentTypeCodes)
    {
        if (OnboardingState != SupplierOnboardingState.InfoRequested)
        {
            throw new DomainException(
                $"Cannot resubmit from state '{OnboardingState}'; only 'InfoRequested' is valid.");
        }

        var missing = GetMissingProfileFields().Where(flaggedProfileFields.Contains)
            .Concat(missingRequiredDocumentTypeCodes.Where(flaggedDocumentTypeCodes.Contains))
            .ToList();
        if (missing.Count > 0)
        {
            throw new DomainException($"Cannot resubmit: missing required items: {string.Join(", ", missing)}.");
        }

        OnboardingState = SupplierOnboardingState.Resubmitted;
    }

    // A supplier that arrived from the ministry's ERP rather than through registration.
    //
    // IT LANDS APPROVED WITHOUT PASSING THROUGH REVIEW - and active, unless the ERP turns it away, as below - and that
    // is the whole reason this exists as its own factory. Approve() requires the state to be UnderReview with every
    // required document present, and an imported supplier has uploaded nothing - they never registered here. The
    // alternative was to walk the nine states with the document check suppressed, which works and writes an audit
    // trail claiming a reviewer reviewed them. No reviewer did. This says what actually happened instead.
    //
    // THE ARABIC NAME IS THE ERP'S OWN WHEN IT HAS ONE, from a field Seven Gates added. When it has none the English
    // name stands in, because the column is required; that is wrong and visible, which is the right kind of wrong -
    // somebody correcting supplier names can see which ones still need it.
    //
    // NO CATEGORY IS SET. The ERP's supplier groups are its own accounting vocabulary and the portal's are the
    // ministry's tourism taxonomy; there is no honest mapping, and a guess is indistinguishable from a choice
    // once it is written.
    //
    // THE REPRESENTATIVE IS NAMED AFTER THE COMPANY when the ERP gives no person, because the field is required
    // and inventing a human being's name is worse than repeating the company's.
    //
    // A SUPPLIER THE ERP HAS DISABLED, OR HAS NOT APPROVED YET, ARRIVES SUSPENDED, not active and not deactivated.
    // Active would let a company Seven Gates will not use be invited to tenders; deactivated is permanent in this
    // product, and neither state in the ERP is. It arrives remembered as SuspendedAsDisabled or SuspendedAsPending, so
    // the ERP's approval brings a waiting one into service - see RecordErpStanding.
    //
    // WHAT ELSE THE ERP HOLDS ARRIVES IN details: the Arabic name, the registration number and its type, the supplier
    // group and a description. Each is empty when the ERP has none, except the Arabic name, which the portal requires
    // and which therefore starts as the English one.
    //
    // It is linked to the ERP from the start: MarkSynced records its identifier.
    public static Supplier ImportFromErp(
        string referenceCode,
        string externalId,
        string displayNameEn,
        string? taxId,
        SupplierLegalType legalType,
        string? currencyCode,
        string representativeName,
        string representativeEmail,
        string? representativePhone,
        ErpStanding standing = ErpStanding.Usable,
        ErpSupplierDetails? details = null)
    {
        var now = DateTimeOffset.UtcNow;
        var displayNameAr = details?.DisplayNameAr ?? displayNameEn;

        var supplier = new Supplier
        {
            Id = Guid.CreateVersion7(),
            ReferenceCode = referenceCode,
            DisplayNameAr = displayNameAr,
            DisplayNameEn = displayNameEn,
            CurrencyCode = currencyCode,
            SupplierGroup = details?.SupplierGroup,
            Description = details?.Description,
            OnboardingState = SupplierOnboardingState.Approved,
            LifecycleState = standing == ErpStanding.Usable ? SupplierLifecycleState.Active : SupplierLifecycleState.Suspended,
            ErpDisabledState = standing switch
            {
                ErpStanding.AwaitingApproval => SupplierErpDisabledState.SuspendedAsPending,
                ErpStanding.Disabled => SupplierErpDisabledState.SuspendedAsDisabled,
                _ => SupplierErpDisabledState.NotDisabled,
            },
            CreatedAt = now,
            UpdatedAt = now,
        };

        supplier.LegalInfo = Domain.Suppliers.LegalInfo.Create(
            displayNameAr,
            displayNameEn,
            details?.RegistrationNumber,
            taxId,
            legalType,
            establishedOn: null,
            details?.RegistrationType);

        supplier._representatives.Add(new Representative
        {
            Id = Guid.CreateVersion7(),
            SupplierId = supplier.Id,
            FullName = representativeName,
            Email = representativeEmail,
            Phone = representativePhone,
            IsPrimary = true,
        });

        supplier.MarkSynced(externalId);

        return supplier;
    }

    // What a later run of the import may change on a supplier it has seen before.
    //
    // THE ERP WINS ONLY ON THE FIELDS IT SENDS. Everything else - the map pin somebody placed, the bank details
    // they entered, the documents they uploaded, the category the ministry assigned - is the portal's and is not
    // touched. A null arriving from the ERP means "this system does not know", not "delete what you have". The English
    // name and the legal type always arrive with a value and are always written; the import reads an ERP supplier with
    // no type as a company.
    //
    // THE REPRESENTATIVE IS RENAMED ONLY TO A PERSON, AND ONLY FROM THE COMPANY'S NAME. The caller passes a name only
    // when the ERP's contact is somebody rather than the "<supplier> Contact" the ERP makes on its own. And it replaces
    // only the stand-in the import put there - a representative still called after the company. Once a person's name
    // is on the primary representative it is the portal's: the supplier may have promoted somebody else to primary, and
    // renaming them to the ERP's contact would put one person's name on another's account.
    //
    // A NULL EMAIL MEANS "THE ERP HAS NONE", and the one on file is kept. That matters because the import fills a
    // missing email with a placeholder: passing the placeholder through here would overwrite a real address a
    // supplier later gave the portal, just because the ERP still has nothing.
    //
    // WHETHER THE ERP TURNS IT AWAY IS RECORDED SEPARATELY, by RecordErpStanding, because that decides a lifecycle
    // and this only copies fields.
    //
    // A RUN THAT FINDS NOTHING NEW CHANGES NOTHING. The sync runs every hour, and a supplier whose version moved on every
    // run would refuse the save of anybody who opened it before the hour and saved after it, although nobody had
    // changed a thing. So only a value that differs is written, and it says whether anything was.
    public bool ApplyErpSnapshot(
        string displayNameEn,
        string? taxId,
        SupplierLegalType legalType,
        string? currencyCode,
        string? representativeEmail,
        string? representativePhone,
        ErpSupplierDetails? details = null,
        string? representativeName = null)
    {
        var companyNames = new[] { DisplayNameEn, displayNameEn };
        var before = (DisplayNameEn, DisplayNameAr, CurrencyCode, SupplierGroup, Description);

        DisplayNameEn = displayNameEn;
        if (currencyCode is not null) CurrencyCode = currencyCode;
        if (details?.DisplayNameAr is not null) DisplayNameAr = details.DisplayNameAr;
        if (details?.SupplierGroup is not null) SupplierGroup = details.SupplierGroup;
        if (details?.Description is not null) Description = details.Description;

        var changed = before != (DisplayNameEn, DisplayNameAr, CurrencyCode, SupplierGroup, Description);

        var legalInfo = Domain.Suppliers.LegalInfo.Create(
            details?.DisplayNameAr ?? LegalInfo?.LegalNameAr ?? displayNameEn,
            displayNameEn,
            details?.RegistrationNumber ?? LegalInfo?.RegistrationNumber,
            taxId ?? LegalInfo?.TaxId,
            legalType,
            LegalInfo?.EstablishedOn,
            details?.RegistrationType ?? LegalInfo?.RegistrationType);

        if (LegalInfo is null || !LegalInfo.Matches(legalInfo))
        {
            LegalInfo = legalInfo;
            changed = true;
        }

        var representative = _representatives.FirstOrDefault(r => r.IsPrimary) ?? _representatives.FirstOrDefault();
        if (representative is not null)
        {
            var person = (representative.FullName, representative.Email, representative.Phone);

            if (representativeName is not null && companyNames.Contains(representative.FullName))
            {
                representative.FullName = representativeName;
            }

            if (representativeEmail is not null) representative.Email = representativeEmail;
            if (representativePhone is not null) representative.Phone = representativePhone;

            changed |= person != (representative.FullName, representative.Email, representative.Phone);
        }

        return changed;
    }

    // Recording whether the ERP will let this supplier be used: suspending it once if it will not and it is active,
    // marking it if it is out of service already, and releasing it once the ERP approves a supplier the sync suspended
    // only while it waited for that.
    //
    // THE ERP MAY SUSPEND, AND IT MAY RELEASE ONLY ITS OWN WAIT. A supplier the ERP disables, or has not approved, is
    // suspended here, so it cannot be invited to a tender Seven Gates would not honour. A supplier the ERP re-enables
    // stays as it is: reinstating somebody is a decision a person makes on this side, and they may have been suspended
    // here for a reason the ERP knows nothing about. The one exception is a supplier the sync suspended ONLY because
    // the ERP had not approved it yet, that nobody has touched since: when the ERP approves it, it comes into service.
    // Otherwise every new supplier - which the hourly sync usually meets while it is still waiting for Seven Gates'
    // chief accountant - would stay suspended until somebody noticed and reinstated it by hand.
    //
    // IT ACTS ONCE, AND REMEMBERS THAT IN ErpDisabledState, as an absence from the ERP is remembered in SyncStatus and
    // for the same reason: suspending on every run on which the ERP says no would undo, within the hour, a reinstatement
    // a person made on purpose. What each value means is on SupplierErpDisabledState. What one call does, by the ERP's
    // standing and the supplier's memory and lifecycle, with the change it returns and what it leaves behind:
    //
    //   The ERP lets it be used:
    //     SuspendedAsPending, not active          Released - active, NotDisabled; or ReleaseWaitsForDocuments - nothing
    //                                               changes, while an expired award-critical document has no renewal
    //     MarkedDisabled                          Cleared - NotDisabled
    //     anything else                           None - NotDisabled, lifecycle untouched
    //   The ERP disables it, or has not approved it:
    //     SuspendedAsPending, ERP disables it     ReleaseWithdrawn - SuspendedAsDisabled
    //     SuspendedAsPending, ERP not approved    None - nothing changes
    //     SuspendedAsDisabled                     None - nothing changes
    //     NotDisabled or MarkedDisabled, active   Suspended - suspended, as SuspendedAsPending if the ERP has not
    //                                               approved it and SuspendedAsDisabled if it disabled it
    //     NotDisabled, not active                 Marked - MarkedDisabled
    //     MarkedDisabled, not active              None - nothing changes
    //
    // It returns what changed, so the caller can record who did it and look again at a reinstatement a mark held up.
    //
    // THE RELEASE ALSO WAITS FOR ITS DOCUMENTS. An award-critical document that expired while the supplier waited was
    // never acted on - the expiry rule suspends only active suppliers, and a document expires once - so the ERP's
    // approval alone would bring back a supplier with an expired commercial registration. The caller says whether every
    // expired award-critical document has an approved renewal; until it has, the hold stands and every run asks again,
    // so the supplier comes back on the first run after both the approval and the renewal are in.
    public ErpDisabledChange RecordErpStanding(ErpStanding standing, bool documentsAllowRelease = true)
    {
        var change = ErpDisabledChangeFor(
            ErpDisabledState, LifecycleState == SupplierLifecycleState.Active, standing, documentsAllowRelease);

        if (change == ErpDisabledChange.Released)
        {
            LifecycleState = SupplierLifecycleState.Active;
        }

        if (standing == ErpStanding.Usable && change != ErpDisabledChange.ReleaseWaitsForDocuments)
        {
            ErpDisabledState = SupplierErpDisabledState.NotDisabled;
        }
        else if (change == ErpDisabledChange.Suspended)
        {
            LifecycleState = SupplierLifecycleState.Suspended;
            ErpDisabledState = standing == ErpStanding.AwaitingApproval
                ? SupplierErpDisabledState.SuspendedAsPending
                : SupplierErpDisabledState.SuspendedAsDisabled;
        }
        else if (change == ErpDisabledChange.Marked)
        {
            ErpDisabledState = SupplierErpDisabledState.MarkedDisabled;
        }
        else if (change == ErpDisabledChange.ReleaseWithdrawn)
        {
            ErpDisabledState = SupplierErpDisabledState.SuspendedAsDisabled;
        }

        return change;
    }

    // What RecordErpStanding would do, worked out from the supplier's memory and lifecycle alone, so the preview and the
    // plan can forecast the same outcome from the columns they read without loading the supplier, and the run can say
    // what a held-back change would have been. It returns only the change; RecordErpStanding sets the memory.
    public static ErpDisabledChange ErpDisabledChangeFor(
        SupplierErpDisabledState state, bool isActive, ErpStanding standing, bool documentsAllowRelease = true)
    {
        if (standing == ErpStanding.Usable)
        {
            return state switch
            {
                SupplierErpDisabledState.SuspendedAsPending when !isActive => documentsAllowRelease
                    ? ErpDisabledChange.Released
                    : ErpDisabledChange.ReleaseWaitsForDocuments,
                SupplierErpDisabledState.MarkedDisabled => ErpDisabledChange.Cleared,
                _ => ErpDisabledChange.None,
            };
        }

        if (state == SupplierErpDisabledState.SuspendedAsPending && standing == ErpStanding.Disabled)
        {
            return ErpDisabledChange.ReleaseWithdrawn;
        }

        if (state is SupplierErpDisabledState.SuspendedAsDisabled or SupplierErpDisabledState.SuspendedAsPending)
        {
            return ErpDisabledChange.None;
        }

        if (isActive) return ErpDisabledChange.Suspended;

        return state == SupplierErpDisabledState.MarkedDisabled ? ErpDisabledChange.None : ErpDisabledChange.Marked;
    }

    // Whether a mark is standing that says the ERP no longer wants this supplier, although the sync has not yet
    // suspended it for that. While one stands, nothing automatic may bring the supplier back into service.
    public bool IsMarkedAsUnwantedByErp =>
        SyncStatus == SupplierSyncStatus.MarkedRemovedFromErp
        || ErpDisabledState == SupplierErpDisabledState.MarkedDisabled;

    // Suspending a supplier because the ERP no longer returns it, and remembering why.
    //
    // THE REASON IS KEPT ON THE SUPPLIER, not only in the audit trail, because the next run has to know it.
    // A person may reinstate the supplier; if nothing remembered that it had already been suspended for this same
    // absence, the next run would suspend it again, on every run, undoing their decision by a job nobody watches. The
    // mark clears the moment the supplier reappears in the ERP, through MarkSynced, so a second disappearance later is
    // treated as new.
    public void SuspendAsRemovedFromErp()
    {
        if (LifecycleState != SupplierLifecycleState.Active)
        {
            throw new DomainException(
                $"Cannot suspend from lifecycle state '{LifecycleState}'; only 'Active' is valid.");
        }

        LifecycleState = SupplierLifecycleState.Suspended;
        SyncStatus = SupplierSyncStatus.RemovedFromErp;
    }

    // Marking a supplier that is already suspended or deactivated as gone from the ERP, without touching its lifecycle.
    //
    // Without it, that supplier would count as "vanished in this run" on every run for as long as it stayed missing, and
    // the plan could pair it with any new company that happened to share its tax number, months later.
    //
    // IT IS A DIFFERENT MEMORY FROM SuspendAsRemovedFromErp. That one means "the sync suspended it, so a person's
    // reinstatement must be respected". This one means only "it was already out of service when it left", and the sync
    // has not yet suspended it for that: the automatic reinstatement after a document renewal leaves it alone, and one
    // a person reactivates is suspended once on the next run. One memory shared by both would leave a supplier
    // reactivated later never suspended for its absence.
    //
    // It clears through MarkSynced when the ERP returns the supplier, except on a run that holds a mass turn-away back
    // and finds it turned away - see RunErpImportHandler.UpdateAsync.
    public void MarkRemovedFromErp() => SyncStatus = SupplierSyncStatus.MarkedRemovedFromErp;

    // Recording that the ERP has this supplier: its identifier, SyncStatus Synced, and when.
    //
    // IT OVERWRITES WHATEVER SyncStatus HELD. That is how RemovedFromErp and MarkedRemovedFromErp clear when the ERP
    // returns the supplier, and why it is called only when the ERP has returned the supplier - see the warning on
    // SupplierSyncStatus.
    //
    // LastSyncedAt is the last time the ERP changed or re-linked it, not the last time a run looked: when changed is
    // false and the supplier is already Synced under this identifier, nothing is written, because stamping it on every
    // hourly run would move every supplier's version with it - see ApplyErpSnapshot. When the last run happened is kept
    // once, on the connection.
    public void MarkSynced(string externalId, bool changed = true)
    {
        if (!changed && ExternalId == externalId && SyncStatus == SupplierSyncStatus.Synced) return;

        ExternalId = externalId;
        SyncStatus = SupplierSyncStatus.Synced;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    // THE PUSH TO THE ERP: creating in the ERP a supplier that registered here, once a reviewer has approved it.
    //
    // THESE METHODS WRITE ONLY THE PUSH'S OWN STATE, AND ExternalId. SyncStatus, LastSyncedAt, ErpDisabledState and the
    // lifecycle belong to the sync and to people. The warning on SupplierSyncStatus says what a push that wrote
    // SyncStatus would undo, and whether the ERP lets the supplier be used is for the sync to read, not for the push to
    // assume.
    //
    // EACH REFUSES FROM A STATUS IT DOES NOT BELONG TO, so a job that runs twice, or a retry pressed while an attempt
    // is under way, cannot move the push somewhere it was never meant to go:
    //
    //   Approve                      any, while there is no ExternalId   -> Requested
    //   BeginErpPush                 Requested or Linked                 -> unchanged, an attempt under way
    //   RecordErpSupplierCreated     Requested or Linked                 -> Linked, with ExternalId
    //   CompleteErpPush              Linked                              -> Created
    //   RecordErpPushAttemptFailed   Requested or Linked                 -> unchanged, the next attempt later
    //   FailErpPush                  Requested or Linked                 -> Failed
    //   RetryErpPush                 Failed, while in service            -> Requested, or Linked with an ExternalId
    //
    // A method that writes a time takes it from the caller, so one run of the job is one moment on every supplier it
    // touches.

    // An attempt begins. A marker left by an attempt that never finished is replaced, so a caller that finds one
    // looks in the ERP for the supplier before it begins, not after.
    public void BeginErpPush(DateTimeOffset now)
    {
        EnsureErpPushUnderWay("begin an ERP push");
        ErpPushStartedAt = now;
    }

    // The ERP has created the Supplier record, and erpName is its name there. It is saved as ExternalId at once, before
    // the address, the contact and the user, so that a failure after this never creates the supplier a second time: the
    // next attempt finds the name and carries on from there, and that attempt is due at once. The marker stays, because
    // the attempt is still under way.
    //
    // A DIFFERENT NAME IS REFUSED. ExternalId is what the import matches on, and replacing it would point this supplier
    // at another ERP record. The same name again is accepted, so an attempt that finds the record it created before is
    // not an error.
    public void RecordErpSupplierCreated(string erpName, DateTimeOffset now)
    {
        EnsureErpPushUnderWay("record the ERP supplier");

        if (string.IsNullOrWhiteSpace(erpName))
        {
            throw new DomainException("The ERP's name for the supplier is required.");
        }

        if (ExternalId is not null && ExternalId != erpName)
        {
            throw new DomainException(
                $"Cannot link this supplier to ERP supplier '{erpName}'; it is already linked to '{ExternalId}'.");
        }

        ExternalId = erpName;
        ErpPushStatus = SupplierErpPushStatus.Linked;
        ErpPushNextAttemptAt = now;
    }

    // The address, the contact and the website user are in the ERP too, and linked. Nothing is due, and the marker and
    // the last error are cleared. Only a linked push can complete, because without an ExternalId the ERP has nothing.
    public void CompleteErpPush()
    {
        if (ErpPushStatus != SupplierErpPushStatus.Linked)
        {
            throw new DomainException(
                $"Cannot complete the ERP push from status '{ErpPushStatus}'; only 'Linked' is valid.");
        }

        ErpPushStatus = SupplierErpPushStatus.Created;
        ErpPushStartedAt = null;
        ErpPushNextAttemptAt = null;
        ErpPushLastError = null;
    }

    // An attempt failed and the push will try again at nextAttemptAt, which the caller works out. The status stays, so
    // a linked supplier carries on from its ExternalId.
    //
    // IT CLEARS THE MARKER, which says the attempt is over. The marker is all that remembers a create may have
    // reached the ERP, so a caller that cannot tell whether its create did - a timeout, or no answer at all - finds
    // out before it records the failure. Otherwise the next attempt creates the supplier a second time.
    //
    // A long message is cut rather than refused, because losing the record of a failure over its length would defeat
    // the point of keeping it.
    public void RecordErpPushAttemptFailed(string message, DateTimeOffset nextAttemptAt)
    {
        EnsureErpPushUnderWay("record a failed ERP push attempt");
        ErpPushLastError = ErpPushError(message);
        ErpPushAttempts++;
        ErpPushStartedAt = null;
        ErpPushNextAttemptAt = nextAttemptAt;
    }

    // The push stops until a person retries it: the ERP refused something it will refuse again, or the attempts ran
    // out. The failed attempt counts, the marker is cleared as in RecordErpPushAttemptFailed, and nothing is due.
    public void FailErpPush(string message)
    {
        EnsureErpPushUnderWay("fail the ERP push");
        ErpPushLastError = ErpPushError(message);
        ErpPushAttempts++;
        ErpPushStatus = SupplierErpPushStatus.Failed;
        ErpPushStartedAt = null;
        ErpPushNextAttemptAt = null;
    }

    // Whether the push may work on this supplier: only while it is in service.
    //
    // A SUPPLIER A PERSON HAS TAKEN OUT OF SERVICE IS NOT PUSHED. Suspended or deactivated, it would otherwise be
    // created in the ERP with a website user holding the Supplier role, for a company whose access here was just
    // withdrawn. Its push stays where it is, and goes on if the supplier comes back into service; a deactivated one
    // never does.
    //
    // THE SYNC'S OWN HOLD WHILE THE ERP APPROVES THE SUPPLIER IS NOT OUT OF SERVICE. The Draft record the push made is
    // what put the supplier there, and the rest of the push - the address, the contact, the website user - is part of
    // what the ERP team approves. Any person acting on the supplier ends that hold (EndErpPendingHold), so a suspension
    // somebody chose is never mistaken for it. SupplierErpPushJob asks the same question in the database.
    public bool IsInServiceForErpPush =>
        LifecycleState == SupplierLifecycleState.Active
        || (LifecycleState == SupplierLifecycleState.Suspended
            && ErpDisabledState == SupplierErpDisabledState.SuspendedAsPending);

    // A person starts a failed push again. It goes back to Requested, or to Linked when the ERP already has the
    // Supplier record, so the next attempt never creates it twice. The count starts again and the next attempt is due
    // at once. A supplier out of service is refused, because the push would not run for it; see IsInServiceForErpPush.
    public void RetryErpPush(DateTimeOffset now)
    {
        if (ErpPushStatus != SupplierErpPushStatus.Failed)
        {
            throw new DomainException(
                $"Cannot retry the ERP push from status '{ErpPushStatus}'; only 'Failed' is valid.");
        }

        if (!IsInServiceForErpPush)
        {
            throw new DomainException(
                $"Cannot retry the ERP push of a supplier that is out of service (lifecycle state '{LifecycleState}'). "
                + "A supplier a person has suspended or deactivated is not created in the ERP; reinstate it first.");
        }

        ErpPushStatus = ExternalId is null ? SupplierErpPushStatus.Requested : SupplierErpPushStatus.Linked;
        ErpPushAttempts = 0;
        ErpPushNextAttemptAt = now;
    }

    private void EnsureErpPushUnderWay(string action)
    {
        if (ErpPushStatus is not (SupplierErpPushStatus.Requested or SupplierErpPushStatus.Linked))
        {
            throw new DomainException(
                $"Cannot {action} from status '{ErpPushStatus}'; only 'Requested' or 'Linked' is valid.");
        }
    }

    // A blank message is refused, because a failure whose cause nobody can read is one nobody can fix; a long one is
    // cut to the column.
    private static string ErpPushError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new DomainException("A failed ERP push needs a message saying what went wrong.");
        }

        return message.Length <= ErpPushLastErrorMaxLength
            ? message
            : message[..(ErpPushLastErrorMaxLength - 1)] + "…";
    }
}
