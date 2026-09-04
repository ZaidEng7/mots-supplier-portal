using System.Collections.Concurrent;
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

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Test-only pipeline hook: wraps request handling and records GC.GetTotalAllocatedBytes deltas
/// keyed by an X-Test-Probe-Id request header the test itself sets. Deliberately narrower than a
/// whole-process measurement: an in-process WebApplicationFactory means GC.GetTotalAllocatedBytes
/// is process-wide, so measuring around client.PostAsync(...) directly also counts the TEST's own
/// MultipartFormDataContent/ByteArrayContent client-side serialization happening in the same
/// window - overhead this fix was never responsible for. An IStartupFilter middleware runs
/// strictly inside the server's own request pipeline, isolating exactly the server-side work for
/// one request from everything else running in the process at the same time.
/// </summary>
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

/// <summary>
/// MSP-84/NFR-PERF-008: UploadDocumentHandler used to copy every upload into a full-file
/// MemoryStream before anything else happened to it - every concurrent upload held its entire file
/// in managed heap at once, a real memory-exhaustion vector on a public-facing system, not a
/// hypothetical. Proof here is resource behavior, not "the code compiles and the small-file test
/// still passes": GC.GetTotalAllocatedBytes(precise: true) is a monotonic counter of bytes ever
/// allocated, immune to GC-timing noise, so the delta across an upload directly answers "did this
/// allocate roughly one file's worth of extra memory or not".
///
/// A real ClamAV daemon runs for this fixture (see PostgresApiFixture._clamav) specifically so the
/// EICAR test below proves the malware-rejection path still works on the new streaming path - a
/// streaming refactor that accidentally let an infected file through by wiring the scan up wrong
/// would be worse than the memory issue it fixes.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StreamingUploadTests(PostgresApiFixture fixture)
{
    private static readonly Guid TaxCertificateDocumentTypeId = DocumentUploadTests.TaxCertificateDocumentTypeId;

    // %PDF magic bytes + padding, sized precisely by the caller - passes FileTypeSniffer's PDF
    // check regardless of size, which is what lets this stand in for "a large real PDF" without
    // needing an actual multi-megabyte PDF fixture on disk.
    private static byte[] BuildPdfOfSize(int totalBytes)
    {
        var bytes = new byte[totalBytes];
        "%PDF-1.4\n"u8.CopyTo(bytes);
        // Fill the rest with non-zero, non-pattern bytes so this can't be mistaken for a
        // suspiciously compressible/degenerate test artifact - a real upload's bytes aren't zero.
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

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_file_over_the_20MB_cap_is_rejected_by_the_framework_before_the_app_level_check()
    {
        // 22MB: over FileTypeSniffer.MaxSizeBytes (20MB) AND over the endpoint's
        // MultipartBodyLengthLimit (20MB + 1MB headroom) - proves the framework-level limit set on
        // the endpoint (RequestFormLimitsAttribute) is what's actually closing this, not just the
        // app's post-parse SizeBytes check the investigation found running too late.
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Oversized Upload Co");
        var bytes = BuildPdfOfSize(22 * 1024 * 1024);

        using var content = BuildUploadContent(bytes);
        var response = await client.PostAsync($"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}/documents", content);

        // ASP.NET Core's form-limit rejection surfaces as a mid-request exception -> 500, not a
        // clean 4xx, because it fires while ReadFormAsync is still parsing the body, before the
        // handler's own BadRequest paths ever run. The point under test is that the request is
        // rejected at all (proving the framework limit is wired up), not the exact status code.
        response.IsSuccessStatusCode.Should().BeFalse("a 22MB upload must not succeed against a 20MB cap");
    }

    [Fact]
    public async Task An_eicar_test_file_is_scanned_and_rejected_by_the_real_clamav_daemon()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Eicar Upload Co");

        // Standard EICAR antivirus test string (https://www.eicar.org/) - not a real virus, every
        // AV engine including ClamAV recognizes it by design. Must sit inside a genuine PDF
        // "stream ... endstream" object, not just trailing bytes after a %PDF header: ClamAV's PDF
        // parser extracts and scans real content-stream objects specifically, and skips bytes that
        // aren't part of one. Confirmed directly against a real clamd (clamdscan CLI) before
        // relying on it here - a %PDF-prefixed file with EICAR merely appended as loose text
        // scanned clean ("OK", not detected) against the same daemon, while this stream-object
        // form is reliably flagged ("Eicar-Test-Signature FOUND").
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
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        // T-010: the upload response's "id" is the public code now, not the Guid.
        var documentCode = created.GetProperty("id").GetString()!;

        // The scan runs out-of-band via Hangfire (DocumentScanJob) - poll for it to finish rather
        // than assuming it already has, same as any other async-background-work assertion.
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
    public async Task Server_side_allocation_during_upload_does_not_scale_with_file_size()
    {
        // A derived host, own DI container, with the allocation probe wired into its pipeline via
        // IStartupFilter (the standard ASP.NET Core seam for augmenting a test host's pipeline
        // without touching Program.cs). Each WebApplicationFactory instance signs its own JWTs
        // with its own key material, so a token minted against the shared `fixture` does not
        // validate here - register/verify/login against THIS factory instead, even though it
        // means a second supplier registration rather than reusing SupplierTestClient's helper
        // (which is typed to PostgresApiFixture specifically, not the general
        // WebApplicationFactory<Program> that WithWebHostBuilder returns).
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
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

            UploadAllocationProbe.Results.TryGetValue(probeId, out var allocated);
            return allocated;
        }

        // Warm up (JIT, connection setup) so it doesn't pollute the first real measurement.
        await MeasureAsync(1024);

        const int small = 1 * 1024 * 1024;   // 1MB
        const int large = 18 * 1024 * 1024;  // 18MB - 17MB bigger than `small`

        var smallAllocated = await MeasureAsync(small);
        var largeAllocated = await MeasureAsync(large);

        // If the old MemoryStream-per-upload behavior were still present, largeAllocated would be
        // roughly (large - small) bytes bigger than smallAllocated. Genuine streaming keeps the
        // server's own contribution roughly constant regardless of file size, so the delta should
        // be a small fraction of the size difference, not comparable to it. Threshold is loose
        // enough to absorb real fixed-size framework/SDK buffers, tight enough that the old
        // behavior - scaling by nearly the full file size - would fail it outright.
        var fileSizeDifference = large - small;
        var allocationGrowth = largeAllocated - smallAllocated;

        allocationGrowth.Should().BeLessThan(fileSizeDifference / 4,
            $"a genuinely streaming upload should not allocate server-side memory proportionally to file size; " +
            $"small={smallAllocated:N0}B, large={largeAllocated:N0}B, growth={allocationGrowth:N0}B " +
            $"vs a {fileSizeDifference:N0}B file-size difference");
    }
}
