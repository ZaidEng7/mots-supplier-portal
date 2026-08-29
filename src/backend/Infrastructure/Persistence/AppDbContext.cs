using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Domain.ReferenceData.Region> Regions => Set<Domain.ReferenceData.Region>();
    public DbSet<Domain.ReferenceData.Category> Categories => Set<Domain.ReferenceData.Category>();
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
    public DbSet<Domain.Configuration.SupplierFieldConfig> SupplierFieldConfigs => Set<Domain.Configuration.SupplierFieldConfig>();
    public DbSet<ReferenceCodeCounter> ReferenceCodeCounters => Set<ReferenceCodeCounter>();

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
            entity.ToTable("app_user", "identity");
            entity.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            entity.HasIndex(u => u.SupplierId);
            entity.HasIndex(u => u.OrganizationId);
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
            entity.Property(s => s.RowVersion).IsRowVersion();
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

        modelBuilder.Entity<Domain.Configuration.SupplierFieldConfig>(entity =>
        {
            entity.ToTable("supplier_field_config", "ops");
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
                new Domain.Configuration.SupplierFieldConfig { Id = Guid.Parse("00000000-0000-0000-0000-000000000416"), Category = Domain.Configuration.FieldConfigCategory.LegalInfoRequired, FieldCode = "establishedOn", IsEnabled = false }
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

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_message", "ops");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Type).HasMaxLength(200).IsRequired();
            entity.Property(o => o.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.Property(o => o.SyncStatus).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(o => o.SyncStatus);
        });
    }
}
