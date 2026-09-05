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
    public DbSet<Notification> Notifications => Set<Notification>();
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
        var roots = new HashSet<EntityEntry>();

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is IVersionedAggregate)
            {
                // An Added root has no prior version to guard or advance - it starts at the default.
                if (entry.State != EntityState.Added) roots.Add(entry);
                continue;
            }

            // Attributed to its root - unless that root is itself being INSERTED. A brand-new
            // aggregate saved together with its children has no prior version to guard and no row to
            // update; forcing it Modified made EF emit an UPDATE against a row that did not exist
            // yet, which is how registration started answering 500.
            if (PrincipalRootOf(entry) is { State: not EntityState.Added } principal) roots.Add(principal);
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
        BumpTouchedVersionedRoots();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpTouchedVersionedRoots();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ReferenceCodeCounter>(entity =>
        {
            entity.ToTable("reference_code_counter", "supplier");
            // The prefix is the natural key; there is no surrogate id because a second row for the
            // same prefix would be a second, competing allocator.
            entity.HasKey(c => c.Prefix);
            entity.Property(c => c.Prefix).HasMaxLength(30);
            entity.Property(c => c.LastValue).IsRequired();
        });

        modelBuilder.Entity<Currency>(entity =>
        {
            entity.ToTable("currencies", "reference");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Code).HasMaxLength(3).IsRequired();
            entity.HasIndex(c => c.Code).IsUnique();
            entity.Property(c => c.NameAr).HasMaxLength(100).IsRequired();
            entity.Property(c => c.NameEn).HasMaxLength(100).IsRequired();

            entity.HasData(
                new Currency { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Code = "SYP", NameAr = "ليرة سورية", NameEn = "Syrian Pound" },
                new Currency { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar" }
            );
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_user", "identity", t =>
            {
                // Task #7/Stage B: SupplierId XOR OrganizationId XOR neither (AppUser.cs's own
                // doc comment, previously convention-only). "Neither" (platform admin) stays
                // allowed - this is NAND (at most one set), not strict XOR. Every existing row
                // has OrganizationId = null today (re-verified against real local data before
                // writing this, not trusted from Stage A's report: 42 users, 0 with
                // OrganizationId, 39 with SupplierId, 3 with neither), so this is a clean
                // additive constraint with nothing to reconcile.
                t.HasCheckConstraint("CK_app_user_supplier_xor_organization", "\"SupplierId\" IS NULL OR \"OrganizationId\" IS NULL");
            });
            entity.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            entity.HasIndex(u => u.SupplierId);
            entity.HasIndex(u => u.OrganizationId);
            entity.HasIndex(u => u.OrgUnitId);
            // SetNull, not Cascade/Restrict: deleting an Organization is not yet a real flow
            // (Stage C+), but when it becomes one, a back-office user losing their org
            // assignment is the right default - not being silently deleted along with the
            // Organization row.
            entity.HasOne<Organization>().WithMany().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<OrgUnit>().WithMany().HasForeignKey(u => u.OrgUnitId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("role", "identity");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_role", "identity");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claim", "identity");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_login", "identity");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claim", "identity");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_token", "identity");

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.ToTable("supplier", "supplier");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.ReferenceCode).HasMaxLength(30).IsRequired();
            entity.HasIndex(s => s.ReferenceCode).IsUnique();
            entity.Property(s => s.DisplayNameAr).HasMaxLength(200).IsRequired();
            entity.Property(s => s.DisplayNameEn).HasMaxLength(200).IsRequired();
            entity.Property(s => s.Description).HasMaxLength(2000);
            entity.Property(s => s.Website).HasMaxLength(300);
            entity.Property(s => s.LogoStorageKey).HasMaxLength(500);
            entity.Property(s => s.SupplierGroup).HasMaxLength(100);
            entity.Property(s => s.CurrencyCode).HasMaxLength(3);
            entity.Property(s => s.ExternalId).HasMaxLength(100);
            entity.Property(s => s.SyncStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(s => s.TermsAcceptedVersion).HasMaxLength(20);
            entity.Property(s => s.OnboardingState).HasConversion<string>().HasMaxLength(30);
            entity.Property(s => s.LifecycleState).HasConversion<string>().HasMaxLength(30);
            entity.Property(s => s.RowVersion).IsAppManagedVersion();
            entity.HasIndex(s => s.OnboardingState);
            entity.HasMany(s => s.Representatives).WithOne().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(s => s.Addresses).WithOne().HasForeignKey(a => a.SupplierId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(s => s.Contacts).WithOne().HasForeignKey(c => c.SupplierId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(s => s.Branches).WithOne().HasForeignKey(b => b.SupplierId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(s => s.BankAccounts).WithOne().HasForeignKey(b => b.SupplierId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(s => s.CategoryLinks).WithOne().HasForeignKey(l => l.SupplierId).OnDelete(DeleteBehavior.Cascade);

            entity.OwnsOne(s => s.LegalInfo, legal =>
            {
                // No explicit ToTable() here: owned types default to the owner's table already.
                // Calling ToTable() with the SAME name turns this into an explicit table-splitting
                // fragment, which makes EF emit a second UPDATE against this row (checked against
                // the same xmin concurrency token) whenever the Supplier aggregate is saved together
                // with an unrelated child-collection change (e.g. AddAddress/AddContact) - the second
                // UPDATE then finds the row's xmin already bumped by the first and throws
                // DbUpdateConcurrencyException with 0 rows affected.
                legal.Property(l => l.LegalNameAr).HasColumnName("LegalNameAr").HasMaxLength(200);
                legal.Property(l => l.LegalNameEn).HasColumnName("LegalNameEn").HasMaxLength(200);
                legal.Property(l => l.RegistrationNumber).HasColumnName("RegistrationNumber").HasMaxLength(100);
                legal.Property(l => l.TaxId).HasColumnName("TaxId").HasMaxLength(100);
                legal.Property(l => l.SupplierType).HasColumnName("SupplierType").HasConversion<string>().HasMaxLength(20);
                legal.Property(l => l.EstablishedOn).HasColumnName("EstablishedOn");
            });
            entity.Navigation(s => s.LegalInfo).IsRequired(false);
        });

        modelBuilder.Entity<Representative>(entity =>
        {
            entity.ToTable("representative", "supplier");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.FullName).HasMaxLength(200).IsRequired();
            entity.Property(r => r.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(r => r.SupplierId).HasFilter("\"IsPrimary\" = true").IsUnique();
        });

        modelBuilder.Entity<Address>(entity =>
        {
            entity.ToTable("address", "supplier");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.Line1).HasMaxLength(300).IsRequired();
            entity.Property(a => a.Line2).HasMaxLength(300);
            entity.Property(a => a.City).HasMaxLength(100).IsRequired();
            entity.Property(a => a.RegionCode).HasMaxLength(20).IsRequired();
            entity.Property(a => a.Country).HasMaxLength(100).IsRequired();
            entity.Property(a => a.PostalCode).HasMaxLength(20);
            entity.HasIndex(a => a.SupplierId);
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("contact", "supplier");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.FullName).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(c => c.SupplierId);
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("branch", "supplier");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(b => b.NameEn).HasMaxLength(200).IsRequired();
            entity.HasIndex(b => b.SupplierId);
        });

        modelBuilder.Entity<BankAccount>(entity =>
        {
            entity.ToTable("bank_account", "supplier");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.AccountHolderName).HasMaxLength(200).IsRequired();
            entity.Property(b => b.BankName).HasMaxLength(200).IsRequired();
            entity.Property(b => b.BranchName).HasMaxLength(200);
            entity.Property(b => b.EncryptedAccountNumber).IsRequired();
            entity.Property(b => b.MaskedAccountNumber).HasMaxLength(50).IsRequired();
            entity.Property(b => b.SwiftBic).HasMaxLength(20);
            entity.Property(b => b.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.HasIndex(b => b.SupplierId);
        });

        modelBuilder.Entity<CategoryLink>(entity =>
        {
            entity.ToTable("category_link", "supplier");
            entity.HasKey(l => l.Id);
            entity.Property(l => l.CategoryCode).HasMaxLength(50).IsRequired();
            entity.HasIndex(l => new { l.SupplierId, l.CategoryCode }).IsUnique();
        });

        // Task #7/Stage A: data model only - see Organization.cs's own doc comment. No FK from
        // AppUser or Supplier into these tables yet (Stage B/C).
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("organization", "organization");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.LegalNameAr).HasMaxLength(200).IsRequired();
            entity.Property(o => o.LegalNameEn).HasMaxLength(200).IsRequired();
            entity.Property(o => o.OrganizationType).HasConversion<string>().HasMaxLength(20);
            entity.Property(o => o.ContactEmail).HasMaxLength(320);
            entity.Property(o => o.ContactPhone).HasMaxLength(30);
            entity.Property(o => o.ExternalId).HasMaxLength(100);
            entity.Property(o => o.SyncStatus).HasConversion<string>().HasMaxLength(20);
            entity.HasMany(o => o.OrgUnits).WithOne().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrgUnit>(entity =>
        {
            entity.ToTable("org_unit", "organization");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(u => u.OrganizationId);
            // Self-nesting tree (§5.2): a unit's parent must be another unit in the same
            // Organization, never a unit belonging elsewhere - restricted to that same FK target
            // rather than a bare unconstrained Guid, and Restrict (not Cascade) so deleting a
            // parent unit cannot silently cascade-delete its children.
            entity.HasOne<OrgUnit>().WithMany().HasForeignKey(u => u.ParentOrgUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SupplierOrgLink>(entity =>
        {
            entity.ToTable("supplier_org_link", "organization");
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => new { l.SupplierId, l.OrganizationId }).IsUnique();
            entity.HasOne<Supplier>().WithMany().HasForeignKey(l => l.SupplierId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Organization>().WithMany().HasForeignKey(l => l.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Domain.ReferenceData.Region>(entity =>
        {
            entity.ToTable("region", "reference");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Code).HasMaxLength(20).IsRequired();
            entity.HasIndex(r => r.Code).IsUnique();
            entity.Property(r => r.NameAr).HasMaxLength(100).IsRequired();
            entity.Property(r => r.NameEn).HasMaxLength(100).IsRequired();

            entity.HasData(
                new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000201"), Code = "DIM", NameAr = "دمشق", NameEn = "Damascus" },
                new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000202"), Code = "ALP", NameAr = "حلب", NameEn = "Aleppo" },
                new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000203"), Code = "LAT", NameAr = "اللاذقية", NameEn = "Latakia" },
                new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000204"), Code = "HOM", NameAr = "حمص", NameEn = "Homs" }
            );
        });

        modelBuilder.Entity<Domain.ReferenceData.Category>(entity =>
        {
            entity.ToTable("category", "reference");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Code).HasMaxLength(50).IsRequired();
            entity.HasIndex(c => c.Code).IsUnique();
            entity.Property(c => c.NameAr).HasMaxLength(150).IsRequired();
            entity.Property(c => c.NameEn).HasMaxLength(150).IsRequired();

            // MSP-54 [ASSUMPTION]: minimal flat interim list, seeded from Discovery's known
            // MOTS supplier categories - superseded by EPIC-21's real buyer Category tree later.
            entity.HasData(
                new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000301"), Code = "accommodation", NameAr = "الإقامة والفنادق", NameEn = "Accommodation & Hotels" },
                new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000302"), Code = "catering", NameAr = "التموين والضيافة", NameEn = "Catering & Hospitality" },
                new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000303"), Code = "transport", NameAr = "النقل والمواصلات", NameEn = "Transport" },
                new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000304"), Code = "tour_operations", NameAr = "تنظيم الرحلات السياحية", NameEn = "Tour Operations" },
                new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000305"), Code = "events", NameAr = "تنظيم الفعاليات", NameEn = "Events & Conferences" },
                new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000306"), Code = "maintenance", NameAr = "الصيانة والخدمات الفنية", NameEn = "Maintenance & Technical Services" }
            );
        });

        modelBuilder.Entity<Domain.ReferenceData.UnitOfMeasure>(entity =>
        {
            entity.ToTable("unit_of_measure", "reference");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Code).HasMaxLength(50).IsRequired();
            entity.HasIndex(u => u.Code).IsUnique();
            entity.Property(u => u.NameAr).HasMaxLength(150).IsRequired();
            entity.Property(u => u.NameEn).HasMaxLength(150).IsRequired();

            // FEAT-06.1 [ASSUMPTION]: minimal interim list matching the hospitality/tourism sector
            // Category.cs already seeds (accommodation, catering, transport, tours, events).
            entity.HasData(
                new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000501"), Code = "night", NameAr = "ليلة", NameEn = "Night" },
                new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000502"), Code = "person", NameAr = "شخص", NameEn = "Person" },
                new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000503"), Code = "trip", NameAr = "رحلة", NameEn = "Trip" },
                new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000504"), Code = "hour", NameAr = "ساعة", NameEn = "Hour" },
                new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000505"), Code = "day", NameAr = "يوم", NameEn = "Day" },
                new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000506"), Code = "unit", NameAr = "وحدة", NameEn = "Unit" },
                new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000507"), Code = "event", NameAr = "فعالية", NameEn = "Event" }
            );
        });

        modelBuilder.Entity<Offering>(entity =>
        {
            entity.ToTable("offering", "supplier");
            entity.Property(o => o.RowVersion).IsAppManagedVersion();
            entity.HasKey(o => o.Id);
            entity.Property(o => o.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(o => o.NameEn).HasMaxLength(200).IsRequired();
            entity.Property(o => o.Description).HasMaxLength(2000);
            entity.Property(o => o.CategoryCode).HasMaxLength(50).IsRequired();
            entity.Property(o => o.UnitOfMeasureCode).HasMaxLength(50).IsRequired();
            entity.Property(o => o.PriceAmount).HasPrecision(18, 2);
            entity.Property(o => o.CurrencyCode).HasMaxLength(10);
            entity.Property(o => o.AttributesJson).HasColumnType("jsonb");
            entity.HasIndex(o => o.SupplierId);
        });

        modelBuilder.Entity<Domain.Idempotency.IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_record", "ops");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Key).HasMaxLength(200).IsRequired();
            entity.Property(r => r.RequestFingerprint).HasMaxLength(64).IsRequired();
            // TEXT, not jsonb. §8.2.3 requires the stored response to be "replayed verbatim", and
            // jsonb normalises: it reorders keys and re-spaces the document, so a replay came back
            // byte-different from the original even though the data was identical. A client comparing
            // responses, or hashing one, would see two different answers to the same request.
            entity.Property(r => r.ResponseBody).HasColumnType("text");

            // The UNIQUE constraint is the reservation. Two concurrent retries of the same submission
            // both try to insert, and Postgres lets exactly one through - the loser gets a duplicate-key
            // violation, which is how the second click is refused without a lock or a read-then-write
            // race. Scoped by UserId so a client-generated key cannot collide across callers.
            entity.HasIndex(r => new { r.UserId, r.Key }).IsUnique();

            // The GC job scans by expiry.
            entity.HasIndex(r => r.ExpiresAt);
        });

        modelBuilder.Entity<Domain.Notifications.NotificationTemplate>(entity =>
        {
            entity.ToTable("notification_template", "ops");
            entity.Property(t => t.RowVersion).IsAppManagedVersion();
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Type).HasMaxLength(100).IsRequired();
            entity.Property(t => t.TitleAr).HasMaxLength(300).IsRequired();
            entity.Property(t => t.TitleEn).HasMaxLength(300).IsRequired();
            entity.Property(t => t.BodyAr).HasMaxLength(1000).IsRequired();
            entity.Property(t => t.BodyEn).HasMaxLength(1000).IsRequired();
            entity.HasIndex(t => t.Type).IsUnique();

            // NOT seeded, for the same reason system_setting is not: an absent row means the shipped
            // catalogue is in force, and no deployment's wording changes until somebody changes it.
        });

        modelBuilder.Entity<Domain.Configuration.SystemSetting>(entity =>
        {
            entity.ToTable("system_setting", "ops");
            entity.Property(s => s.RowVersion).IsAppManagedVersion();
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Key).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Value).HasMaxLength(500).IsRequired();
            entity.HasIndex(s => s.Key).IsUnique();

            // NOT seeded, deliberately. An absent row means "nobody has decided", and every consumer
            // falls back to configuration and then to the definition's default - so an environment
            // that never opens the settings screen behaves exactly as it did before this table
            // existed. Seeding the defaults would erase that distinction and turn "unset" into "an
            // administrator chose 30", which is the fact the audit trail is supposed to carry.
        });

        modelBuilder.Entity<Domain.Configuration.SupplierFieldConfig>(entity =>
        {
            entity.ToTable("supplier_field_config", "ops");
            entity.Property(c => c.RowVersion).IsAppManagedVersion();
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Category).HasMaxLength(50).IsRequired();
            entity.Property(c => c.FieldCode).HasMaxLength(50).IsRequired();
            entity.HasIndex(c => new { c.Category, c.FieldCode }).IsUnique();

            // FEAT-04.9/FEAT-04.2 [ASSUMPTION 2026-08-27]: seeded to reproduce exactly the
            // previously-hardcoded behavior - editable via admin endpoints thereafter.
            entity.HasData(
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000401"), Category = Domain.Configuration.FieldConfigCategory.ComplianceRetrigger, FieldCode = "legalInfo", IsEnabled = true },
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000402"), Category = Domain.Configuration.FieldConfigCategory.ComplianceRetrigger, FieldCode = "bankAccount", IsEnabled = true },
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000403"), Category = Domain.Configuration.FieldConfigCategory.ComplianceRetrigger, FieldCode = "categoryLink", IsEnabled = true },
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000411"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "legalNameAr", IsEnabled = true },
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000412"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "legalNameEn", IsEnabled = true },
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000413"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "registrationNumber", IsEnabled = false },
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000414"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "taxId", IsEnabled = false },
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000415"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "supplierType", IsEnabled = false },
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000416"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "establishedOn", IsEnabled = false },
                // D-6/BRULE-087: the Ministry's commercial-visibility flag, seeded OFF. BRULE-087
                // names aggregate-only as the default and tags the question itself as
                // [REQUIRES BUSINESS CONFIRMATION], so MOT Legal's answer flips this row.
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000421"), Category = Domain.Configuration.FieldConfigCategory.GovernanceVisibility, FieldCode = "commercialValues", IsEnabled = false }
            );
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("user_session", "identity");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TokenHash).HasMaxLength(200).IsRequired();
            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => t.FamilyId);
        });

        modelBuilder.Entity<SecurityToken>(entity =>
        {
            entity.ToTable("security_token", "identity");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.UserId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_log", "ops");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.ActorKind).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.AggregateType).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Changes).HasColumnType("jsonb");
            entity.HasIndex(a => new { a.AggregateType, a.AggregateId, a.OccurredAt });
            entity.HasIndex(a => new { a.ActorUserId, a.OccurredAt });
            entity.HasIndex(a => a.CorrelationId);
            // Supports the keyset scan on the own-trail read (MSP-66). Without an index matching the
            // (OccurredAt, Id) sort, keyset paging still returns correct rows but degrades at depth
            // exactly like the OFFSET it replaced - the cost it exists to avoid.
            entity.HasIndex(a => new { a.OccurredAt, a.Id });
            // MSP-75/FR-AUD-004: the entity, actor, and date-range filters above were already
            // covered by the three indexes above this one - checked before assuming a gap existed,
            // per the earlier audit finding's own claim that the indexes "exist, unused". Action was
            // the one dimension with no matching index; an action-only or action+date filter on this
            // table would otherwise be a full scan of a table that grows forever by design.
            entity.HasIndex(a => new { a.Action, a.OccurredAt });
        });

        modelBuilder.Entity<Domain.ReferenceData.DocumentType>(entity =>
        {
            entity.ToTable("document_type", "reference");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Code).HasMaxLength(50).IsRequired();
            entity.HasIndex(d => d.Code).IsUnique();
            entity.Property(d => d.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(d => d.NameEn).HasMaxLength(200).IsRequired();

            // Generic types only - no invented Syrian-specific document rules (FR-REG-006 pattern).
            entity.HasData(
                new Domain.ReferenceData.DocumentType
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000101"),
                    Code = "commercial_registration",
                    NameAr = "السجل التجاري",
                    NameEn = "Commercial Registration",
                    IsRequired = true,
                    ExpiryTracked = false,
                },
                new Domain.ReferenceData.DocumentType
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000102"),
                    Code = "tax_certificate",
                    NameAr = "الشهادة الضريبية",
                    NameEn = "Tax Certificate",
                    IsRequired = true,
                    ExpiryTracked = true,
                },
                new Domain.ReferenceData.DocumentType
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000103"),
                    Code = "chamber_membership",
                    NameAr = "عضوية الغرفة التجارية",
                    NameEn = "Chamber of Commerce Membership",
                    IsRequired = false,
                    ExpiryTracked = true,
                }
            );
        });

        modelBuilder.Entity<SupplierDocument>(entity =>
        {
            entity.ToTable("supplier_document", "supplier");
            // T-010: the public identifier. Unique in the DATABASE, not merely in the generator - the
            // generator is atomic (MSP-81) but a unique index is what makes a collision impossible rather
            // than unlikely, and it is what every other reference code in this schema already has.
            entity.Property(d => d.ReferenceCode).HasMaxLength(30).IsRequired();
            entity.HasIndex(d => d.ReferenceCode).IsUnique();
            entity.HasKey(d => d.Id);
            entity.Property(d => d.State).HasConversion<string>().HasMaxLength(20);
            entity.Property(d => d.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(d => d.OriginalFileName).HasMaxLength(300).IsRequired();
            entity.Property(d => d.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(d => d.RejectReason).HasMaxLength(1000);
            entity.HasIndex(d => new { d.SupplierId, d.DocumentTypeId, d.IsLatestVersion });
            entity.HasOne<Supplier>().WithMany().HasForeignKey(d => d.SupplierId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Domain.ReferenceData.DocumentType>().WithMany().HasForeignKey(d => d.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentExpiryReminder>(entity =>
        {
            entity.ToTable("document_expiry_reminder", "supplier");
            entity.HasKey(r => r.Id);

            // The unique index IS the de-duplication rule. Checking in C# and inserting afterwards
            // leaves a window between the read and the write, and this job can legitimately run
            // concurrently with itself (a retry overlapping a scheduled run). A duplicate insert
            // must fail at the database, not merely be unlikely.
            entity.HasIndex(r => new { r.SupplierDocumentId, r.DocumentVersion, r.ThresholdDays })
                .IsUnique();

            entity.HasOne<SupplierDocument>().WithMany()
                .HasForeignKey(r => r.SupplierDocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SupplierReviewAnnotation>(entity =>
        {
            entity.ToTable("supplier_review_annotation", "supplier");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Reason).HasMaxLength(2000).IsRequired();
            entity.Property(a => a.FlaggedProfileFields).HasColumnType("text[]");
            entity.Property(a => a.FlaggedDocumentTypeIds).HasColumnType("uuid[]");
            entity.HasIndex(a => new { a.SupplierId, a.ResolvedAt });
        });

        // EPIC-15/T3-14. DATABASE-MODEL.md §2.7 specifies this table in full; it is transcribed
        // rather than designed, including the two things that carry the guarantees:
        //   U(dedupe_key)              - the idempotency guarantee, so the same event delivered
        //                                twice produces one row rather than two
        //   IX(recipient_user_id, read_at) - the bell's own query (unread for this user), which is
        //                                on every page of the app for every authenticated persona
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notification", "shared");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Type).HasMaxLength(200).IsRequired();
            entity.Property(n => n.Channel).HasConversion<string>().HasMaxLength(20);
            entity.Property(n => n.DeliveryStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(n => n.TitleAr).HasMaxLength(300).IsRequired();
            entity.Property(n => n.TitleEn).HasMaxLength(300).IsRequired();
            entity.Property(n => n.BodyAr).HasMaxLength(2000).IsRequired();
            entity.Property(n => n.BodyEn).HasMaxLength(2000).IsRequired();
            entity.Property(n => n.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
            entity.Property(n => n.DedupeKey).HasMaxLength(400).IsRequired();

            entity.HasIndex(n => n.DedupeKey).IsUnique();
            entity.HasIndex(n => new { n.RecipientUserId, n.ReadAt });

            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(n => n.RecipientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            // xmin, as every other versioned aggregate maps it (§8.1).
            entity.Property(n => n.RowVersion).IsAppManagedVersion();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_message", "ops");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Type).HasMaxLength(200).IsRequired();
            entity.Property(o => o.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.Property(o => o.SyncStatus).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(o => o.SyncStatus);
        });

        // FEAT-11.1, pulled forward for EPIC-07 (docs/architecture/DATABASE-MODEL.md §2.5,
        // schema "evaluation").
        modelBuilder.Entity<EvaluationTemplate>(entity =>
        {
            entity.ToTable("evaluation_template", "evaluation");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(t => t.NameEn).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(t => t.RowVersion).IsAppManagedVersion();
            // One version-row per (FamilyId, Version) - see EvaluationTemplate.cs's own doc
            // comment on why each version is its own row rather than one row mutating in place.
            entity.HasIndex(t => new { t.FamilyId, t.Version }).IsUnique();
            entity.HasMany(t => t.Criteria).WithOne().HasForeignKey(c => c.EvaluationTemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Criterion>(entity =>
        {
            entity.ToTable("criterion", "evaluation");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Dimension).HasConversion<string>().HasMaxLength(20);
            entity.Property(c => c.ScoringType).HasConversion<string>().HasMaxLength(20);
            entity.Property(c => c.Weight).HasPrecision(5, 2);
            entity.Property(c => c.MaxScore).HasPrecision(6, 2);
            entity.Property(c => c.Threshold).HasPrecision(6, 2);
            entity.Property(c => c.GuidanceAr).HasMaxLength(1000);
            entity.Property(c => c.GuidanceEn).HasMaxLength(1000);
            entity.HasIndex(c => c.EvaluationTemplateId);
        });

        // FEAT-07.1..07.10 (docs/architecture/DATABASE-MODEL.md §2.3, schema "rfq").
        modelBuilder.Entity<Rfq>(entity =>
        {
            entity.ToTable("rfq", "rfq");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.ReferenceCode).HasMaxLength(30).IsRequired();
            entity.HasIndex(r => r.ReferenceCode).IsUnique();
            entity.Property(r => r.TitleAr).HasMaxLength(300).IsRequired();
            entity.Property(r => r.TitleEn).HasMaxLength(300).IsRequired();
            entity.Property(r => r.DescriptionAr).HasMaxLength(4000);
            entity.Property(r => r.DescriptionEn).HasMaxLength(4000);
            entity.Property(r => r.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(r => r.State).HasConversion<string>().HasMaxLength(20);
            entity.Property(r => r.EvaluationTemplateSnapshotJson).HasColumnType("jsonb");
            entity.Property(r => r.CancelReason).HasMaxLength(2000);
            entity.Property(r => r.RowVersion).IsAppManagedVersion();
            entity.HasIndex(r => new { r.OrganizationId, r.State });
            entity.HasIndex(r => r.State);
            entity.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.RfqId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(r => r.Requirements).WithOne().HasForeignKey(q => q.RfqId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(r => r.Attachments).WithOne().HasForeignKey(a => a.RfqId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(r => r.Approvals).WithOne().HasForeignKey(a => a.RfqId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(r => r.Invitations).WithOne().HasForeignKey(i => i.RfqId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(r => r.Clarifications).WithOne().HasForeignKey(c => c.RfqId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(r => r.Addenda).WithOne().HasForeignKey(a => a.RfqId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RfqItem>(entity =>
        {
            entity.ToTable("rfq_item", "rfq");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.TitleAr).HasMaxLength(300).IsRequired();
            entity.Property(i => i.TitleEn).HasMaxLength(300).IsRequired();
            entity.Property(i => i.SpecificationAr).HasMaxLength(2000);
            entity.Property(i => i.SpecificationEn).HasMaxLength(2000);
            entity.Property(i => i.CategoryCode).HasMaxLength(50).IsRequired();
            entity.Property(i => i.Quantity).HasPrecision(18, 4);
            entity.Property(i => i.UnitOfMeasureCode).HasMaxLength(50).IsRequired();
            entity.HasIndex(i => new { i.RfqId, i.LineNo }).IsUnique();
        });

        modelBuilder.Entity<Requirement>(entity =>
        {
            entity.ToTable("requirement", "rfq");
            entity.HasKey(q => q.Id);
            entity.Property(q => q.TextAr).HasMaxLength(2000).IsRequired();
            entity.Property(q => q.TextEn).HasMaxLength(2000).IsRequired();
            entity.Property(q => q.DocumentTypeCode).HasMaxLength(50);
            entity.HasIndex(q => q.RfqId);
        });

        modelBuilder.Entity<RfqAttachment>(entity =>
        {
            entity.ToTable("rfq_attachment", "rfq");
            entity.Property(a => a.ScanState).HasConversion<string>().HasMaxLength(20);
            entity.HasKey(a => a.Id);
            entity.Property(a => a.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(a => a.OriginalFileName).HasMaxLength(300).IsRequired();
            entity.Property(a => a.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(a => a.Caption).HasMaxLength(500);
            entity.HasIndex(a => a.RfqId);
        });

        // OQ-004 interim (RfqApproval.cs's own doc comment): an ordered, growing array of review
        // passes, not a scalar approver field.
        modelBuilder.Entity<RfqApproval>(entity =>
        {
            entity.ToTable("rfq_approval", "rfq");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Decision).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.Comment).HasMaxLength(2000);
            entity.HasIndex(a => new { a.RfqId, a.StepNo }).IsUnique();
        });

        // FEAT-08.1/DATABASE-MODEL.md §2.4: unique(rfq_id, supplier_id) is DB-enforced, "never
        // left to app-only checks" per that doc's own note - Rfq.InviteSupplier's app-level
        // duplicate check is a fast-fail UX nicety, not the actual invariant guarantee.
        modelBuilder.Entity<Invitation>(entity =>
        {
            entity.ToTable("invitation", "rfq");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(i => i.DeclineReason).HasMaxLength(2000);
            entity.HasIndex(i => new { i.RfqId, i.SupplierId }).IsUnique();
            entity.HasIndex(i => i.SupplierId);
        });

        // FEAT-10.1..10.3/DOMAIN-MODEL.md §5.4: Question is required at construction; Answer starts
        // null until AnswerClarification sets it (see Clarification.cs's own doc comment on why
        // AskedBySupplierId is always stored and only ever hidden at the DTO layer).
        modelBuilder.Entity<Clarification>(entity =>
        {
            entity.ToTable("clarification", "rfq");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Question).HasMaxLength(4000).IsRequired();
            entity.Property(c => c.Answer).HasMaxLength(4000);
            entity.Property(c => c.Visibility).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(c => c.RfqId);
            entity.HasIndex(c => c.AskedBySupplierId);
        });

        // FEAT-10.4/BRULE-038: additive record, never mutates the RFQ's original content - see
        // Addendum.cs's own doc comment.
        modelBuilder.Entity<Addendum>(entity =>
        {
            entity.ToTable("addendum", "rfq");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.TitleAr).HasMaxLength(300).IsRequired();
            entity.Property(a => a.TitleEn).HasMaxLength(300).IsRequired();
            entity.Property(a => a.DescriptionAr).HasMaxLength(4000).IsRequired();
            entity.Property(a => a.DescriptionEn).HasMaxLength(4000).IsRequired();
            entity.HasIndex(a => a.RfqId);
        });

        // FEAT-09.1..09.6/DOMAIN-MODEL.md §5.5: Proposal is its own aggregate root (schema
        // "proposal"), not an Rfq child - DOMAIN-MODEL.md's own "Aggregate: Proposal.ProposalState".
        // unique(rfq_id, supplier_id) is the real uniqueness guarantee (Proposal.Create's own
        // handler-level idempotent-start check is a fast-fail UX nicety on top of it, same pattern
        // as Invitation's own duplicate-invite check).
        modelBuilder.Entity<Proposal>(entity =>
        {
            entity.ToTable("proposal", "proposal");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.ReferenceCode).HasMaxLength(30).IsRequired();
            entity.HasIndex(p => p.ReferenceCode).IsUnique();
            entity.Property(p => p.State).HasConversion<string>().HasMaxLength(30);
            entity.Property(p => p.CurrencyCode).HasMaxLength(3);
            entity.Property(p => p.PaymentTerms).HasMaxLength(500);
            entity.Property(p => p.IncotermCode).HasMaxLength(10);
            entity.Property(p => p.DeliveryTermsAr).HasMaxLength(1000);
            entity.Property(p => p.DeliveryTermsEn).HasMaxLength(1000);
            entity.Property(p => p.Warranty).HasMaxLength(500);
            entity.Property(p => p.NarrativeAr).HasMaxLength(4000);
            entity.Property(p => p.NarrativeEn).HasMaxLength(4000);
            entity.Property(p => p.WithdrawReason).HasMaxLength(2000);
            // T-064: same bound, same kind of value - a supplier's free text explaining a transition.
            entity.Property(p => p.DeclineReason).HasMaxLength(2000);
            // Same bound as WithdrawReason - both are a person's free text explaining a transition.
            entity.Property(p => p.ClarificationReason).HasMaxLength(2000);
            entity.Property(p => p.RowVersion).IsAppManagedVersion();
            // Unique per (rfq, supplier) among proposals that are NOT withdrawn.
            //
            // The unfiltered version made BUSINESS-PROCESSES.md §4.1's re-entry impossible at the
            // database level: "re-submission allowed while window open (new draft)" needs a second
            // row, and the index refused one. Narrowed rather than dropped - the rule being enforced
            // is "one LIVE proposal per supplier per RFQ", which is what uniqueness was always for;
            // a withdrawn proposal is a historical record, not a current bid, and any number of them
            // can accumulate if a supplier withdraws repeatedly within the window.
            entity.HasIndex(p => new { p.RfqId, p.SupplierId })
                .IsUnique()
                // The column name is QUOTED. This project maps to PascalCase columns, and an
                // unquoted `state` folds to lowercase in Postgres and does not exist - the first
                // version of this filter failed every migration with 42703.
                //
                // A-9 added Lapsed and Cancelled, and both belong in this exclusion for the same
                // reason Withdrawn does: they are historical records rather than current bids. A
                // supplier whose draft LAPSED on RFQ-1 must be able to bid again if that RFQ reopens
                // its window, and one whose proposal was CANCELLED with the RFQ must not be blocked
                // from a re-tender. Leaving them in would have made the index refuse the second row
                // and surface as a 500 on a perfectly legitimate submission - which is exactly how
                // the unfiltered version of this index failed the first time.
                .HasFilter("\"State\" NOT IN ('Withdrawn', 'Lapsed', 'Cancelled')");
            entity.HasIndex(p => new { p.SupplierId, p.State });
            entity.HasIndex(p => new { p.RfqId, p.State });
            entity.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.ProposalId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(p => p.Documents).WithOne().HasForeignKey(d => d.ProposalId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(p => p.RequirementAnswers).WithOne().HasForeignKey(a => a.ProposalId).OnDelete(DeleteBehavior.Cascade);
        });

        // FEAT-09.1/FR-PRP-002: the two-envelope FINANCIAL table - deliberately its own table so a
        // query can omit it entirely rather than filtering a shared row (see ProposalItem.cs's own
        // doc comment). LineTotal is a computed property, not a column - never persisted.
        modelBuilder.Entity<ProposalItem>(entity =>
        {
            entity.ToTable("proposal_item", "proposal");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Quantity).HasPrecision(18, 4);
            entity.Property(i => i.UnitPrice).HasPrecision(18, 4);
            entity.Property(i => i.Discount).HasPrecision(18, 4);
            entity.Property(i => i.NotesAr).HasMaxLength(2000);
            entity.Property(i => i.NotesEn).HasMaxLength(2000);
            entity.Ignore(i => i.LineTotal);
            entity.HasIndex(i => new { i.ProposalId, i.RfqItemId }).IsUnique();
        });

        modelBuilder.Entity<ProposalDocument>(entity =>
        {
            entity.ToTable("proposal_document", "proposal");
            entity.Property(a => a.ScanState).HasConversion<string>().HasMaxLength(20);
            entity.Property(d => d.Envelope).HasConversion<string>().HasMaxLength(20);
            entity.HasKey(d => d.Id);
            entity.Property(d => d.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(d => d.OriginalFileName).HasMaxLength(300).IsRequired();
            entity.Property(d => d.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(d => d.Caption).HasMaxLength(500);
            entity.HasIndex(d => d.ProposalId);
        });

        modelBuilder.Entity<RequirementAnswer>(entity =>
        {
            entity.ToTable("requirement_answer", "proposal");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.AnswerAr).HasMaxLength(4000).IsRequired();
            entity.Property(a => a.AnswerEn).HasMaxLength(4000).IsRequired();
            entity.HasIndex(a => new { a.ProposalId, a.RequirementId }).IsUnique();
        });

        // EPIC-11/DOMAIN-MODEL.md §5.7: Evaluation is its own aggregate root (schema "evaluation"),
        // bound to RfqId - same "own bounded context, referenced by id" shape as Proposal. One
        // Evaluation per Rfq (unique index on RfqId).
        modelBuilder.Entity<MotsSupplierPortal.Domain.Evaluation.Evaluation>(entity =>
        {
            entity.ToTable("evaluation", "evaluation");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.State).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.RowVersion).IsAppManagedVersion();
            entity.HasIndex(e => e.RfqId).IsUnique();
            entity.HasMany(e => e.Criteria).WithOne().HasForeignKey(c => c.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Assignments).WithOne().HasForeignKey(a => a.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Scores).WithOne().HasForeignKey(s => s.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Results).WithOne().HasForeignKey(r => r.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EvaluationCriterionSnapshot>(entity =>
        {
            entity.ToTable("evaluation_criterion_snapshot", "evaluation");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Dimension).HasConversion<string>().HasMaxLength(20);
            entity.Property(c => c.ScoringType).HasConversion<string>().HasMaxLength(20);
            entity.Property(c => c.Weight).HasPrecision(5, 2);
            entity.Property(c => c.MaxScore).HasPrecision(6, 2);
            entity.Property(c => c.Threshold).HasPrecision(6, 2);
            entity.Ignore(c => c.IsFinancial);
            entity.HasIndex(c => c.EvaluationId);
        });

        modelBuilder.Entity<EvaluationAssignment>(entity =>
        {
            entity.ToTable("evaluation_assignment", "evaluation");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.RecusalReason).HasMaxLength(2000);
            entity.Ignore(a => a.IsActive);
            entity.HasIndex(a => new { a.EvaluationId, a.EvaluatorUserId }).IsUnique();
        });

        // FEAT-11.3/DATABASE-MODEL.md §2.6: unique(EvaluationId, EvaluatorUserId, ProposalId,
        // CriterionId) is the DB-enforced guarantee behind blind independent scoring (OQ-005/
        // BRULE-058) - one row per evaluator per proposal per criterion, never shared.
        modelBuilder.Entity<EvaluatorScore>(entity =>
        {
            entity.ToTable("evaluator_score", "evaluation");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.RawScore).HasPrecision(6, 2);
            entity.Property(s => s.CommentAr).HasMaxLength(2000);
            entity.Property(s => s.CommentEn).HasMaxLength(2000);
            entity.HasIndex(s => new { s.EvaluationId, s.EvaluatorUserId, s.ProposalId, s.CriterionId }).IsUnique();
            entity.HasIndex(s => new { s.EvaluationId, s.EvaluatorUserId });
        });

        modelBuilder.Entity<ConsolidatedResult>(entity =>
        {
            entity.ToTable("consolidated_result", "evaluation");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.TechnicalWeightedScore).HasPrecision(8, 2);
            entity.Property(r => r.FinancialWeightedScore).HasPrecision(8, 2);
            entity.Property(r => r.WeightedTotal).HasPrecision(8, 2);
            entity.HasIndex(r => new { r.EvaluationId, r.ProposalId }).IsUnique();
        });

        // EPIC-14/DOMAIN-MODEL.md §5.8: Award is its own aggregate root (schema "award"), bound to
        // RfqId - same "own bounded context, referenced by id" shape as Proposal/Evaluation. One
        // Award per Rfq (unique index on RfqId).
        modelBuilder.Entity<Award>(entity =>
        {
            entity.ToTable("award", "award");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.State).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.JustificationAr).HasMaxLength(4000).IsRequired();
            entity.Property(a => a.JustificationEn).HasMaxLength(4000).IsRequired();
            entity.Property(a => a.ComparisonSnapshotJson).HasColumnType("jsonb");
            entity.Property(a => a.ErpSyncStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.ExternalPurchaseOrderRef).HasMaxLength(100);
            entity.Property(a => a.RowVersion).IsAppManagedVersion();
            entity.HasIndex(a => a.RfqId).IsUnique();
            entity.HasMany(a => a.Approvals).WithOne().HasForeignKey(p => p.AwardId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Approval>(entity =>
        {
            entity.ToTable("approval", "award");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Decision).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.Comment).HasMaxLength(2000);
            entity.HasIndex(a => new { a.AwardId, a.StepNo });
        });
    }
}
