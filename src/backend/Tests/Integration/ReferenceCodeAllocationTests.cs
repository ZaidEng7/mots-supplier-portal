using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-81. Reference codes were allocated as COUNT(*) + 1, which is not a sequence.
///
/// These run against real Postgres because the defect only exists there: an in-memory or mocked
/// store would not reproduce either failure mode, since both are about what the database does when
/// two statements interleave or when rows disappear.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReferenceCodeAllocationTests(PostgresApiFixture fixture)
{
    private static object RegistrationPayload(string suffix) => new
    {
        displayNameAr = "شركة اختبار",
        displayNameEn = $"RefCode {suffix}",
        registrationNumber = $"RC-{Guid.NewGuid():N}"[..12],
        representativeName = "Integration Tester",
        representativePhone = "+963900000000",
        email = $"refcode-{Guid.NewGuid():N}@example.com",
        password = SupplierTestClient.Password,
    };

    private static async Task<string?> RegisterAndReadCodeAsync(HttpClient client, string suffix)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationPayload(suffix));
        // MSP-73: success is 200 OK now, not 201 Created - the enumeration fix made success and
        // duplicate responses identical in shape, so status alone no longer distinguishes them.
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string?>>();
        // §12.1/R-9: the register response spells this supplierCode now, matching §12.2 everywhere.
        return body!["supplierCode"];
    }

    /// <summary>
    /// The test that matters. Against COUNT(*) + 1 these registrations all read the same count and
    /// generate the same code; the unique index then rejects all but one, so the losers surface as
    /// 500s rather than as duplicate codes.
    /// </summary>
    [Fact]
    public async Task Concurrent_registrations_all_succeed_and_receive_distinct_codes()
    {
        const int parallelism = 12;

        var codes = await Task.WhenAll(Enumerable.Range(0, parallelism).Select(async i =>
        {
            // A client each: sharing one HttpClient would serialise nothing, but keeps the failure
            // mode ambiguous if connection reuse ever changed.
            var client = fixture.CreateClient();
            return await RegisterAndReadCodeAsync(client, $"Concurrent {i}");
        }));

        codes.Should().NotContainNulls(
            "every concurrent registration must succeed; a 500 here means two callers claimed one code");
        codes.Should().OnlyHaveUniqueItems(
            "the database allocates the sequence, so no two registrations can be issued the same code");
    }

    /// <summary>
    /// Deleting a supplier lowers COUNT(*) below the highest code already issued. The old generator
    /// then re-issued an existing code and every subsequent registration failed.
    /// </summary>
    [Fact]
    public async Task Deleting_a_supplier_does_not_cause_its_code_to_be_reused()
    {
        var doomedCode = await RegisterAndReadCodeAsync(fixture.CreateClient(), "To Be Deleted");
        doomedCode.Should().NotBeNull();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Mirrors what DraftCleanupJob does daily, which is why this is ordinary operation
            // rather than a contrived state.
            await db.Suppliers.Where(s => s.ReferenceCode == doomedCode).ExecuteDeleteAsync();
        }

        var nextCode = await RegisterAndReadCodeAsync(fixture.CreateClient(), "After Deletion");

        nextCode.Should().NotBeNull("registration must still work after a supplier is deleted");
        nextCode.Should().NotBe(doomedCode,
            "the counter is monotonic; a deleted code is retired, not returned to the pool");
    }

    [Fact]
    public async Task Codes_keep_the_documented_shape_and_advance_monotonically()
    {
        // Guards the format alongside the mechanism: a correct sequence formatted wrongly would
        // still break every URL and audit record that carries a reference code.
        var first = await RegisterAndReadCodeAsync(fixture.CreateClient(), "Shape One");
        var second = await RegisterAndReadCodeAsync(fixture.CreateClient(), "Shape Two");

        first.Should().MatchRegex($@"^SUP-{DateTime.UtcNow.Year}-\d{{6}}$");
        second.Should().MatchRegex($@"^SUP-{DateTime.UtcNow.Year}-\d{{6}}$");

        // Gaps are acceptable (a rolled-back registration consumes a value, exactly as a Postgres
        // sequence does), so this asserts ordering rather than adjacency.
        string.CompareOrdinal(second, first).Should().BePositive();
    }

    [Fact]
    public async Task Counter_is_seeded_from_the_highest_issued_code_not_the_row_count()
    {
        // The migration's backfill. If it had used COUNT(*), the counter would sit below codes that
        // already exist and the next registration would collide - the original bug, reintroduced by
        // the fix for it.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var prefix = $"SUP-{DateTime.UtcNow.Year}-";
        var counter = await db.ReferenceCodeCounters.SingleAsync(c => c.Prefix == prefix);

        var highestIssued = await db.Suppliers
            .Where(s => s.ReferenceCode.StartsWith(prefix))
            .MaxAsync(s => s.ReferenceCode);

        counter.LastValue.Should().Be(long.Parse(highestIssued[prefix.Length..]),
            "the counter must never sit below a code that has already been handed out");
    }
}
