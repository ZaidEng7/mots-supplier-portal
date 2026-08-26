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
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Representative> Representatives => Set<Representative>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Domain.ReferenceData.DocumentType> DocumentTypes => Set<Domain.ReferenceData.DocumentType>();
    public DbSet<SupplierDocument> SupplierDocuments => Set<SupplierDocument>();
    public DbSet<SupplierReviewAnnotation> SupplierReviewAnnotations => Set<SupplierReviewAnnotation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
            entity.Property(s => s.RegistrationNumber).HasMaxLength(100);
            entity.Property(s => s.TaxId).HasMaxLength(100);
            entity.Property(s => s.AddressLine).HasMaxLength(300);
            entity.Property(s => s.City).HasMaxLength(100);
            entity.Property(s => s.Country).HasMaxLength(100);
            entity.Property(s => s.CurrencyCode).HasMaxLength(3);
            entity.Property(s => s.OnboardingState).HasConversion<string>().HasMaxLength(30);
            entity.Property(s => s.LifecycleState).HasConversion<string>().HasMaxLength(30);
            entity.Property(s => s.RowVersion).IsRowVersion();
            entity.HasIndex(s => s.OnboardingState);
            entity.HasMany(s => s.Representatives).WithOne().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Representative>(entity =>
        {
            entity.ToTable("representative", "supplier");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.FullName).HasMaxLength(200).IsRequired();
            entity.Property(r => r.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(r => r.SupplierId).HasFilter("\"IsPrimary\" = true").IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("user_session", "identity");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TokenHash).HasMaxLength(200).IsRequired();
            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => t.FamilyId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_log", "ops");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.ActorKind).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.AggregateType).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(100).IsRequired();
            entity.HasIndex(a => new { a.AggregateType, a.AggregateId, a.OccurredAt });
            entity.HasIndex(a => new { a.ActorUserId, a.OccurredAt });
            entity.HasIndex(a => a.CorrelationId);
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
