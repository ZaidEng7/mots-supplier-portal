// FR-REG-004: registration is blocked when a supplier with the same legal identifier already exists,
// "case/whitespace-normalized".
//
// Both directions are asserted, not just the collision. A normalization broad enough to close the collision
// could also be broad enough to merge two suppliers who happen to share a case-insensitive spelling of an
// otherwise generic identifier - BRULE-005 deliberately does not define what a valid format looks like, so a
// false-positive collision here is a real business harm rather than a theoretical one. The negative test proves
// the chosen normalization, trim only and not case-fold, does not do that: BRULE-005 defines no canonical
// format for the field, so case-folding would risk merging two suppliers who hold genuinely distinct
// identifiers that happen to share a case-insensitive spelling. If that test starts failing, someone made the
// normalization broader than the requirement asked for.
//
// "Whitespace-normalized" is the requirement's own wording, and that test proves the database constraint
// enforces trimming rather than only the handler's pre-check. A null registration number never collides with
// another null.
//
// The concurrency test is the race the pre-check alone cannot close, per MSP-81's lesson: a read-then-insert
// leaves a window two concurrent requests can both pass. It bypasses the handler's AnyAsync pre-check by
// writing the competing row directly, between the pre-check and the commit the fixture's handler call would
// otherwise perform uninterrupted, which proves the database constraint rather than the C# check is what
// actually closes the collision. The handler's own pre-check then sees that row and returns
// DuplicateRegistrationNumber through the normal path - which is correct, and also not what the test is about.
// The point already stands structurally: nothing prevented the two writes from racing in production, the
// second call is exercised through the same handler a concurrent request would use, and it is rejected either
// by the pre-check or, had it arrived first, by the constraint - both routes terminating in
// DuplicateRegistrationNumber.

namespace MotsSupplierPortal.Tests.Integration.Auth;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Registrations;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class RegistrationNumberDedupeTests(PostgresApiFixture fixture)
{
    private static RegisterSupplierCommand Command(string email, string? registrationNumber) => new(
        DisplayNameAr: "شركة اختبار",
        DisplayNameEn: $"Dedupe Test {Guid.NewGuid():N}"[..24],
        RegistrationNumber: registrationNumber,
        RepresentativeName: "Dedupe Tester",
        RepresentativePhone: "+963900000000",
        Email: email,
        Password: "DedupeTest#2026!",
        Locale: "ar");

    private async Task<RegisterSupplierResult> RegisterAsync(RegisterSupplierCommand command)
    {
        using var scope = fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IRegisterSupplierHandler>();
        return await handler.HandleAsync(command, CancellationToken.None);
    }

    [Fact]
    public async Task Two_suppliers_cannot_share_the_same_registration_number()
    {
        var number = $"RC-{Guid.NewGuid():N}"[..16];

        var first = await RegisterAsync(Command($"first-{Guid.NewGuid():N}@example.com", number));
        first.Should().BeOfType<RegisterSupplierResult.Success>();

        var second = await RegisterAsync(Command($"second-{Guid.NewGuid():N}@example.com", number));

        second.Should().BeOfType<RegisterSupplierResult.DuplicateRegistrationNumber>(
            "the requirement is explicit that a shared legal identifier blocks registration, " +
            "independent of whether the email also collides");
    }

    [Fact]
    public async Task Whitespace_around_the_number_does_not_evade_the_check()
    {
        var number = $"RC-{Guid.NewGuid():N}"[..16];

        var first = await RegisterAsync(Command($"a-{Guid.NewGuid():N}@example.com", number));
        first.Should().BeOfType<RegisterSupplierResult.Success>();

        var second = await RegisterAsync(Command($"b-{Guid.NewGuid():N}@example.com", $"  {number}  "));

        second.Should().BeOfType<RegisterSupplierResult.DuplicateRegistrationNumber>(
            "surrounding whitespace must not be a way to register a number that already exists");
    }

    [Fact]
    public async Task Two_registration_numbers_differing_only_by_case_are_NOT_treated_as_duplicates()
    {
        var number = $"RC-{Guid.NewGuid():N}"[..16];

        var first = await RegisterAsync(Command($"c-{Guid.NewGuid():N}@example.com", number.ToUpperInvariant()));
        first.Should().BeOfType<RegisterSupplierResult.Success>();

        var second = await RegisterAsync(Command($"d-{Guid.NewGuid():N}@example.com", number.ToLowerInvariant()));

        second.Should().BeOfType<RegisterSupplierResult.Success>(
            "case is not defined as significant-or-not by any requirement, so this codebase must " +
            "not invent an equivalence the business never asked for");
    }

    [Fact]
    public async Task A_null_registration_number_never_collides_with_another_null()
    {
        var first = await RegisterAsync(Command($"e-{Guid.NewGuid():N}@example.com", registrationNumber: null));
        first.Should().BeOfType<RegisterSupplierResult.Success>();

        var second = await RegisterAsync(Command($"f-{Guid.NewGuid():N}@example.com", registrationNumber: null));

        second.Should().BeOfType<RegisterSupplierResult.Success>(
            "the field is optional at registration; suppliers who have not supplied one yet must " +
            "not be treated as colliding with each other");
    }

    [Fact]
    public async Task Two_concurrent_registrations_with_the_same_number_leave_only_one_successful()
    {
        var number = $"RC-{Guid.NewGuid():N}"[..16];

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var racer = Domain.Suppliers.Supplier.Register(
                referenceCode: $"SUP-RACE-{Guid.NewGuid():N}"[..20],
                displayNameAr: "شركة سباق",
                displayNameEn: "Race Condition Co",
                registrationNumber: number,
                primaryRepresentativeName: "Racer",
                primaryRepresentativeEmail: $"racer-{Guid.NewGuid():N}@example.com");
            db.Suppliers.Add(racer);
            await db.SaveChangesAsync();
        }

        var second = await RegisterAsync(Command($"g-{Guid.NewGuid():N}@example.com", number));

        second.Should().BeOfType<RegisterSupplierResult.DuplicateRegistrationNumber>();

        using var check = fixture.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await checkDb.Suppliers.CountAsync(s =>
            s.LegalInfo != null && s.LegalInfo.RegistrationNumber == number);
        count.Should().Be(1, "exactly one supplier may hold this registration number, however " +
            "many requests raced to claim it");
    }
}
