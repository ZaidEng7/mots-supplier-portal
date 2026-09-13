// T-030 and D-15: a write to a CHILD advances its aggregate root's version.
//
// While the version was Postgres xmin it did not. xmin moves only when the root ROW is written, and a child
// insert leaves the root untouched - so a correct If-Match on any child-write route was silently ignored,
// and two callers editing different children of one aggregate both won. These tests are the proof that the
// application-managed counter closed it, asserted against storage and against the wire rather than against
// the code path.
//
// The first test is a pure CHILD write: nothing on the supplier row itself changes, and the version the API
// hands out moved with it. The storage assertion alone would not prove the ETag a client relies on had
// changed.
//
// The second is the end-to-end proof, on a route that DECLARES If-Match - the supplier PATCH. Under xmin
// this sequence succeeded, and that was the lost update: adding a contact left the supplier row untouched,
// so its xmin never moved, so an ETag read BEFORE the contact was still accepted afterwards, and a caller
// editing the profile could overwrite it on top of a version it had never seen. Split (1) delivered the
// bump; split (3) put the supplier's child writes under If-Match, so the child write here now sends the
// version it just read. That does not weaken what the test proves - the point is still that a version read
// BEFORE a child write is stale afterwards, and the guarded PATCH refuses it. It uses CreateRawClient
// rather than the fixture's default, which probes a CURRENT ETag for every mutation: a caller who always
// sends the right version cannot observe a wrong one. The refused write is then recoverable by exactly the
// step §8.1 prescribes - re-read, retry.
//
// The Added-root test is the regression this work caught during development: attributing a child to its
// root and forcing that root Modified made EF emit an UPDATE against a row being INSERTED in the same unit
// of work, and registration - which writes a Supplier and its representative together - answered 500. An
// Added root is excluded.
//
// The one-hop limit in PrincipalRootOf is an assumption about this schema: every aggregate is one level
// deep. Rather than trust it, the context exposes what the walk could not attribute, so a grandchild
// introduced later shows up in that test instead of silently failing to bump. It seeds its own supplier -
// the first version took whichever one happened to be in the database, which passed alone and failed in
// the full run, an order dependence rather than a finding.
//
// The DELETED-root test: the bump forces State = Modified on every touched root, and on a deleted root
// that would turn the DELETE into an UPDATE - the row survives, the version advances, and the caller is
// told the thing was removed. Found by T-061's revert, where the override row came back every time. It is
// asserted at the DbContext rather than through a route, because the only versioned root with a delete
// today is NotificationTemplate and the point is the SaveChanges behaviour itself: the next versioned
// aggregate to gain a delete must not rediscover this.

namespace MotsSupplierPortal.Tests.Integration.Concurrency;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ChildWriteVersionTests(PostgresApiFixture fixture)
{
    private async Task<uint> VersionAsync(string supplierCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Suppliers.AsNoTracking()
            .Where(s => s.ReferenceCode == supplierCode).Select(s => s.RowVersion).FirstAsync();
    }

    [Fact]
    public async Task Adding_a_child_advances_the_roots_version_and_the_etag_it_issues()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Child Version Co");
        var supplierCode = await client.OwnSupplierCodeAsync();

        var before = await VersionAsync(supplierCode);
        var etagBefore = (await client.GetAsync($"/api/v1/suppliers/{supplierCode}")).Headers.ETag!.Tag;

        var added = await client.PostAsJsonAsync("/api/v1/suppliers/me/contacts", new
            {
                fullName = "ليان الأحمد", email = $"contact-{Guid.NewGuid():N}@example.sy",
                phone = "+963900000001", role = (string?)null,
        });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());

        var after = await VersionAsync(supplierCode);
        after.Should().Be(before + 1, "a child write moves the aggregate, so it must move the version");

        var etagAfter = (await client.GetAsync($"/api/v1/suppliers/{supplierCode}")).Headers.ETag!.Tag;
        etagAfter.Should().NotBe(etagBefore);
    }

    [Fact]
    public async Task A_child_write_makes_a_previously_read_etag_stale_on_a_guarded_route()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Stale ETag Co");
        var supplierCode = await client.OwnSupplierCodeAsync();

        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;

        var beforeChild = (await raw.GetAsync($"/api/v1/suppliers/{supplierCode}")).Headers.ETag!;

        using var contactRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/suppliers/me/contacts")
        {
            Content = JsonContent.Create(new
            {
                fullName = "ليان الأحمد", email = $"stale-{Guid.NewGuid():N}@example.sy",
                phone = "+963900000001", role = (string?)null,
            }),
        };
        contactRequest.Headers.TryAddWithoutValidation("If-Match", beforeChild.ToString());
        var contact = await raw.SendAsync(contactRequest);
        contact.StatusCode.Should().Be(HttpStatusCode.OK, await contact.Content.ReadAsStringAsync());

        var stalePatch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/suppliers/{supplierCode}")
        {
            Content = new StringContent("""{"description":"after a child write"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        stalePatch.Headers.IfMatch.Add(beforeChild);

        (await raw.SendAsync(stalePatch)).StatusCode.Should().Be(HttpStatusCode.PreconditionFailed,
            "the contact moved the aggregate, so an ETag read before it is stale - under xmin this " +
            "same request succeeded and overwrote the profile on top of a version it never saw");

        var fresh = (await raw.GetAsync($"/api/v1/suppliers/{supplierCode}")).Headers.ETag!;
        var retried = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/suppliers/{supplierCode}")
        {
            Content = new StringContent("""{"description":"after a re-read"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        retried.Headers.IfMatch.Add(fresh);

        (await raw.SendAsync(retried)).StatusCode.Should().Be(HttpStatusCode.OK,
            "412 must be recoverable by re-reading, or the guard is a wall rather than a precondition");
    }

    [Fact]
    public async Task Creating_an_aggregate_does_not_try_to_advance_a_version_it_does_not_have_yet()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Fresh Aggregate Co");

        var supplierCode = await client.OwnSupplierCodeAsync();
        supplierCode.Should().StartWith("SUP-");

        (await VersionAsync(supplierCode)).Should().BeGreaterThan(0,
            "a new row starts at the column default rather than at zero");
    }

    [Fact]
    public async Task Nothing_in_the_change_set_escapes_attribution_unnoticed()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Attribution Co");
        var supplierCode = await client.OwnSupplierCodeAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplier = await db.Suppliers.Include(s => s.Contacts)
            .FirstAsync(s => s.ReferenceCode == supplierCode);
        supplier.AddContact("اسم", $"attr-{Guid.NewGuid():N}@example.sy", "+963900000005", null);

        db.UnattributedChildTypes().Should().BeEmpty(
            "a changed entity that is neither a versioned root nor a child of one would never bump anything");
    }

    [Fact]
    public async Task Deleting_a_versioned_root_deletes_it_rather_than_bumping_it()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var template = new MotsSupplierPortal.Domain.Notifications.NotificationTemplate
        {
            Id = Guid.CreateVersion7(),
            Type = "probe.delete." + Guid.NewGuid().ToString("N")[..8],
            TitleAr = "عنوان", TitleEn = "Title", BodyAr = "نص", BodyEn = "Body",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Add(template);
        await db.SaveChangesAsync();

        db.Remove(template);
        await db.SaveChangesAsync();

        (await db.Set<MotsSupplierPortal.Domain.Notifications.NotificationTemplate>()
            .AsNoTracking().CountAsync(t => t.Id == template.Id))
            .Should().Be(0, "a deleted versioned root must not come back as an update");
    }
}
