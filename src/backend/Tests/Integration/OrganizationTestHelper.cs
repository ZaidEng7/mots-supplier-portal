// A real buying body for tender tests to scope a staff client to.
//
// No endpoint creates a bare one outside the administration flow that is tested elsewhere, so this creates one
// through the domain factory directly, which is the same pattern other suites use for setup outside the scope of
// the test itself.
//
// Its public code comes from the real allocator, so a test organization carries a real code and the counter moves
// the way it does in production. A literal would collide on the unique index the moment two tests ran.

namespace MotsSupplierPortal.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class OrganizationTestHelper
{
    public static async Task<Organization> CreateOrganizationAsync(PostgresApiFixture fixture, string? nameEn = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var org = Organization.Create(
            await MotsSupplierPortal.Infrastructure.Registrations.ReferenceCodeGenerator.NextCodeAsync(db, "ORG", CancellationToken.None),
            "منظمة اختبار", nameEn ?? $"Test Org {Guid.NewGuid():N}", OrganizationType.Hotel);
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        return org;
    }
}
