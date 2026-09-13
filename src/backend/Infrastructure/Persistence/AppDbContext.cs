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

namespace MotsSupplierPortal.Infrastructure.Persistence;

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
    /// <summary>BRULE-016: which categories a document type is required for. Written by the admin surface and
    /// read by RequiredDocumentTypeResolver since D-59.</summary>
    public DbSet<Domain.ReferenceData.DocumentTypeCategory> DocumentTypeCategories => Set<Domain.ReferenceData.DocumentTypeCategory>();

    /// <summary>T-076: administrator rewordings of the transactional emails.</summary>
    public DbSet<Domain.Configuration.EmailTemplateOverride> EmailTemplateOverrides => Set<Domain.Configuration.EmailTemplateOverride>();

    /// <summary>SCR-716: administrator rewordings of shipped interface strings.</summary>
    public DbSet<Domain.Configuration.UiStringOverride> UiStringOverrides => Set<Domain.Configuration.UiStringOverride>();
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>SCR-901/D-60: the notification types a user has switched off. A row means "do not deliver";
    /// no row means deliver - see NotificationPreference for why absence is the safe direction.</summary>
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

    /// <summary>
    /// §8.1's stale-version check, applied once here rather than in every handler.
    ///
    /// <para>EF only enforces optimistic concurrency if something sets the ORIGINAL value of the
    /// version property to what the CALLER believed it was. Without that it compares the row against
    /// the copy it just read, which always matches - the guard MSP-65 described as "decoration".
    /// Two handlers did it by hand; the other forty-odd aggregate writes did not, so every one of
    /// them was silently last-write-wins.</para>
    ///
    /// <para>Applied only when exactly ONE versioned root is being modified. A request that touches
    /// two would otherwise have one caller-supplied version stamped onto both, which is worse than
    /// no guard: it would fail the write that was never contended. That case does not arise today
    /// and is asserted by a test rather than assumed.</para>
    /// </summary>
    public void ApplyExpectedVersion(uint expected)
    {
        // T-030: TOUCHED, not Modified. This used to look only for a Modified root, which meant a
        // request that changed a child stamped nothing - the root was still Unchanged at this point,
        // because the bump happens inside SaveChangesAsync. A correct If-Match on any child-write
        // route was therefore ignored, which is the defect T-030 records.
        var roots = TouchedVersionedRoots();

        if (roots.Count != 1) return;

        // OriginalValue is what lands in the UPDATE's WHERE clause. CurrentValue is left alone here
        // and advanced by the bump, so the statement reads
        // SET RowVersion = current + 1 WHERE RowVersion = expected - and a stale caller matches no
        // row, which surfaces as the DbUpdateConcurrencyException §8.1 turns into a 412.
        roots[0].Property(nameof(IVersionedAggregate.RowVersion)).OriginalValue = expected;
    }

    /// <summary>
    /// Every versioned root this change set writes, whether directly or through a child. One place,
    /// because ApplyExpectedVersion's "exactly one" precondition and the bump have to agree on what
    /// counts as touched - if they disagree, a request either guards a root it does not advance or
    /// advances one it does not guard.
    /// </summary>
    private List<EntityEntry> TouchedVersionedRoots()
    {
        // Keyed on the tracked ENTITY, with reference equality - not on EntityEntry.
        //
        // EF hands out a fresh EntityEntry wrapper each time you ask, so a HashSet<EntityEntry> does not
        // deduplicate: the same Supplier arrives once as a Modified root and again as the principal of a
        // changed child, and the set holds both. That made roots.Count == 2, which made
        // ApplyExpectedVersion bail on its "exactly one root" precondition and apply NO guard - so a stale
        // If-Match was accepted and the write went through. Latent until T-030 split (3), because before
        // it no child-write route declared If-Match, so no request reached here with both.
        //
        // The bump had the same problem in a quieter form: two wrappers meant the version advanced by two.
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
                // An Added root has no prior version to guard or advance - it starts at the default.
                if (entry.State != EntityState.Added) Add(entry);
                continue;
            }

            // Attributed to its root - unless that root is itself being INSERTED. A brand-new
            // aggregate saved together with its children has no prior version to guard and no row to
            // update; forcing it Modified made EF emit an UPDATE against a row that did not exist
            // yet, which is how registration started answering 500.
            if (PrincipalRootOf(entry) is { State: not EntityState.Added } principal) Add(principal);
        }

        return [.. roots];
    }

    /// <summary>How many versioned roots the current change set would write. Exposed so a test can
    /// assert the "exactly one" precondition above rather than trusting it.</summary>
    public int ModifiedVersionedRootCount() =>
        ChangeTracker.Entries().Count(e => e.State == EntityState.Modified && e.Entity is IVersionedAggregate);

    /// <summary>
    /// T-030/D-15: advances every versioned root this change set touches, including the ones touched
    /// only through a CHILD.
    ///
    /// <para><b>The defect this closes.</b> The version used to be Postgres <c>xmin</c>, which moves
    /// only when the root ROW is written. A child insert marks the CHILD <c>Added</c> and leaves the
    /// root <c>Unchanged</c>, so no UPDATE was emitted against the root, its xmin never advanced, and
    /// <c>ApplyExpectedVersion</c> - which only looked at <c>Modified</c> roots - found nothing to
    /// stamp. The result: on any route that only touches children, a correct <c>If-Match</c> was
    /// silently ignored and two callers editing different children of one aggregate both won.</para>
    ///
    /// <para><b>One level, deliberately.</b> A changed entity is attributed to a root by walking its
    /// foreign keys to a principal that is a versioned root and is tracked in this same context. Every
    /// aggregate in this codebase is one level deep - Rfq/RfqItem, Supplier/Address,
    /// Proposal/ProposalItem - and a grandchild would need the walk to recurse. Rather than write a
    /// general graph walk for a shape that does not exist, this stops at one hop and
    /// <c>UnattributedChildCount</c> makes the assumption checkable from a test instead of hoping.</para>
    ///
    /// <para>Marking an otherwise-unchanged root <c>Modified</c> is what makes the guard fire: EF then
    /// emits <c>UPDATE … WHERE RowVersion = @original</c>, and a stale caller gets zero rows affected
    /// and a <c>DbUpdateConcurrencyException</c>, which the pipeline already turns into §8.1's 412.</para>
    /// </summary>
    /// <summary>
    /// T-031: stamps <c>StateChangedAt</c> on every tracked aggregate whose state property actually
    /// changed in this unit of work.
    ///
    /// <para><b>Actually changed</b> is the whole of it. A handler that re-assigns the same state -
    /// re-submitting an already-submitted proposal, a no-op transition guarded elsewhere - has not
    /// moved anything, and stamping it would make a queue report a fresh arrival for a row that has
    /// been waiting a fortnight. EF knows the original value, so this asks rather than assumes.</para>
    ///
    /// <para>An ADDED aggregate is stamped too: it has just entered its first state, and a null there
    /// would mean "unknown", which is reserved for rows that predate the column.</para>
    /// </summary>
    private void StampStateChanges()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not IStateTimestamped) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            // The property name is the aggregate's own declaration - see IStateTimestamped - read
            // through the model rather than hard-coded here, so a second aggregate whose state lives
            // under a different name needs no change in this file.
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
            // A DELETED root is not bumped, and must not be: forcing State = Modified on it turns the
            // DELETE into an UPDATE, so the row survives and the caller is told it was removed. Found
            // by T-061's revert - the override row came back after every delete. A deleted row has no
            // next version to advance, while it still WANTS the guard ApplyExpectedVersion put on it,
            // which is why Deleted stays in TouchedVersionedRoots and is skipped only here.
            if (root.State is EntityState.Deleted) continue;

            root.State = EntityState.Modified;
            var property = root.Property(nameof(IVersionedAggregate.RowVersion));
            property.CurrentValue = unchecked((uint)property.CurrentValue! + 1);

            // T-003. A root that records when it last changed is stamped HERE, in the same statement
            // block that advances its version - the two facts describe one event, and writing them
            // apart is how they come to disagree. Roots that do not declare ILastModified are
            // untouched, so this costs nothing until one does.
            if (root.Entity is ILastModified)
            {
                root.Property(nameof(ILastModified.UpdatedAt)).CurrentValue = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>The tracked versioned root this entity hangs off, or null when it is not a child of
    /// one. Null is the ordinary answer for a reference-data row or an aggregate with no version.</summary>
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

            // Local only. A principal that is not already tracked is not being written in this unit of
            // work, so there is nothing to bump and nothing to guard - and loading it here to bump it
            // would turn a save into a query.
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

    /// <summary>
    /// Changed entities that are neither a versioned root nor attributable to one. Exposed so a test
    /// can assert what the one-hop walk above cannot see, rather than leaving the limitation as a
    /// comment nobody checks.
    /// </summary>
    public IReadOnlyList<string> UnattributedChildTypes() =>
        [.. ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not IVersionedAggregate && PrincipalRootOf(e) is null)
            .Select(e => e.Metadata.ClrType.Name)
            .Distinct()
            .Order()];

    /// <summary>
    /// T-030: the bump runs here, on the overloads every other entry point funnels into.
    ///
    /// <para><b>Overriding <c>SaveChangesAsync(CancellationToken)</c> alone was not enough, and that
    /// was a real hole rather than a style point.</b> EF's public surface has four ways in - sync and
    /// async, each with and without <c>acceptAllChangesOnSuccess</c> - and the two convenience forms
    /// delegate to these two. A caller using <c>SaveChanges()</c> synchronously, or the
    /// <c>acceptAllChangesOnSuccess</c> overload, would have skipped the version bump entirely: the
    /// write would land and the aggregate's version would not move, which is precisely the defect
    /// T-030 exists to close. A concurrency scheme that applies on three paths out of four is worse
    /// than none, because it looks like it works.</para>
    ///
    /// <para><c>ExecuteUpdateAsync</c> and <c>ExecuteDeleteAsync</c> still bypass this by design -
    /// they issue SQL without a change tracker. Nothing in this codebase uses them to mutate an
    /// aggregate that an <c>If-Match</c> guards; they are used for test setup and for the GC jobs.</para>
    /// </summary>
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
