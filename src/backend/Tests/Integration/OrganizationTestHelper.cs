using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>EPIC-07: RFQ tests need a real Organization row to scope a procurement staff test
/// client to (StaffTestClient.CreateAsync(fixture, role, organizationId)) - there is no HTTP
/// endpoint that creates a bare buying-entity Organization outside the admin
/// OrganizationEndpoints flow tested elsewhere, so this creates one directly via the domain
/// factory, same pattern as SupplierLifecycleEndpointTests' direct db manipulation for
/// out-of-scope-of-the-test-itself setup.</summary>
public static class OrganizationTestHelper
{
    public static async Task<Organization> CreateOrganizationAsync(PostgresApiFixture fixture, string? nameEn = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var org = Organization.Create(
            "منظمة اختبار", nameEn ?? $"Test Org {Guid.NewGuid():N}", OrganizationType.Hotel);
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        return org;
    }
}
