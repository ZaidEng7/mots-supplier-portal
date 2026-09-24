// The database context: the sets, the identity table names, and the concurrency and timestamp machinery.
//
// The entity configurations live in their own folder and are applied from the assembly, so this file holds the
// behaviour rather than the mappings.
//
//
// THE STALE-VERSION CHECK IS APPLIED ONCE HERE, NOT IN EVERY HANDLER
//
// The mapper only enforces optimistic concurrency if something sets the ORIGINAL value of the version property to
// what the CALLER believed it was. Without that it compares the row against the copy it just read, which always
// matches, which is the guard an earlier review called decoration.
//
// Two handlers did it by hand. The other forty-odd aggregate writes did not, so every one of them was silently
// last-write-wins.
//
// The expected value lands in the update's condition while the current value is left alone and advanced by the
// bump, so the statement reads "set the version to current plus one where the version is the expected one". A stale
// caller matches no row, which surfaces as the concurrency failure the pipeline turns into a precondition-failed
// answer.
//
// It applies to TOUCHED roots rather than modified ones. It used to look only for a modified root, which meant a
// request that changed a child stamped nothing: the root was still unchanged at that point, because the bump
// happens inside the save. A correct precondition on any child-write route was therefore ignored.
//
// And it applies only when exactly ONE versioned root is being written. A request touching two would otherwise have
// one caller-supplied version stamped onto both, which is worse than no guard, because it would fail the write that
// was never contended. That case does not arise today and is asserted by a test rather than assumed.
//
//
// TOUCHED ROOTS ARE KEYED ON THE TRACKED ENTITY, BY REFERENCE, NOT ON ITS WRAPPER
//
// The mapper hands out a fresh wrapper each time you ask, so a set of wrappers does not de-duplicate. The same
// supplier arrives once as a modified root and again as the owner of a changed child, and the set held both.
//
// That made the count two, which made the guard bail on its exactly-one precondition and apply NO guard, so a
// stale precondition was accepted and the write went through. Latent until a later change, because before it no
// child-write route declared a precondition, so no request reached here with both.
//
// The bump had the same problem in a quieter form: two wrappers meant the version advanced by two.
//
//
// THE VERSION IS ADVANCED BY THE APPLICATION, INCLUDING FOR A CHILD-ONLY WRITE
//
// It used to be the database's own row identifier, which moves only when the root ROW is written. A child insert
// marks the CHILD as new and leaves the root unchanged, so no update was emitted against the root, its version
// never advanced, and the guard found nothing to stamp.
//
// The result: on any route that only touches children, a correct precondition was silently ignored and two callers
// editing different children of one aggregate both won.
//
// Marking an otherwise-unchanged root as modified is what makes the guard fire.
//
// A changed entity is attributed to its root by walking its foreign keys ONE hop to a principal that is a versioned
// root and is already tracked in this same context. Every aggregate in this codebase is one level deep, and a
// grandchild would need the walk to recurse; rather than write a general graph walk for a shape that does not
// exist, it stops at one hop and exposes the count of what it could not attribute so the assumption is checkable
// from a test instead of hoped about.
//
// Only local principals count. One that is not already tracked is not being written in this unit of work, so there
// is nothing to bump and nothing to guard, and loading it here would turn a save into a query.
//
// An ADDED root is skipped: it has no prior version to guard or advance, and forcing it modified made the mapper
// emit an update against a row that did not exist yet, which is how registration started failing.
//
// A DELETED root is skipped too, and must be. Forcing it modified turns the delete into an update, so the row
// survives and the caller is told it was removed. Found when reverting an override brought the row back after every
// delete. A deleted row has no next version to advance while it still WANTS the guard, which is why it stays in the
// touched set and is skipped only at the bump.
//
//
// THE TWO TIMESTAMPS ARE STAMPED ALONGSIDE THE BUMP
//
// A root that records when it last changed is stamped in the same block that advances its version. The two facts
// describe one event, and writing them apart is how they come to disagree. Roots that do not declare it are
// untouched, so this costs nothing until one does.
//
// The state timestamp is stamped only when the state property ACTUALLY changed. A handler that re-assigns the same
// state has not moved anything, and stamping it would make a queue report a fresh arrival for a row that has been
// waiting a fortnight. The mapper knows the original value, so this asks rather than assumes.
//
// A newly added aggregate is stamped too: it has just entered its first state, and an absent value there would mean
// unknown, which is reserved for rows that predate the column.
//
// The state property's name is the aggregate's own declaration, read through the model rather than hard-coded, so a
// second aggregate whose state lives under a different name needs no change in this file.
//
//
// ALL FOUR SAVE OVERLOADS ARE COVERED, AND THAT WAS A REAL HOLE
//
// The mapper's public surface has four ways in, synchronous and asynchronous, each with and without the
// accept-changes flag, and the two convenience forms delegate to the other two.
//
// A caller using the synchronous form, or the flag overload, would have skipped the bump entirely: the write would
// land and the version would not move, which is precisely the defect this machinery exists to close. A concurrency
// scheme that applies on three paths out of four is worse than none, because it looks like it works.
//
// The bulk update and delete statements still bypass this by design, because they issue SQL without a change
// tracker. Nothing in this codebase uses them to mutate an aggregate a precondition guards; they are used for test
// setup and for the cleanup jobs.

