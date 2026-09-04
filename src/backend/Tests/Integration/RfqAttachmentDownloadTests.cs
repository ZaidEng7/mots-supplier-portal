using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T3-01: RFQ tender attachments could be uploaded and deleted but never read.
///
/// <para><b>The gate is the whole feature.</b> A download is a direct object read, and the id looks
/// unguessable, which is exactly why row-scoping gets forgotten on one. Every test here is about who
/// gets bytes rather than whether bytes come back.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RfqAttachmentDownloadTests(PostgresApiFixture fixture)
{
    private async Task<(HttpClient Client, Guid SupplierId)> ActiveSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

        return (client, supplier.Id);
    }

    private async Task RunTimelineJobAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
    }

    /// <summary>A published RFQ in a fresh org, with one attachment and one invited supplier.</summary>
    private async Task<(string ReferenceCode, Guid AttachmentId, HttpClient Officer, Guid OrgId)> PublishedRfqWithAttachmentAsync(
        Guid invitedSupplierId, string title)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        // An RFQ cannot reach review without a bound evaluation template - the completeness gate
        // said so in as many words, and it is the setup this suite needs rather than the subject.
        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"Template {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "معيار", nameEn = "Only Criterion", dimension = "Technical", weight = 100, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var create = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = title, descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddSeconds(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddMinutes(30),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var referenceCode = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 1, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("tender specification bytes"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "specification.pdf");
        var upload = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/attachments", content);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = invitedSupplierId });
        var review = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        review.StatusCode.Should().Be(HttpStatusCode.OK, await review.Content.ReadAsStringAsync());
        var approve = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
        (await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await RunTimelineJobAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attachmentId = await db.Set<RfqAttachment>()
            .Where(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.ReferenceCode == referenceCode))
            .Select(a => a.Id).FirstAsync();

        return (referenceCode, attachmentId, officer, org.Id);
    }

    private static string Url(string referenceCode, Guid attachmentId) =>
        $"/api/v1/rfqs/{referenceCode}/attachments/{attachmentId}/download-url";

    [Fact]
    public async Task An_invited_supplier_can_download_the_tender_document()
    {
        // The whole point of T3-01: a specification a supplier is meant to bid against, that the
        // supplier could not open.
        var (supplier, supplierId) = await ActiveSupplierAsync($"AttachDl {Guid.NewGuid():N}"[..30]);
        var (referenceCode, attachmentId, officer, _) = await PublishedRfqWithAttachmentAsync(supplierId, "Attachment RFQ");

        var response = await supplier.GetAsync(Url(referenceCode, attachmentId));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("url").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("fileName").GetString().Should().Be("specification.pdf");

        // The owner control on the other side of the same URL: the buyer who attached it can read it
        // too, so the supplier's 200 is not the only path that works.
        (await officer.GetAsync(Url(referenceCode, attachmentId))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_supplier_who_was_not_invited_gets_404_indistinguishable_from_an_unknown_id()
    {
        var (invited, invitedId) = await ActiveSupplierAsync($"AttachIn {Guid.NewGuid():N}"[..30]);
        var (outsider, _) = await ActiveSupplierAsync($"AttachOut {Guid.NewGuid():N}"[..30]);
        var (referenceCode, attachmentId, _, _) = await PublishedRfqWithAttachmentAsync(invitedId, "Attachment Scope RFQ");

        // Owner control: the invited supplier really can read this exact attachment, so the 404
        // below is the scope working rather than a route that refuses everyone.
        (await invited.GetAsync(Url(referenceCode, attachmentId))).StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await outsider.GetAsync(Url(referenceCode, attachmentId));
        refused.StatusCode.Should().Be(HttpStatusCode.NotFound, "§9.2: 404, never 403");

        // Indistinguishable from an id that never existed. The status code alone is not the claim -
        // a different BODY would still tell a prober that the attachment is real.
        var fabricated = await outsider.GetAsync(Url(referenceCode, Guid.CreateVersion7()));
        fabricated.StatusCode.Should().Be(HttpStatusCode.NotFound);

        static string Shape(string body) => System.Text.RegularExpressions.Regex.Replace(
            body, "\"(instance|traceId|correlationId)\":\"[^\"]*\"", "$1");

        Shape(await refused.Content.ReadAsStringAsync())
            .Should().Be(Shape(await fabricated.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Staff_from_another_organization_cannot_download_it()
    {
        // A count-free direct-object read across organizations - the widest one in the product.
        var (_, supplierId) = await ActiveSupplierAsync($"AttachOrg {Guid.NewGuid():N}"[..30]);
        var (referenceCode, attachmentId, officer, _) = await PublishedRfqWithAttachmentAsync(supplierId, "Attachment Org RFQ");

        (await officer.GetAsync(Url(referenceCode, attachmentId))).StatusCode
            .Should().Be(HttpStatusCode.OK, "owner control: the RFQ's own officer reads it");

        var otherOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var outsider = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, otherOrg.Id);

        (await outsider.GetAsync(Url(referenceCode, attachmentId))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_attachment_cannot_be_reached_through_another_rfqs_reference_code()
    {
        // The attachment is resolved THROUGH the RFQ, never by its own id. Looking it up by id and
        // checking the parent afterwards would make the id the key - the classic direct-object read
        // defect, and the one a route shaped like this invites.
        var (supplier, supplierId) = await ActiveSupplierAsync($"AttachXref {Guid.NewGuid():N}"[..30]);
        var (firstCode, attachmentId, _, _) = await PublishedRfqWithAttachmentAsync(supplierId, "Attachment A");
        var (secondCode, _, _, _) = await PublishedRfqWithAttachmentAsync(supplierId, "Attachment B");

        (await supplier.GetAsync(Url(firstCode, attachmentId))).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: through its own RFQ it resolves");

        (await supplier.GetAsync(Url(secondCode, attachmentId))).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "the same id under a different RFQ is not the same object");
    }

    [Fact]
    public async Task Granting_access_is_audited()
    {
        // A tender document handed to a bidder is the evidence that every invited supplier had the
        // same specification - which is exactly what a challenge to a tender asks about.
        var (supplier, supplierId) = await ActiveSupplierAsync($"AttachAudit {Guid.NewGuid():N}"[..30]);
        var (referenceCode, attachmentId, _, _) = await PublishedRfqWithAttachmentAsync(supplierId, "Attachment Audit RFQ");

        (await supplier.GetAsync(Url(referenceCode, attachmentId))).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var audited = await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.AggregateType == "RfqAttachment"
                        && a.AggregateId == attachmentId
                        && a.Action == "rfq_attachment_access_granted");

        audited.Should().BeTrue("asserted against the stored row, not the handler's code path");
    }

    /// <summary>
    /// T-026: the object key is built from server-side values only.
    ///
    /// <para>It used to interpolate BOTH the route's reference code and the client's own file name -
    /// <c>$"rfq-attachments/{referenceCode}/{guid}-{file.FileName}"</c> - and both are caller input.
    /// A name containing "../" shapes the key, and so does a reference code, which is not validated
    /// until the handler runs several lines later.</para>
    /// </summary>
    [Theory]
    [InlineData("../../../etc/passwd", "traversal")]
    [InlineData("..\\..\\windows\\system32\\config", "windows separators")]
    [InlineData("a/b/c.pdf", "a plain separator")]
    public async Task A_hostile_file_name_cannot_shape_the_storage_key(string fileName, string why)
    {
        // A DRAFT RFQ: attachments can only be added while an RFQ is editable, and the publishing
        // helper above leaves it SubmissionOpen. Nothing here needs a published RFQ - the assertions
        // are on the stored key, not on who can read it.
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var create = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = $"Key RFQ {why}", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(2),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var referenceCode = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("referenceCode").GetString()!;

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("bytes"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", fileName);

        var upload = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/attachments", content);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rfqId = await db.Rfqs.Where(r => r.ReferenceCode == referenceCode).Select(r => r.Id).FirstAsync();
        var keys = await db.Set<RfqAttachment>().Where(a => a.RfqId == rfqId)
            .Select(a => a.StorageKey).ToListAsync();

        // Asserted against the STORED key, not against the handler's code path.
        keys.Should().OnlyContain(k => k.StartsWith("rfq-attachments/"), "the prefix is fixed");
        keys.Should().OnlyContain(k => !k.Contains(".."), "no traversal segment survives");
        keys.Should().OnlyContain(k => k.Split('/').Length == 2, "exactly one segment after the prefix");

        // And the name is not lost - it is metadata on the row, which is what the download's
        // Content-Disposition reads.
        var stored = await db.Set<RfqAttachment>().Where(a => a.RfqId == rfqId)
            .Select(a => a.OriginalFileName).ToListAsync();
        stored.Should().Contain(fileName, "the file name is kept as metadata, just never as an address");
    }

    [Fact]
    public async Task An_ordinary_file_name_still_round_trips()
    {
        // The control. A key scheme that rejected everything would pass every assertion above.
        var (supplier, supplierId) = await ActiveSupplierAsync($"KeyOk {Guid.NewGuid():N}"[..30]);
        var (referenceCode, attachmentId, _, _) = await PublishedRfqWithAttachmentAsync(supplierId, "Key OK RFQ");

        var response = await supplier.GetAsync(Url(referenceCode, attachmentId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("fileName").GetString().Should().Be("specification.pdf",
            "an ordinary upload still stores and returns its own name");
        body.GetProperty("url").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// T-025 / D-10: quarantine-first for RFQ attachments. Both directions, because a gate that
    /// refuses everything passes the negative and a gate that refuses nothing passes the positive.
    /// </summary>
    [Fact]
    public async Task An_infected_attachment_is_refused_and_a_clean_one_is_not()
    {
        var (supplier, supplierId) = await ActiveSupplierAsync($"Scan {Guid.NewGuid():N}"[..30]);
        var (referenceCode, cleanAttachmentId, officer, _) =
            await PublishedRfqWithAttachmentAsync(supplierId, "Scan RFQ");

        // The control FIRST: the ordinary attachment seeded above downloads, so the refusal below is
        // about the scanner rather than about the gate refusing everyone.
        (await supplier.GetAsync(Url(referenceCode, cleanAttachmentId))).StatusCode
            .Should().Be(HttpStatusCode.OK, "a clean attachment is served once scanned");

        // And it is Clean in STORAGE - the scan actually ran rather than the gate being skipped.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Set<RfqAttachment>().AsNoTracking().FirstAsync(a => a.Id == cleanAttachmentId))
                .ScanState.Should().Be(AttachmentScanState.Clean);
        }

        // Standard EICAR string inside a real PDF content stream - the same construction
        // StreamingUploadTests uses, and for the reason recorded there: ClamAV's PDF parser scans
        // stream objects, not bytes trailing a %PDF header.
        const string eicar = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
        var infected = "%PDF-1.4\n" +
            "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n" +
            "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n" +
            "3 0 obj\n<</Type/Page/Parent 2 0 R/Contents 4 0 R>>\nendobj\n" +
            $"4 0 obj\n<</Length {eicar.Length}>>\nstream\n{eicar}\nendstream\nendobj\n" +
            "trailer\n<</Root 1 0 R>>\n%%EOF";

        // A second RFQ, in Draft, because attachments can only be added while an RFQ is editable.
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var draftOfficer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var create = await draftOfficer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = "Scan Draft RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(2),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var draftCode = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(infected));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "infected.pdf");
        (await draftOfficer.PostAsync($"/api/v1/rfqs/{draftCode}/attachments", content))
            .StatusCode.Should().Be(HttpStatusCode.OK, "upload does not scan - the download does");

        Guid infectedId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rfqId = await db.Rfqs.Where(r => r.ReferenceCode == draftCode).Select(r => r.Id).FirstAsync();
            infectedId = await db.Set<RfqAttachment>().Where(a => a.RfqId == rfqId).Select(a => a.Id).FirstAsync();
        }

        var refused = await draftOfficer.GetAsync(Url(draftCode, infectedId));

        refused.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the same answer as any other miss - a distinct 'infected' reply would tell an uploader their malware arrived");

        // Asserted in storage: the row records the rejection rather than staying PendingScan and
        // being re-scanned on every request.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Set<RfqAttachment>().AsNoTracking().FirstAsync(a => a.Id == infectedId))
                .ScanState.Should().Be(AttachmentScanState.ScanRejected);
        }
    }

    [Fact]
    public async Task An_attachment_uploaded_before_the_scan_existed_is_not_assumed_clean()
    {
        // D-10's existing-rows path. A row that predates the gate carries PendingScan, and the
        // download is what scans it - not a backfill that would have to walk every object in storage
        // before anything worked.
        var (supplier, supplierId) = await ActiveSupplierAsync($"Legacy {Guid.NewGuid():N}"[..30]);
        var (referenceCode, attachmentId, _, _) = await PublishedRfqWithAttachmentAsync(supplierId, "Legacy RFQ");

        // Force it back to the state an existing row would be in.
        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Set<RfqAttachment>().Where(a => a.Id == attachmentId)
                .ExecuteUpdateAsync(p => p.SetProperty(a => a.ScanState, AttachmentScanState.PendingScan));
        }

        (await supplier.GetAsync(Url(referenceCode, attachmentId))).StatusCode
            .Should().Be(HttpStatusCode.OK, "it is scanned on access, then served");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db2 = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db2.Set<RfqAttachment>().AsNoTracking().FirstAsync(a => a.Id == attachmentId))
            .ScanState.Should().Be(AttachmentScanState.Clean, "the scan ran and its result was recorded");
    }
}
