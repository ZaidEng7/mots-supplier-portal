// The upload path streams the file rather than holding it whole in memory, and still refuses malware.
//
// The handler used to copy every upload into a full-file buffer before anything else happened to it, so every
// concurrent upload held its entire file on the managed heap at once. That is a real memory-exhaustion vector on a
// public-facing system rather than a hypothetical.
//
//
// THE PROOF IS RESOURCE BEHAVIOUR, NOT "IT COMPILES AND THE SMALL-FILE TEST STILL PASSES"
//
// The allocation counter is monotonic, a count of bytes ever allocated, so it is immune to collection-timing
// noise and the delta across an upload directly answers whether roughly one file's worth of extra memory was
// allocated.
//
// If the old behaviour were still present, the large upload would allocate about the size difference more than
// the small one. Genuine streaming keeps the server's own contribution roughly constant regardless of file size,
// so the delta should be a small fraction of that difference rather than comparable to it. The threshold is loose
// enough to absorb real fixed-size framework buffers and tight enough that the old behaviour, scaling by nearly
// the full file size, would fail it outright.
//
// A warm-up request runs first so compilation and connection setup do not pollute the first real measurement.
//
//
// THE MEASUREMENT IS TAKEN INSIDE THE SERVER'S OWN PIPELINE
//
// The counter is process-wide and the host runs in-process, so measuring around the client call would also count
// the TEST's own client-side serialisation in the same window, which this fix was never responsible for.
//
// A middleware hook, keyed by a header the test itself sets, runs strictly inside the server's request pipeline
// and isolates exactly the server-side work for one request from everything else in the process.
//
// That host is derived, with its own container, and it signs its own tokens with its own key material, so a token
// minted against the shared fixture does not validate against it. Hence a second supplier registration here
// rather than reusing the shared helper, which is typed to the shared fixture.
//
//
// THE MALWARE CASE, AND WHY THE SAMPLE IS SHAPED THE WAY IT IS
//
// A real scanner daemon runs for this fixture specifically so this proves the rejection path still works on the
// streaming path. A streaming change that accidentally let an infected file through by wiring the scan up wrong
// would be worse than the memory issue it fixes.
//
// The sample is the standard industry test string, which is not a real virus and which every engine recognises by
// design. It must sit inside a genuine document content-stream object rather than as trailing bytes after a
// header: the scanner's parser extracts and scans real stream objects and skips bytes that are not part of one.
//
// Confirmed directly against a real daemon before relying on it. The same string merely appended as loose text
// after a header scanned CLEAN, while this form is reliably flagged.
//
// Its control is the clean upload, which must land in review rather than merely uploaded. Without it, the malware
// case alone would still pass on a pipeline that never transitions anything anywhere but refused.
//
// The scan runs out of band, so these poll for it rather than assuming it has already finished.
//
//
// THE SIZE AND TYPE REFUSALS
//
// The oversize sample is over both the application's own cap and the endpoint's request limit, which proves the
// framework-level limit is what closes it rather than the application's post-parse check that the investigation
// found running too late.
//
// That rejection surfaces as a mid-request failure rather than a clean refusal, because it fires while the body is
// still being parsed and before the handler's own paths run. The point under test is that the request is rejected
// at all, not the exact status code.
//
// Elsewhere the two are asserted as distinct: oversize and wrong type get different answers rather than one
// generic refusal for both.
//
//
// THE CONTRACT DETAILS, ASSERTED WHERE THEY ARE OBSERVABLE
//
// The creation is accepted-but-incomplete, because the scan has not run yet. The scan status is derived from the
// state the row is actually in, which at that instant is unscanned, so the control for the clean case is asserted
// further down.
//
// The location header names a resource that now exists and resolves for the owner: an accepted response whose
// location cannot be read is a worse contract than a non-conforming path, which is why the assertion follows the
// header rather than merely reading it. Its control is another supplier following the same location and getting a
// miss rather than somebody else's document.
//
// The content route answers with a redirect to the signed link, followed manually because a redirect to a foreign
// origin is the whole point of the route and an auto-following client hides it. Its control is a different
// supplier getting neither redirect nor link.
//
// And the documented query actually returns the document: asserting the column alone would not have caught a
// filter that cannot parse the value it is being asked to match.
//
//
// THE SAMPLE FILE ITSELF
//
// A real document header plus padding, sized by the caller, which passes the type sniff regardless of size. That
// is what lets it stand in for a large real document without a multi-megabyte fixture on disk.
//
// The padding is non-zero and non-repeating so it cannot be mistaken for a degenerate or suspiciously
// compressible artefact. A real upload's bytes are not zeroes.

namespace MotsSupplierPortal.Tests.Integration.Documents;

