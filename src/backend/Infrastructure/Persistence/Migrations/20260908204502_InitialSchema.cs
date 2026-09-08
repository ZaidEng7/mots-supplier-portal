using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// P12 item 29: the 59 migrations that built this schema, squashed into the one baseline a fresh
    /// database now applies. Generated from the model, then hand-extended with the two things the model
    /// cannot express - see the end of <c>Up</c>.
    ///
    /// <para><b>What was deliberately NOT carried over.</b> Four of the folded migrations backfilled
    /// existing rows: a <c>CreatedAt</c> repair for rows written as <c>-infinity</c>, the reference-code
    /// counter's two seeds, the RFQ <c>PublishedAt</c> reconstruction from the audit trail, and the
    /// document reference-code assignment. Every one of them updates rows this migration has just created
    /// none of, so replaying them would be theatre. They are gone, and this is where that decision is
    /// recorded.</para>
    ///
    /// <para><b>The trap this squash had to avoid.</b> D-58 marked two document types award-critical in a
    /// data migration (<c>UpdateData</c> against the seeded rows). A squashed baseline seeds that table
    /// from the model instead of replaying history, so the flag had to move into <c>AppDbContext</c>'s
    /// <c>HasData</c> first - otherwise both types would have come back <c>false</c>, BRULE-023 would have
    /// gone back to suspending nobody, and every test that proves the rule fires would still have passed
    /// against a database seeded the old way. <c>AwardCriticalBlocksBiddingTests</c> is what pins it.</para>
    ///
    /// <para><b>What this means for a database that already exists.</b> Its <c>__EFMigrationsHistory</c>
    /// names 59 migrations this project no longer contains, so <c>Migrate()</c> will try to create tables
    /// that are already there. There is one deployed database - the demonstration environment - and the
    /// route forward for it is to drop and re-seed, which is what <c>DevDataSeeder</c> exists for. A
    /// database holding data somebody cares about would instead need its history table rewritten to a
    /// single row naming this migration; nothing here does that automatically, because guessing which case
    /// you are in is how a squash loses data.</para>
    /// </summary>
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rfq");

            migrationBuilder.EnsureSchema(
                name: "supplier");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "award");

            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.EnsureSchema(
                name: "reference");

            migrationBuilder.EnsureSchema(
                name: "evaluation");

            migrationBuilder.EnsureSchema(
                name: "shared");

            migrationBuilder.EnsureSchema(
                name: "organization");

            migrationBuilder.EnsureSchema(
                name: "proposal");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorLabel = table.Column<string>(type: "text", nullable: true),
                    AggregateType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceCode = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FromState = table.Column<string>(type: "text", nullable: true),
                    ToState = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Changes = table.Column<string>(type: "jsonb", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "award",
                schema: "award",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    WinningProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    JustificationAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    JustificationEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RecommendedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecommendationRevision = table.Column<int>(type: "integer", nullable: false),
                    AwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ComparisonSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    ErpSyncStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExternalPurchaseOrderRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErpSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErpRetryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_award", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "category",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "currencies",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "document_type",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiryTracked = table.Column<bool>(type: "boolean", nullable: false),
                    IsAwardCritical = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_type", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "email_template_override",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubjectAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SubjectEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    BodyEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_template_override", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "evaluation",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_template",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsReferenced = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_record",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_record", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_template",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    BodyEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "offering",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UnitOfMeasureCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    AttributesJson = table.Column<string>(type: "jsonb", nullable: true),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', regexp_replace(coalesce(\"NameAr\",'') || ' ' || coalesce(\"NameEn\",'') || ' ' || coalesce(\"Description\",''), '[^[:alnum:]]+', ' ', 'g'))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offering", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organization",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LegalNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrganizationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SyncStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SyncStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_message", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "proposal",
                schema: "proposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PaymentTerms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IncotermCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    DeliveryTermsAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DeliveryTermsEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Warranty = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValidityStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidityEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    NarrativeAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NarrativeEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WithdrawReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AwardOfferedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeclinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeclineReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ClarificationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ClarificationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reference_code_counter",
                schema: "supplier",
                columns: table => new
                {
                    Prefix = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_code_counter", x => x.Prefix);
                });

            migrationBuilder.CreateTable(
                name: "region",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_region", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rfq",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmissionOpensAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmissionClosesAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmissionDeadlineChangeReason = table.Column<string>(type: "text", nullable: true),
                    SubmissionDeadlineChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClarificationDeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvaluationTargetDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvaluationTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvaluationTemplateVersion = table.Column<int>(type: "integer", nullable: true),
                    EvaluationTemplateSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', regexp_replace(coalesce(\"TitleAr\",'') || ' ' || coalesce(\"TitleEn\",'') || ' ' || coalesce(\"ReferenceCode\",''), '[^[:alnum:]]+', ' ', 'g'))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rfq", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "role",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "security_token",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_token", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supplier",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Website = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LogoStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SupplierGroup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    LegalNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LegalNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TaxId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SupplierType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EstablishedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    OnboardingState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SyncStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TermsAcceptedVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TermsAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    AssignedReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', regexp_replace(coalesce(\"DisplayNameAr\",'') || ' ' || coalesce(\"DisplayNameEn\",'') || ' ' || coalesce(\"ReferenceCode\",''), '[^[:alnum:]]+', ' ', 'g'))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_field_config",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FieldCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_field_config", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_review_annotation",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FlaggedProfileFields = table.Column<string[]>(type: "text[]", nullable: false),
                    FlaggedDocumentTypeIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_review_annotation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_setting",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_setting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ui_string_override",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ui_string_override", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "unit_of_measure",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unit_of_measure", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_session",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Ip = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_session", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "approval",
                schema: "award",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepNo = table.Column<int>(type: "integer", nullable: false),
                    ApproverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval", x => x.Id);
                    table.ForeignKey(
                        name: "FK_approval_award_AwardId",
                        column: x => x.AwardId,
                        principalSchema: "award",
                        principalTable: "award",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_type_category",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_type_category", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_type_category_document_type_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalSchema: "reference",
                        principalTable: "document_type",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consolidated_result",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicallyQualified = table.Column<bool>(type: "boolean", nullable: false),
                    TechnicalWeightedScore = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    FinancialWeightedScore = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    WeightedTotal = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: true),
                    TieUnresolved = table.Column<bool>(type: "boolean", nullable: false),
                    TieResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TieResolutionReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consolidated_result", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consolidated_result_evaluation_EvaluationId",
                        column: x => x.EvaluationId,
                        principalSchema: "evaluation",
                        principalTable: "evaluation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_assignment",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecusedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecusalReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConflictDeclaredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_assignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_assignment_evaluation_EvaluationId",
                        column: x => x.EvaluationId,
                        principalSchema: "evaluation",
                        principalTable: "evaluation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_criterion_snapshot",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Dimension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    ScoringType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequiresJustification = table.Column<bool>(type: "boolean", nullable: false),
                    GuidanceAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GuidanceEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_criterion_snapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_criterion_snapshot_evaluation_EvaluationId",
                        column: x => x.EvaluationId,
                        principalSchema: "evaluation",
                        principalTable: "evaluation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluator_score",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    CommentAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CommentEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ScoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluator_score", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluator_score_evaluation_EvaluationId",
                        column: x => x.EvaluationId,
                        principalSchema: "evaluation",
                        principalTable: "evaluation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "criterion",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Dimension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    ScoringType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GuidanceAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GuidanceEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequiresJustification = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_criterion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_criterion_evaluation_template_EvaluationTemplateId",
                        column: x => x.EvaluationTemplateId,
                        principalSchema: "evaluation",
                        principalTable: "evaluation_template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "org_unit",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentOrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_unit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_org_unit_org_unit_ParentOrgUnitId",
                        column: x => x.ParentOrgUnitId,
                        principalSchema: "organization",
                        principalTable: "org_unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_org_unit_organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposal_document",
                schema: "proposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Envelope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScanState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_document", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposal_document_proposal_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "proposal",
                        principalTable: "proposal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposal_item",
                schema: "proposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    NotesAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NotesEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposal_item_proposal_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "proposal",
                        principalTable: "proposal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "requirement_answer",
                schema: "proposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnswerAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AnswerEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_answer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_requirement_answer_proposal_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "proposal",
                        principalTable: "proposal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "addendum",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DescriptionEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_addendum", x => x.Id);
                    table.ForeignKey(
                        name: "FK_addendum_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clarification",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    AskedBySupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Answer = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AskedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clarification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clarification_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invitation",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InvitedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ViewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeclineReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invitation_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "requirement",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TextEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsMandatory = table.Column<bool>(type: "boolean", nullable: false),
                    DocumentTypeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExpectedEnvelope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_requirement_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_approval",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepNo = table.Column<int>(type: "integer", nullable: false),
                    AssignedApproverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApproverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rfq_approval", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rfq_approval_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_attachment",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScanState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rfq_attachment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rfq_attachment_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_item",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SpecificationAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SpecificationEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitOfMeasureCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsUnitPrice = table.Column<bool>(type: "boolean", nullable: false),
                    IsOptional = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rfq_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rfq_item_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_claim",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_claim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_claim_role_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "address",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Line1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Line2 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RegionCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_address", x => x.Id);
                    table.ForeignKey(
                        name: "FK_address_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_account",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BranchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EncryptedAccountNumber = table.Column<byte[]>(type: "bytea", nullable: false),
                    MaskedAccountNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SwiftBic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_account", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bank_account_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "branch",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_branch_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "category_link",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category_link", x => x.Id);
                    table.ForeignKey(
                        name: "FK_category_link_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contact_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "representative",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Position = table.Column<string>(type: "text", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_representative", x => x.Id);
                    table.ForeignKey(
                        name: "FK_representative_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_document",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsLatestVersion = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RejectReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_document", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_document_document_type_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalSchema: "reference",
                        principalTable: "document_type",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_document_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_org_link",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_org_link", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_org_link_organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_org_link_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: false),
                    LanguageChosenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.Id);
                    table.CheckConstraint("CK_app_user_supplier_xor_organization", "\"SupplierId\" IS NULL OR \"OrganizationId\" IS NULL");
                    table.ForeignKey(
                        name: "FK_app_user_org_unit_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalSchema: "organization",
                        principalTable: "org_unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_app_user_organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "document_expiry_reminder",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersion = table.Column<int>(type: "integer", nullable: false),
                    ThresholdDays = table.Column<int>(type: "integer", nullable: false),
                    WasSent = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_expiry_reminder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_expiry_reminder_supplier_document_SupplierDocument~",
                        column: x => x.SupplierDocumentId,
                        principalSchema: "supplier",
                        principalTable: "supplier_document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification",
                schema: "shared",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BodyEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DedupeKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_app_user_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_preference",
                schema: "shared",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_preference_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claim",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_claim_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_login",
                schema: "identity",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_login", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_user_login_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_role",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_role", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_role_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_role_role_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_token",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_token", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_user_token_app_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "reference",
                table: "category",
                columns: new[] { "Id", "Code", "IsActive", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000301"), "accommodation", true, "الإقامة والفنادق", "Accommodation & Hotels" },
                    { new Guid("00000000-0000-0000-0000-000000000302"), "catering", true, "التموين والضيافة", "Catering & Hospitality" },
                    { new Guid("00000000-0000-0000-0000-000000000303"), "transport", true, "النقل والمواصلات", "Transport" },
                    { new Guid("00000000-0000-0000-0000-000000000304"), "tour_operations", true, "تنظيم الرحلات السياحية", "Tour Operations" },
                    { new Guid("00000000-0000-0000-0000-000000000305"), "events", true, "تنظيم الفعاليات", "Events & Conferences" },
                    { new Guid("00000000-0000-0000-0000-000000000306"), "maintenance", true, "الصيانة والخدمات الفنية", "Maintenance & Technical Services" }
                });

            migrationBuilder.InsertData(
                schema: "reference",
                table: "currencies",
                columns: new[] { "Id", "Code", "IsActive", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "SYP", true, "ليرة سورية", "Syrian Pound" },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "USD", true, "دولار أمريكي", "US Dollar" }
                });

            migrationBuilder.InsertData(
                schema: "reference",
                table: "document_type",
                columns: new[] { "Id", "Code", "ExpiryTracked", "IsActive", "IsAwardCritical", "IsRequired", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000101"), "commercial_registration", false, true, true, true, "السجل التجاري", "Commercial Registration" },
                    { new Guid("00000000-0000-0000-0000-000000000102"), "tax_certificate", true, true, true, true, "الشهادة الضريبية", "Tax Certificate" },
                    { new Guid("00000000-0000-0000-0000-000000000103"), "chamber_membership", true, true, false, false, "عضوية الغرفة التجارية", "Chamber of Commerce Membership" }
                });

            migrationBuilder.InsertData(
                schema: "reference",
                table: "region",
                columns: new[] { "Id", "Code", "IsActive", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000201"), "DIM", true, "دمشق", "Damascus" },
                    { new Guid("00000000-0000-0000-0000-000000000202"), "ALP", true, "حلب", "Aleppo" },
                    { new Guid("00000000-0000-0000-0000-000000000203"), "LAT", true, "اللاذقية", "Latakia" },
                    { new Guid("00000000-0000-0000-0000-000000000204"), "HOM", true, "حمص", "Homs" }
                });

            migrationBuilder.InsertData(
                schema: "ops",
                table: "supplier_field_config",
                columns: new[] { "Id", "Category", "FieldCode", "IsEnabled" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000401"), "ComplianceRetrigger", "legalInfo", true },
                    { new Guid("00000000-0000-0000-0000-000000000402"), "ComplianceRetrigger", "bankAccount", true },
                    { new Guid("00000000-0000-0000-0000-000000000403"), "ComplianceRetrigger", "categoryLink", true },
                    { new Guid("00000000-0000-0000-0000-000000000411"), "LegalInfoRequired", "legalNameAr", true },
                    { new Guid("00000000-0000-0000-0000-000000000412"), "LegalInfoRequired", "legalNameEn", true },
                    { new Guid("00000000-0000-0000-0000-000000000413"), "LegalInfoRequired", "registrationNumber", false },
                    { new Guid("00000000-0000-0000-0000-000000000414"), "LegalInfoRequired", "taxId", false },
                    { new Guid("00000000-0000-0000-0000-000000000415"), "LegalInfoRequired", "supplierType", false },
                    { new Guid("00000000-0000-0000-0000-000000000416"), "LegalInfoRequired", "establishedOn", false },
                    { new Guid("00000000-0000-0000-0000-000000000421"), "GovernanceVisibility", "commercialValues", false }
                });

            migrationBuilder.InsertData(
                schema: "reference",
                table: "unit_of_measure",
                columns: new[] { "Id", "Code", "IsActive", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000501"), "night", true, "ليلة", "Night" },
                    { new Guid("00000000-0000-0000-0000-000000000502"), "person", true, "شخص", "Person" },
                    { new Guid("00000000-0000-0000-0000-000000000503"), "trip", true, "رحلة", "Trip" },
                    { new Guid("00000000-0000-0000-0000-000000000504"), "hour", true, "ساعة", "Hour" },
                    { new Guid("00000000-0000-0000-0000-000000000505"), "day", true, "يوم", "Day" },
                    { new Guid("00000000-0000-0000-0000-000000000506"), "unit", true, "وحدة", "Unit" },
                    { new Guid("00000000-0000-0000-0000-000000000507"), "event", true, "فعالية", "Event" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_addendum_RfqId",
                schema: "rfq",
                table: "addendum",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_address_SupplierId",
                schema: "supplier",
                table: "address",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "app_user",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_OrganizationId",
                schema: "identity",
                table: "app_user",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_OrgUnitId",
                schema: "identity",
                table: "app_user",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_SupplierId",
                schema: "identity",
                table: "app_user",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identity",
                table: "app_user",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_AwardId_StepNo",
                schema: "award",
                table: "approval",
                columns: new[] { "AwardId", "StepNo" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_Action_OccurredAt",
                schema: "ops",
                table: "audit_log",
                columns: new[] { "Action", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ActorUserId_OccurredAt",
                schema: "ops",
                table: "audit_log",
                columns: new[] { "ActorUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_AggregateType_AggregateId_OccurredAt",
                schema: "ops",
                table: "audit_log",
                columns: new[] { "AggregateType", "AggregateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_CorrelationId",
                schema: "ops",
                table: "audit_log",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_OccurredAt_Id",
                schema: "ops",
                table: "audit_log",
                columns: new[] { "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_award_RfqId",
                schema: "award",
                table: "award",
                column: "RfqId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_account_SupplierId",
                schema: "supplier",
                table: "bank_account",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_branch_SupplierId",
                schema: "supplier",
                table: "branch",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_category_Code",
                schema: "reference",
                table: "category",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_category_link_SupplierId_CategoryCode",
                schema: "supplier",
                table: "category_link",
                columns: new[] { "SupplierId", "CategoryCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clarification_AskedBySupplierId",
                schema: "rfq",
                table: "clarification",
                column: "AskedBySupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_clarification_RfqId",
                schema: "rfq",
                table: "clarification",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_consolidated_result_EvaluationId_ProposalId",
                schema: "evaluation",
                table: "consolidated_result",
                columns: new[] { "EvaluationId", "ProposalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contact_SupplierId",
                schema: "supplier",
                table: "contact",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_criterion_EvaluationTemplateId",
                schema: "evaluation",
                table: "criterion",
                column: "EvaluationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_currencies_Code",
                schema: "reference",
                table: "currencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_expiry_reminder_SupplierDocumentId_DocumentVersion~",
                schema: "supplier",
                table: "document_expiry_reminder",
                columns: new[] { "SupplierDocumentId", "DocumentVersion", "ThresholdDays" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_type_Code",
                schema: "reference",
                table: "document_type",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_type_category_DocumentTypeId_CategoryCode",
                schema: "reference",
                table: "document_type_category",
                columns: new[] { "DocumentTypeId", "CategoryCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_template_override_Key",
                schema: "ops",
                table: "email_template_override",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_RfqId",
                schema: "evaluation",
                table: "evaluation",
                column: "RfqId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_assignment_EvaluationId_EvaluatorUserId",
                schema: "evaluation",
                table: "evaluation_assignment",
                columns: new[] { "EvaluationId", "EvaluatorUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_criterion_snapshot_EvaluationId",
                schema: "evaluation",
                table: "evaluation_criterion_snapshot",
                column: "EvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_template_FamilyId_Version",
                schema: "evaluation",
                table: "evaluation_template",
                columns: new[] { "FamilyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluator_score_EvaluationId_EvaluatorUserId",
                schema: "evaluation",
                table: "evaluator_score",
                columns: new[] { "EvaluationId", "EvaluatorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_evaluator_score_EvaluationId_EvaluatorUserId_ProposalId_Cri~",
                schema: "evaluation",
                table: "evaluator_score",
                columns: new[] { "EvaluationId", "EvaluatorUserId", "ProposalId", "CriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_record_ExpiresAt",
                schema: "ops",
                table: "idempotency_record",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_record_UserId_Key",
                schema: "ops",
                table: "idempotency_record",
                columns: new[] { "UserId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invitation_RfqId_SupplierId",
                schema: "rfq",
                table: "invitation",
                columns: new[] { "RfqId", "SupplierId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invitation_SupplierId",
                schema: "rfq",
                table: "invitation",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_DedupeKey",
                schema: "shared",
                table: "notification",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_RecipientUserId_ReadAt",
                schema: "shared",
                table: "notification",
                columns: new[] { "RecipientUserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_preference_UserId_NotificationType",
                schema: "shared",
                table: "notification_preference",
                columns: new[] { "UserId", "NotificationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_template_Type",
                schema: "ops",
                table: "notification_template",
                column: "Type",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_offering_SearchVector",
                schema: "supplier",
                table: "offering",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_offering_SupplierId",
                schema: "supplier",
                table: "offering",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_org_unit_OrganizationId",
                schema: "organization",
                table: "org_unit",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_org_unit_ParentOrgUnitId",
                schema: "organization",
                table: "org_unit",
                column: "ParentOrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_message_SyncStatus",
                schema: "ops",
                table: "outbox_message",
                column: "SyncStatus");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_ReferenceCode",
                schema: "proposal",
                table: "proposal",
                column: "ReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposal_RfqId_State",
                schema: "proposal",
                table: "proposal",
                columns: new[] { "RfqId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_RfqId_SupplierId",
                schema: "proposal",
                table: "proposal",
                columns: new[] { "RfqId", "SupplierId" },
                unique: true,
                filter: "\"State\" NOT IN ('Withdrawn', 'Lapsed', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_SupplierId_State",
                schema: "proposal",
                table: "proposal",
                columns: new[] { "SupplierId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_document_ProposalId",
                schema: "proposal",
                table: "proposal_document",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_item_ProposalId_RfqItemId",
                schema: "proposal",
                table: "proposal_item",
                columns: new[] { "ProposalId", "RfqItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_region_Code",
                schema: "reference",
                table: "region",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_representative_SupplierId",
                schema: "supplier",
                table: "representative",
                column: "SupplierId",
                unique: true,
                filter: "\"IsPrimary\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_RfqId",
                schema: "rfq",
                table: "requirement",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_answer_ProposalId_RequirementId",
                schema: "proposal",
                table: "requirement_answer",
                columns: new[] { "ProposalId", "RequirementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rfq_OrganizationId_OwnerUserId",
                schema: "rfq",
                table: "rfq",
                columns: new[] { "OrganizationId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_rfq_OrganizationId_State",
                schema: "rfq",
                table: "rfq",
                columns: new[] { "OrganizationId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_rfq_ReferenceCode",
                schema: "rfq",
                table: "rfq",
                column: "ReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rfq_SearchVector",
                schema: "rfq",
                table: "rfq",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_rfq_State",
                schema: "rfq",
                table: "rfq",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_rfq_approval_RfqId_StepNo",
                schema: "rfq",
                table: "rfq_approval",
                columns: new[] { "RfqId", "StepNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rfq_attachment_RfqId",
                schema: "rfq",
                table: "rfq_attachment",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_rfq_item_RfqId_LineNo",
                schema: "rfq",
                table: "rfq_item",
                columns: new[] { "RfqId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identity",
                table: "role",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_claim_RoleId",
                schema: "identity",
                table: "role_claim",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_security_token_TokenHash",
                schema: "identity",
                table: "security_token",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_security_token_UserId",
                schema: "identity",
                table: "security_token",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_OnboardingState",
                schema: "supplier",
                table: "supplier",
                column: "OnboardingState");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_ReferenceCode",
                schema: "supplier",
                table: "supplier",
                column: "ReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_SearchVector",
                schema: "supplier",
                table: "supplier",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_document_DocumentTypeId",
                schema: "supplier",
                table: "supplier_document",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_document_ReferenceCode",
                schema: "supplier",
                table: "supplier_document",
                column: "ReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_document_SupplierId_DocumentTypeId_IsLatestVersion",
                schema: "supplier",
                table: "supplier_document",
                columns: new[] { "SupplierId", "DocumentTypeId", "IsLatestVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_field_config_Category_FieldCode",
                schema: "ops",
                table: "supplier_field_config",
                columns: new[] { "Category", "FieldCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_org_link_OrganizationId",
                schema: "organization",
                table: "supplier_org_link",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_org_link_SupplierId_OrganizationId",
                schema: "organization",
                table: "supplier_org_link",
                columns: new[] { "SupplierId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_review_annotation_SupplierId_ResolvedAt",
                schema: "supplier",
                table: "supplier_review_annotation",
                columns: new[] { "SupplierId", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_system_setting_Key",
                schema: "ops",
                table: "system_setting",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ui_string_override_Key_Language",
                schema: "ops",
                table: "ui_string_override",
                columns: new[] { "Key", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_unit_of_measure_Code",
                schema: "reference",
                table: "unit_of_measure",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_claim_UserId",
                schema: "identity",
                table: "user_claim",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_login_UserId",
                schema: "identity",
                table: "user_login",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_RoleId",
                schema: "identity",
                table: "user_role",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_user_session_FamilyId",
                schema: "identity",
                table: "user_session",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_user_session_UserId",
                schema: "identity",
                table: "user_session",
                column: "UserId");

            // ---- Beyond the model: two objects EF cannot generate from it ------------------------
            //
            // Both were separate migrations before the squash and both are still needed, so they are
            // reproduced here verbatim rather than left behind. Neither is expressible as model
            // configuration: one is a trigger, the other an index over an expression.

            // FR-AUD-002/NFR-CMP-002: "no user, including admin, can edit or delete" an audit entry.
            //
            // A trigger rather than a REVOKE, and the distinction is the point. The application connects
            // as the role that owns these tables, and in Postgres a table's owner bypasses GRANT/REVOKE
            // checks on it - so REVOKE UPDATE, DELETE would read as a control in the diff and enforce
            // nothing against the connection this product actually uses. A BEFORE trigger runs for every
            // caller regardless of ownership, superuser included. INSERT is untouched.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION ops.prevent_audit_log_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'ops.audit_log is append-only (FR-AUD-002/NFR-CMP-002): % on row % is not permitted',
                        TG_OP, OLD."Id"
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER audit_log_immutable
                BEFORE UPDATE OR DELETE ON ops.audit_log
                FOR EACH ROW
                EXECUTE FUNCTION ops.prevent_audit_log_mutation();
                """);

            // One registration number, one supplier - compared trimmed, because "1234 " and "1234" are the
            // same company and a plain unique index would let both register. NULL needs no special case:
            // Postgres never treats two NULLs as equal, and the field is optional at registration.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "IX_supplier_RegistrationNumber_Normalized"
                ON supplier.supplier (btrim("RegistrationNumber"))
                WHERE "RegistrationNumber" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS supplier."IX_supplier_RegistrationNumber_Normalized";""");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_immutable ON ops.audit_log;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS ops.prevent_audit_log_mutation();");

            migrationBuilder.DropTable(
                name: "addendum",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "address",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "approval",
                schema: "award");

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "bank_account",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "branch",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "category",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "category_link",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "clarification",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "consolidated_result",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "contact",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "criterion",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "currencies",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "document_expiry_reminder",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "document_type_category",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "email_template_override",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "evaluation_assignment",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "evaluation_criterion_snapshot",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "evaluator_score",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "idempotency_record",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "invitation",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "notification",
                schema: "shared");

            migrationBuilder.DropTable(
                name: "notification_preference",
                schema: "shared");

            migrationBuilder.DropTable(
                name: "notification_template",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "offering",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "proposal_document",
                schema: "proposal");

            migrationBuilder.DropTable(
                name: "proposal_item",
                schema: "proposal");

            migrationBuilder.DropTable(
                name: "reference_code_counter",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "region",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "representative",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "requirement",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "requirement_answer",
                schema: "proposal");

            migrationBuilder.DropTable(
                name: "rfq_approval",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "rfq_attachment",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "rfq_item",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "role_claim",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "security_token",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "supplier_field_config",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "supplier_org_link",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "supplier_review_annotation",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "system_setting",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "ui_string_override",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "unit_of_measure",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "user_claim",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_login",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_role",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_session",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_token",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "award",
                schema: "award");

            migrationBuilder.DropTable(
                name: "evaluation_template",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "supplier_document",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "evaluation",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "proposal",
                schema: "proposal");

            migrationBuilder.DropTable(
                name: "rfq",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "role",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_user",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "document_type",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "supplier",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "org_unit",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "organization",
                schema: "organization");
        }
    }
}
