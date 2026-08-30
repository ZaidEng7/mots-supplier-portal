using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Task #11: two real gaps found while sweeping every endpoint's actual authorization, not what its
/// comments claimed.
///
/// <para><b>Gap 1 - AllowAnonymous silently overrides RequireAuthorization.</b> AuthEndpoints.cs
/// declared the whole /api/v1/auth group AllowAnonymous (correct for login/refresh/etc, which have
/// no session yet) and then re-declared .RequireAuthorization() individually on the three session
/// routes, with a comment claiming that override worked. It does not: ASP.NET Core's
/// AuthorizationMiddleware short-circuits on the mere PRESENCE of IAllowAnonymous metadata,
/// regardless of what IAuthorizeData is also present. Verified directly before fixing: GET
/// /api/v1/auth/sessions and POST /api/v1/auth/sessions/revoke-all both returned 200 with no
/// Authorization header - only the handlers' own `scope.UserId is null` guards (returning an empty
/// page / revokedCount 0) kept this from leaking real data. Fixed by moving AllowAnonymous off the
/// group and onto only the actually-public routes individually.</para>
///
/// <para><b>Gap 2 - "is staff" was trusted as "holds document.review".</b>
/// GetDocumentDownloadUrlHandler treated scope.SupplierId is null (i.e. any staff user of any role)
/// as sufficient to download any supplier's document, on the strength of a comment claiming the
/// ENDPOINT enforced document.review. The endpoint was mapped with bare .RequireAuthorization() -
/// no permission at all. Fixed by checking the real document.review permission claim
/// (IScopeContext.HasPermission) instead of the is-staff proxy for it.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EndpointAuthorizationGapTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Listing_sessions_without_any_credentials_is_rejected_not_served_empty()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an anonymous caller must be rejected by the auth pipeline itself, not merely handed an " +
            "empty page by the handler's own null-check - those look identical in the response but " +
            "only one of them means the endpoint is actually gated");
    }

    [Fact]
    public async Task Revoking_all_sessions_without_any_credentials_is_rejected_not_a_silent_no_op()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/sessions/revoke-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoking_one_session_without_any_credentials_is_rejected()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsync($"/api/v1/auth/sessions/{Guid.NewGuid()}/revoke", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "this one previously returned 404 (the handler's own scope.UserId-is-null guard), which " +
            "is a plausible-looking response for a caller with no idea the auth pipeline never ran");
    }

    private async Task<(Guid SupplierId, Guid DocumentId)> SeedApprovedDocumentAsync()
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, $"Doc Gap {Guid.NewGuid():N}"[..20]);
        _ = client;

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplierId = await db.Users
            .Where(u => u.SupplierId != null)
            .OrderByDescending(u => u.Id)
            .Select(u => u.SupplierId!.Value)
            .FirstAsync();

        var typeId = await db.DocumentTypes.Select(t => t.Id).FirstAsync();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        var document = SupplierDocument.CreatePendingScan(
            supplierId, typeId, 1, "quarantine/key", "cert.pdf", "application/pdf", 2048,
            Guid.CreateVersion7(), issueDate: null, expiryDate: today.AddDays(90),
            expiryTracked: true, today: today);
        document.MarkScanClean("clean/key");
        document.Approve(Guid.CreateVersion7());

        db.SupplierDocuments.Add(document);
        await db.SaveChangesAsync();

        return (supplierId, document.Id);
    }

    [Fact]
    public async Task Staff_without_document_review_cannot_download_another_supplier_s_document()
    {
        var (_, documentId) = await SeedApprovedDocumentAsync();

        // ProcurementOfficer holds rfq.publish only (Permissions.cs DefaultPermissions) - no
        // document.review. Before the fix, being staff at all (any role) was sufficient.
        var staff = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);

        var response = await staff.GetAsync($"/api/v1/documents/{documentId}/download-url");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "NotFoundOrForbidden is intentionally indistinguishable from 'does not exist' " +
            "(FR-DOC-008) - the point is that a role with no document.review gets nothing either way");
    }

    [Fact]
    public async Task Staff_with_document_review_can_download_another_supplier_s_document()
    {
        var (_, documentId) = await SeedApprovedDocumentAsync();

        // OnboardingReviewer holds document.review (Permissions.cs DefaultPermissions) - the
        // legitimate case the fix must not have broken.
        var staff = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var response = await staff.GetAsync($"/api/v1/documents/{documentId}/download-url");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_owning_supplier_can_still_download_its_own_document_with_no_special_permission()
    {
        // supplier_user holds no document.review permission either (Permissions.cs) - ownership
        // alone must still be sufficient, which is the reason the fix could not just be
        // .RequirePermission(DocumentReview) at the endpoint.
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Doc Owner {Guid.NewGuid():N}"[..20]);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplierId = await db.Users
            .Where(u => u.SupplierId != null)
            .OrderByDescending(u => u.Id)
            .Select(u => u.SupplierId!.Value)
            .FirstAsync();

        var typeId = await db.DocumentTypes.Select(t => t.Id).FirstAsync();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var document = SupplierDocument.CreatePendingScan(
            supplierId, typeId, 1, "quarantine/key", "cert.pdf", "application/pdf", 2048,
            Guid.CreateVersion7(), issueDate: null, expiryDate: today.AddDays(90),
            expiryTracked: true, today: today);
        document.MarkScanClean("clean/key");
        document.Approve(Guid.CreateVersion7());
        db.SupplierDocuments.Add(document);
        await db.SaveChangesAsync();

        var response = await client.GetAsync($"/api/v1/documents/{document.Id}/download-url");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
