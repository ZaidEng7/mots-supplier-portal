// Importing suppliers from the ERP, against a real database.
//
// THE SECOND TEST IS THE ONE THAT EARNS ITS KEEP. This button will be pressed twice - by somebody checking it
// worked, by somebody who did not see the first result, by a re-run after a fix. A second run that created a
// second supplier and a second account for the same company would corrupt the registry the ministry reads, and
// it would do it silently, because both rows would look correct on their own.
//
// THE ACCOUNT IS ASSERTED AS SIGN-IN-READY, NOT MERELY AS EXISTING. Sign-in refuses an unconfirmed address, so an
// account created without EmailConfirmed can never be used - and with no invitation email being sent, nobody
// would ever confirm it. That would be an import that reports eighty accounts created and delivers eighty
// accounts nobody can enter. The role matters for the same reason: a supplier with no role sees nothing.
//
// NOBODY IS LEFT OUT. A supplier with no email arrives with a placeholder login that cannot deliver, a disabled
// one arrives suspended, and a later run that brings a real address moves the LOGIN onto it, not just the contact -
// otherwise the supplier's actual person could never sign in. The reverse is guarded too: a run where the ERP has
// lost the address must not replace a real one with a placeholder.
//
// THE PASSWORD IS CHECKED BEFORE THE FIRST WRITE, so a misconfigured one costs nothing rather than leaving the
// registry half-populated. The test uses a password the product's own rules reject, rather than a made-up
// assertion about what those rules are.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ErpImportRunTests(PostgresApiFixture fixture)
{
    private const string Password = "Wattle-Harbour-Quince-72";

    private sealed class FixedSource(params ErpSupplier[] suppliers) : IErpSupplierSource
    {
        public Task<IReadOnlyList<ErpSupplier>> ListSuppliersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ErpSupplier>>(suppliers);
    }

    private static ErpSupplier Supplier(string id, string? email, string? name = null, bool disabled = false) =>
        new(id, name ?? id, "Local", "Company", "TAX-" + id, "Syria", email, "+963 11 555 0000", disabled, "SYP",
            null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static async Task<ErpImportRunReport> RunAsync(
        PostgresApiFixture fixture, IErpSupplierSource source, string password = Password)
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        var handler = new RunErpImportHandler(
            source,
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            Options.Create(new ErpImportOptions { InitialPassword = password }),
            scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
            NullLogger<RunErpImportHandler>.Instance);

        return await handler.HandleAsync(ErpImportTrigger.Manual, CancellationToken.None);
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}";

    [Fact]
    public async Task A_supplier_arrives_approved_with_an_account_that_can_sign_in()
    {
        var externalId = Unique("ERP-CREATE");
        var email = $"{externalId.ToLowerInvariant()}@sgtest.example";

        var report = await RunAsync(fixture, new FixedSource(Supplier(externalId, email, "Damascus Supplies")));

        report.Created.Should().Be(1);
        report.Rows[0].ReferenceCode.Should().NotBeNull();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var supplier = await db.Suppliers
            .Include(s => s.Representatives)
            .SingleAsync(s => s.ExternalId == externalId);

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Approved);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
        supplier.SyncStatus.Should().Be(SupplierSyncStatus.Synced);
        supplier.DisplayNameAr.Should().Be("Damascus Supplies", "the ERP holds one name");

        var user = await users.FindByEmailAsync(email);
        user.Should().NotBeNull();
        user!.EmailConfirmed.Should().BeTrue(
            "sign-in refuses an unconfirmed address, and with no invitation sent nobody would ever confirm it");
        (await users.IsInRoleAsync(user, Roles.SupplierAdmin)).Should().BeTrue();
        supplier.Representatives[0].UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Running_it_twice_updates_rather_than_duplicating()
    {
        var externalId = Unique("ERP-TWICE");
        var email = $"{externalId.ToLowerInvariant()}@sgtest.example";
        var source = new FixedSource(Supplier(externalId, email, "Homs Linen"));

        var first = await RunAsync(fixture, source);
        var second = await RunAsync(fixture, source);

        first.Created.Should().Be(1);
        second.Created.Should().Be(0);
        second.Updated.Should().Be(1, "the same company must not become two rows in the ministry's registry");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        (await db.Suppliers.CountAsync(s => s.ExternalId == externalId)).Should().Be(1);
        (await users.Users.CountAsync(u => u.Email == email)).Should().Be(1);
        second.Rows[0].ReferenceCode.Should().Be(first.Rows[0].ReferenceCode);
    }

    [Fact]
    public async Task A_supplier_with_no_email_arrives_with_a_placeholder_login()
    {
        var externalId = Unique("ERP-NOEMAIL");

        var report = await RunAsync(fixture, new FixedSource(Supplier(externalId, email: null)));

        report.Created.Should().Be(1, "every supplier in the ERP is meant to appear in the portal");
        report.Refused.Should().Be(0);
        report.Rows[0].Notes.Should().ContainMatch("*placeholder*");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var supplier = await db.Suppliers.Include(s => s.Representatives).SingleAsync(s => s.ExternalId == externalId);
        var user = await users.FindByIdAsync(supplier.Representatives[0].UserId!.Value.ToString());

        user!.Email.Should().EndWith("@erp-import.invalid", "a placeholder must never be able to deliver");
        ErpImportAdmission.IsPlaceholder(supplier.Representatives[0].Email).Should().BeTrue();
    }

    [Fact]
    public async Task A_supplier_disabled_in_the_erp_arrives_suspended()
    {
        var externalId = Unique("ERP-DISABLED");

        await RunAsync(fixture, new FixedSource(
            Supplier(externalId, $"{externalId.ToLowerInvariant()}@sgtest.example", disabled: true)));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplier = await db.Suppliers.SingleAsync(s => s.ExternalId == externalId);
        supplier.LifecycleState.Should().Be(
            SupplierLifecycleState.Suspended,
            "the preview promised this, and an active one could be invited to a tender Seven Gates would not honour");
    }

    [Fact]
    public async Task A_real_email_arriving_later_moves_the_login_off_the_placeholder()
    {
        var externalId = Unique("ERP-LATEREMAIL");
        var realEmail = $"{externalId.ToLowerInvariant()}@sgtest.example";

        await RunAsync(fixture, new FixedSource(Supplier(externalId, email: null)));
        var second = await RunAsync(fixture, new FixedSource(Supplier(externalId, realEmail)));

        second.Updated.Should().Be(1);
        second.Rows[0].Notes.Should().ContainMatch("*login moved from placeholder*");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var supplier = await db.Suppliers.Include(s => s.Representatives).SingleAsync(s => s.ExternalId == externalId);
        var user = await users.FindByIdAsync(supplier.Representatives[0].UserId!.Value.ToString());

        user!.Email.Should().Be(
            realEmail,
            "a contact updated while the login stayed on .invalid would leave the real person unable to sign in");
        user.UserName.Should().Be(realEmail);
        user.EmailConfirmed.Should().BeTrue("changing the address resets confirmation, and sign-in refuses without it");
        supplier.Representatives[0].Email.Should().Be(realEmail);
        (await users.CheckPasswordAsync(user, Password)).Should().BeTrue("moving the login must not lose the password");
    }

    [Fact]
    public async Task A_placeholder_never_overwrites_a_real_address_already_on_file()
    {
        var externalId = Unique("ERP-KEEPREAL");
        var realEmail = $"{externalId.ToLowerInvariant()}@sgtest.example";

        await RunAsync(fixture, new FixedSource(Supplier(externalId, realEmail)));
        await RunAsync(fixture, new FixedSource(Supplier(externalId, email: null)));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var supplier = await db.Suppliers.Include(s => s.Representatives).SingleAsync(s => s.ExternalId == externalId);
        supplier.Representatives[0].Email.Should().Be(
            realEmail,
            "the ERP losing an address is not a reason to replace a real one with an address that cannot deliver");
        (await users.FindByEmailAsync(realEmail)).Should().NotBeNull();
    }

    [Fact]
    public async Task A_run_of_several_finishes_all_of_them()
    {
        var good = Unique("ERP-MIXED-OK");
        var bad = Unique("ERP-MIXED-NOEMAIL");

        var report = await RunAsync(fixture, new FixedSource(
            Supplier(good, $"{good.ToLowerInvariant()}@sgtest.example"),
            Supplier(bad, email: null)));

        report.ErpSupplierCount.Should().Be(2);
        report.Created.Should().Be(2, "the one with no email gets a placeholder instead of being left out");
        report.Refused.Should().Be(0);
        report.Failed.Should().Be(0);
        (report.Created + report.Updated + report.Refused + report.Failed).Should().Be(report.ErpSupplierCount);
    }

    [Fact]
    public async Task A_password_the_product_would_refuse_stops_the_run_before_anything_is_written()
    {
        var externalId = Unique("ERP-WEAKPW");

        var act = () => RunAsync(
            fixture,
            new FixedSource(Supplier(externalId, $"{externalId.ToLowerInvariant()}@sgtest.example")),
            password: "password");

        await act.Should().ThrowAsync<ErpImportNotConfiguredException>();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Suppliers.AnyAsync(s => s.ExternalId == externalId)).Should().BeFalse(
            "discovering this on the first account leaves the registry half-populated");
    }
}