namespace MotsSupplierPortal.Infrastructure.Persistence;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Domain.ReferenceData.Region> Regions => Set<Domain.ReferenceData.Region>();
    public DbSet<Domain.ReferenceData.Category> Categories => Set<Domain.ReferenceData.Category>();
    public DbSet<Domain.ReferenceData.UnitOfMeasure> UnitsOfMeasure => Set<Domain.ReferenceData.UnitOfMeasure>();
    public DbSet<Domain.ReferenceData.Incoterm> Incoterms => Set<Domain.ReferenceData.Incoterm>();
    public DbSet<Offering> Offerings => Set<Offering>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Representative> Representatives => Set<Representative>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<CategoryLink> CategoryLinks => Set<CategoryLink>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SecurityToken> SecurityTokens => Set<SecurityToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Domain.ReferenceData.DocumentType> DocumentTypes => Set<Domain.ReferenceData.DocumentType>();
    public DbSet<SupplierDocument> SupplierDocuments => Set<SupplierDocument>();
    public DbSet<DocumentExpiryReminder> DocumentExpiryReminders => Set<DocumentExpiryReminder>();
    public DbSet<SupplierReviewAnnotation> SupplierReviewAnnotations => Set<SupplierReviewAnnotation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Domain.ReferenceData.DocumentTypeCategory> DocumentTypeCategories => Set<Domain.ReferenceData.DocumentTypeCategory>();

    public DbSet<Domain.Configuration.EmailTemplateOverride> EmailTemplateOverrides => Set<Domain.Configuration.EmailTemplateOverride>();

    public DbSet<Domain.Configuration.UiStringOverride> UiStringOverrides => Set<Domain.Configuration.UiStringOverride>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<Domain.Configuration.SupplierFieldConfig> SupplierFieldConfigs => Set<Domain.Configuration.SupplierFieldConfig>();
    public DbSet<Domain.Configuration.SystemSetting> SystemSettings => Set<Domain.Configuration.SystemSetting>();
    public DbSet<Domain.Notifications.NotificationTemplate> NotificationTemplates => Set<Domain.Notifications.NotificationTemplate>();
    public DbSet<ReferenceCodeCounter> ReferenceCodeCounters => Set<ReferenceCodeCounter>();
    public DbSet<Domain.Idempotency.IdempotencyRecord> IdempotencyRecords => Set<Domain.Idempotency.IdempotencyRecord>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<SupplierOrgLink> SupplierOrgLinks => Set<SupplierOrgLink>();
    public DbSet<EvaluationTemplate> EvaluationTemplates => Set<EvaluationTemplate>();
    public DbSet<Criterion> Criteria => Set<Criterion>();
    public DbSet<Rfq> Rfqs => Set<Rfq>();
    public DbSet<RfqItem> RfqItems => Set<RfqItem>();
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<RfqAttachment> RfqAttachments => Set<RfqAttachment>();
    public DbSet<RfqApproval> RfqApprovals => Set<RfqApproval>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Clarification> Clarifications => Set<Clarification>();
    public DbSet<Addendum> Addenda => Set<Addendum>();
    public DbSet<Proposal> Proposals => Set<Proposal>();
    public DbSet<ProposalItem> ProposalItems => Set<ProposalItem>();
    public DbSet<ProposalDocument> ProposalDocuments => Set<ProposalDocument>();
    public DbSet<RequirementAnswer> RequirementAnswers => Set<RequirementAnswer>();
    public DbSet<MotsSupplierPortal.Domain.Evaluation.Evaluation> Evaluations => Set<MotsSupplierPortal.Domain.Evaluation.Evaluation>();
    public DbSet<EvaluationCriterionSnapshot> EvaluationCriterionSnapshots => Set<EvaluationCriterionSnapshot>();
    public DbSet<EvaluationAssignment> EvaluationAssignments => Set<EvaluationAssignment>();
    public DbSet<EvaluatorScore> EvaluatorScores => Set<EvaluatorScore>();
    public DbSet<ConsolidatedResult> ConsolidatedResults => Set<ConsolidatedResult>();
    public DbSet<Award> Awards => Set<Award>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<Domain.Integration.ApiKey> ApiKeys => Set<Domain.Integration.ApiKey>();

    public void ApplyExpectedVersion(uint expected)
    {
        var roots = TouchedVersionedRoots();

        if (roots.Count != 1) return;

        roots[0].Property(nameof(IVersionedAggregate.RowVersion)).OriginalValue = expected;
    }

    private List<EntityEntry> TouchedVersionedRoots()
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var roots = new List<EntityEntry>();

        void Add(EntityEntry entry)
        {
            if (seen.Add(entry.Entity)) roots.Add(entry);
        }

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is IVersionedAggregate)
            {
                if (entry.State != EntityState.Added) Add(entry);
                continue;
            }

            if (PrincipalRootOf(entry) is { State: not EntityState.Added } principal) Add(principal);
        }

        return [.. roots];
    }

    public int ModifiedVersionedRootCount() =>
        ChangeTracker.Entries().Count(e => e.State == EntityState.Modified && e.Entity is IVersionedAggregate);

    private void StampStateChanges()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not IStateTimestamped) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            var stateName = (string)entry.Metadata.ClrType
                .GetProperty(nameof(IStateTimestamped.StatePropertyName))!
                .GetValue(null)!;

            var state = entry.Property(stateName);
            var changed = entry.State is EntityState.Added
                || !Equals(state.CurrentValue, state.OriginalValue);
            if (!changed) continue;

            entry.Property(nameof(IStateTimestamped.StateChangedAt)).CurrentValue = now;
        }
    }

    private void BumpTouchedVersionedRoots()
    {
        foreach (var root in TouchedVersionedRoots())
        {
            if (root.State is EntityState.Deleted) continue;

            root.State = EntityState.Modified;
            var property = root.Property(nameof(IVersionedAggregate.RowVersion));
            property.CurrentValue = unchecked((uint)property.CurrentValue! + 1);

            if (root.Entity is ILastModified)
            {
                root.Property(nameof(ILastModified.UpdatedAt)).CurrentValue = DateTimeOffset.UtcNow;
            }
        }
    }

    private EntityEntry? PrincipalRootOf(EntityEntry entry)
    {
        foreach (var foreignKey in entry.Metadata.GetForeignKeys())
        {
            if (!typeof(IVersionedAggregate).IsAssignableFrom(foreignKey.PrincipalEntityType.ClrType))
            {
                continue;
            }

            var keyValues = foreignKey.Properties
                .Select(p => entry.Property(p.Name).CurrentValue)
                .ToArray();
            if (keyValues.Any(v => v is null)) continue;

            var principal = ChangeTracker.Entries()
                .FirstOrDefault(candidate =>
                    candidate.Entity is IVersionedAggregate
                    && candidate.Metadata.ClrType == foreignKey.PrincipalEntityType.ClrType
                    && foreignKey.PrincipalKey.Properties
                        .Select(p => candidate.Property(p.Name).CurrentValue)
                        .SequenceEqual(keyValues));

            if (principal is not null) return principal;
        }

        return null;
    }

    public IReadOnlyList<string> UnattributedChildTypes() =>
        [.. ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not IVersionedAggregate && PrincipalRootOf(e) is null)
            .Select(e => e.Metadata.ClrType.Name)
            .Distinct()
            .Order()];

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampStateChanges();
        BumpTouchedVersionedRoots();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampStateChanges();
        BumpTouchedVersionedRoots();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("role", "identity");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_role", "identity");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claim", "identity");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_login", "identity");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claim", "identity");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_token", "identity");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