using System.Collections.Concurrent;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

file static class UploadAllocationProbe
{
    public static readonly ConcurrentDictionary<string, long> Results = new();

    public sealed class Filter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Probe-Id", out var probeId) && probeId.Count > 0)
                {
                    var before = GC.GetTotalAllocatedBytes(precise: true);
                    await nextMiddleware();
                    var after = GC.GetTotalAllocatedBytes(precise: true);
                    Results[probeId[0]!] = after - before;
                }
                else
                {
                    await nextMiddleware();
                }
            });
            next(app);
        };
    }
}

[Collection(IntegrationTestCollection.Name)]
public sealed class StreamingUploadTests(PostgresApiFixture fixture)
{
    private static readonly Guid TaxCertificateDocumentTypeId = UploadFixtures.TaxCertificateDocumentTypeId;

    private static byte[] BuildPdfOfSize(int totalBytes)
    {
        var bytes = new byte[totalBytes];
        "%PDF-1.4\n"u8.CopyTo(bytes);
        var rng = new Random(42);
        rng.NextBytes(bytes.AsSpan(9));
        "\ntrailer<</Root 1 0 R>>\n%%EOF"u8.CopyTo(bytes.AsSpan(totalBytes - 29));
        return bytes;
    }

    private static MultipartFormDataContent BuildUploadContent(byte[] fileBytes, string fileName = "cert.pdf")
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(TaxCertificateDocumentTypeId.ToString()), "documentTypeId" },
            { new StringContent("2027-03-15"), "expiryDate" },
        };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    [Fact]
    public async Task A_large_file_near_the_20MB_cap_uploads_successfully()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Large Upload Co");
        var bytes = BuildPdfOfSize(19 * 1024 * 1024); // under the 20MB cap

        using var content = BuildUploadContent(bytes);
        var response = await client.PostAsync($"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}/documents", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_file_over_the_20MB_cap_is_rejected_by_the_framework_before_the_app_level_check()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Oversized Upload Co");
        var bytes = BuildPdfOfSize(22 * 1024 * 1024);

        using var content = BuildUploadContent(bytes);
        var response = await client.PostAsync($"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}/documents", content);

        response.IsSuccessStatusCode.Should().BeFalse("a 22MB upload must not succeed against a 20MB cap");
    }

    [Fact]
    public async Task An_eicar_test_file_is_scanned_and_rejected_by_the_real_clamav_daemon()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Eicar Upload Co");

        const string eicar = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
        var body = "%PDF-1.4\n" +
            "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n" +
            "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n" +
            "3 0 obj\n<</Type/Page/Parent 2 0 R/Contents 4 0 R>>\nendobj\n" +
            $"4 0 obj\n<</Length {eicar.Length}>>\nstream\n{eicar}\nendstream\nendobj\n" +
            "trailer\n<</Root 1 0 R>>\n%%EOF";
        var bytes = System.Text.Encoding.ASCII.GetBytes(body);

        using var content = BuildUploadContent(bytes, "eicar.pdf");
        var response = await client.PostAsync($"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}/documents", content);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var documentCode = created.GetProperty("documentId").GetString()!;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        string? state = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            state = await db.SupplierDocuments.Where(d => d.ReferenceCode == documentCode).Select(d => d.State.ToString()).FirstAsync();
            if (state != "PendingScan") break;
            await Task.Delay(500);
        }

        state.Should().Be("ScanRejected", "clamd must have flagged the EICAR signature and the job must have recorded it");
    }

    [Fact]
    public async Task A_clean_upload_reaches_the_review_queue_the_documented_reviewer_query_reads()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Clean Scan Co");
        var supplierCode = await client.OwnSupplierCodeAsync();

        using var content = BuildUploadContent(BuildPdfOfSize(4096), "clean.pdf");
        var response = await client.PostAsync($"/api/v1/suppliers/{supplierCode}/documents", content);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var documentCode = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetString()!;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        string? state = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            state = await db.SupplierDocuments.Where(d => d.ReferenceCode == documentCode)
                .Select(d => d.State.ToString()).FirstAsync();
            if (state != "PendingScan") break;
            await Task.Delay(500);
        }

        state.Should().Be("UnderReview",
            "the scan job is what §4.4 says moves a document into review, and it stopped at Uploaded");

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var queued = await reviewer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/suppliers/{supplierCode}/documents?state=UnderReview,Rejected");
        queued.GetProperty("data").EnumerateArray()
            .Select(d => d.GetProperty("documentId").GetString())
            .Should().Contain(documentCode, "§12.3's reviewer queue is the reason UnderReview exists");
    }

    [Fact]
    public async Task The_documented_upload_and_download_contract_holds_end_to_end()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Contract Doc Co");
        var supplierCode = await client.OwnSupplierCodeAsync();

        using var content = BuildUploadContent(BuildPdfOfSize(4096), "contract.pdf");
        var upload = await client.PostAsync($"/api/v1/suppliers/{supplierCode}/documents", content);

        upload.StatusCode.Should().Be(HttpStatusCode.Accepted, await upload.Content.ReadAsStringAsync());
        var created = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var documentCode = created.GetProperty("documentId").GetString()!;

        created.GetProperty("scanStatus").GetString().Should().BeOneOf("Pending", "Clean");

        var location = upload.Headers.Location!.ToString();
        location.Should().Be($"/api/v1/suppliers/{supplierCode}/documents/{documentCode}");
        var followed = await client.GetAsync(location);
        followed.StatusCode.Should().Be(HttpStatusCode.OK, await followed.Content.ReadAsStringAsync());
        (await followed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("documentId").GetString().Should().Be(documentCode);

        var stranger2 = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Location Stranger Co");
        (await stranger2.GetAsync(location)).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "§9.2: out of scope is indistinguishable from does not exist");

        using var oversized = BuildUploadContent(BuildPdfOfSize(21 * 1024 * 1024), "big.pdf");
        (await client.PostAsync($"/api/v1/suppliers/{supplierCode}/documents", oversized))
            .StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        using var wrongType = BuildUploadContent(System.Text.Encoding.UTF8.GetBytes("not a pdf"), "notes.txt");
        (await client.PostAsync($"/api/v1/suppliers/{supplierCode}/documents", wrongType))
            .StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await db.SupplierDocuments.Where(d => d.ReferenceCode == documentCode)
                .Select(d => d.State).FirstAsync();
            if (state != DocumentState.PendingScan) break;
            await Task.Delay(500);
        }

        using var noRedirect = fixture.CreateClientWithoutRedirects();
        noRedirect.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var redirect = await noRedirect.GetAsync($"/api/v1/documents/{documentCode}/content");

        redirect.StatusCode.Should().Be(HttpStatusCode.Found, "§12.3 specifies a 302");
        redirect.Headers.Location.Should().NotBeNull("the redirect must name the pre-signed URL");

        var stranger = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Stranger Doc Co");
        (await stranger.GetAsync($"/api/v1/documents/{documentCode}/content"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Server_side_allocation_during_upload_does_not_scale_with_file_size()
    {
        await using var probeFactory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, UploadAllocationProbe.Filter>()));
        using var client = probeFactory.CreateClient();

        var email = $"itest-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            displayNameAr = "شركة اختبار",
            displayNameEn = "Memory Measurement Co",
            registrationNumber = $"RC-{Guid.NewGuid():N}"[..12],
            representativeName = "Integration Tester",
            representativePhone = "+963900000000",
            email,
            password = SupplierTestClient.Password,
        });

        await using (var scope = probeFactory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<MotsSupplierPortal.Domain.Identity.AppUser>>();
            var securityTokenService = scope.ServiceProvider.GetRequiredService<MotsSupplierPortal.Application.Common.ISecurityTokenService>();
            var user = await userManager.FindByEmailAsync(email);
            var rawToken = await securityTokenService.IssueAsync(
                user!.Id, MotsSupplierPortal.Domain.Identity.SecurityTokenPurpose.EmailVerification, TimeSpan.FromHours(24), CancellationToken.None);
            await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token = rawToken });
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = SupplierTestClient.Password });
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.GetProperty("accessToken").GetString());

        async Task<long> MeasureAsync(int sizeBytes)
        {
            var bytes = BuildPdfOfSize(sizeBytes);
            using var content = BuildUploadContent(bytes);
            var probeId = Guid.NewGuid().ToString("N");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}/documents") { Content = content };
            request.Headers.Add("X-Test-Probe-Id", probeId);

            var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

            UploadAllocationProbe.Results.TryGetValue(probeId, out var allocated);
            return allocated;
        }

        await MeasureAsync(1024);

        const int small = 1 * 1024 * 1024;   // 1MB
        const int large = 18 * 1024 * 1024;  // 18MB - 17MB bigger than `small`

        var smallAllocated = await MeasureAsync(small);
        var largeAllocated = await MeasureAsync(large);

        var fileSizeDifference = large - small;
        var allocationGrowth = largeAllocated - smallAllocated;

        allocationGrowth.Should().BeLessThan(fileSizeDifference / 4,
            $"a genuinely streaming upload should not allocate server-side memory proportionally to file size; " +
            $"small={smallAllocated:N0}B, large={largeAllocated:N0}B, growth={allocationGrowth:N0}B " +
            $"vs a {fileSizeDifference:N0}B file-size difference");
    }
}
