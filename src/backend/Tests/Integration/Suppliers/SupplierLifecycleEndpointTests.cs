// The post-approval lifecycle through the real endpoints, and revocation proven by an actual sign-in and an actual
// refresh.
//
// Checking the lifecycle column would prove nothing here. A supplier marked deactivated whose users can still
// refresh their way to a valid session is the same defect class as a second factor that enrolled but never
// challenged, so the revocation tests go through the authentication endpoints.
//
// The supplier is forced to active rather than driven through the full onboarding journey, because this is about
// what happens AFTER approval and rebuilding that journey per test would make these tests fail for reasons that
// belong to other work.
//
//
// THE REFRESH CASE IS THE HALF A STATE FLAG WOULD MISS
//
// A user already holding a refresh cookie has a live credential in the browser. If deactivation only changed the
// supplier row, they would keep renewing access indefinitely and the deactivation would be cosmetic.
//
// The cookie is forwarded BY HAND rather than through the client's cookie container, because the cookie is marked
// secure unconditionally and the platform's container refuses to return such a cookie over plain HTTP, unlike a
// browser, which treats the local host as trustworthy. That browser behaviour was verified against a real browser;
// the test host is not one, so the cookie is attached explicitly rather than the test silently proving nothing.
//
// And the ROTATED cookie is carried forward, not the original. Refresh rotates, so the first call consumed the
// original, and reusing it would fail whether or not deactivation revoked anything: a test that passes in both the
// fixed and the broken state. Verified, with revocation removed entirely the earlier version still passed.
//
//
// THE OTHER ASSERTIONS
//
// Deactivating an active supplier skips suspension, which is legal.
//
// A field-level validation failure answers as unprocessable rather than as a bad request, which stays for a
// malformed or unmodelled body and is a different kind of wrong.
//
// And the permission guard is exercised rather than assumed: a supplier administrator deliberately does not hold
// the lifecycle permission, because a supplier must not be able to suspend or reactivate itself, let alone anyone
// else.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierLifecycleEndpointTests(PostgresApiFixture fixture)
{
    private sealed record LifecycleResponse(string LifecycleState);

    private async Task<(string ReferenceCode, string Email)> ApprovedSupplierAsync(string name)
    {
        var email = $"lifecycle-{Guid.NewGuid():N}@example.com";
        var client = fixture.CreateClient();

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            displayNameAr = "شركة اختبار",
            displayNameEn = name,
            registrationNumber = $"RC-{Guid.NewGuid():N}"[..12],
            representativeName = "Lifecycle Tester",
            representativePhone = "+963900000000",
            email,
            password = SupplierTestClient.Password,
        });

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);

        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

        var user = await db.Users.FirstAsync(u => u.Email == email);
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(p => p
            .SetProperty(u => u.EmailConfirmed, true));

        return (supplier.ReferenceCode, email);
    }

    private static async Task<HttpResponseMessage> TransitionAsync(
        HttpClient staff, string referenceCode, string action, string reason) =>
        await staff.PostAsJsonAsync($"/api/v1/review/{referenceCode}/{action}", new { reason });

    [Fact]
    public async Task Full_lifecycle_active_suspended_reactivated_suspended_deactivated_is_audited()
    {
        var (referenceCode, _) = await ApprovedSupplierAsync($"Lifecycle Full {Guid.NewGuid():N}"[..30]);
        var staff = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var suspend = await TransitionAsync(staff, referenceCode, "suspend", "Sanctions screening hit");
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);
        (await suspend.Content.ReadFromJsonAsync<LifecycleResponse>())!.LifecycleState.Should().Be("Suspended");

        var reactivate = await TransitionAsync(staff, referenceCode, "reactivate", "Screening cleared");
        (await reactivate.Content.ReadFromJsonAsync<LifecycleResponse>())!.LifecycleState.Should().Be("Active");

        var suspendAgain = await TransitionAsync(staff, referenceCode, "suspend", "Repeated non-performance");
        (await suspendAgain.Content.ReadFromJsonAsync<LifecycleResponse>())!.LifecycleState.Should().Be("Suspended");

        var deactivate = await TransitionAsync(staff, referenceCode, "deactivate", "Contract terminated");
        (await deactivate.Content.ReadFromJsonAsync<LifecycleResponse>())!.LifecycleState.Should().Be("Deactivated");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trail = await db.AuditLogs
            .Where(a => a.ReferenceCode == referenceCode)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
            .Select(a => new { a.Action, a.FromState, a.ToState, a.Reason })
            .ToListAsync();

        trail.Select(t => t.Action).Should().ContainInOrder(
            "supplier_suspended", "supplier_reactivated", "supplier_suspended", "supplier_deactivated");

        trail.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Reason),
            "BRULE-096: every lifecycle transition records why");
        trail.Should().OnlyContain(t => t.FromState != null && t.ToState != null,
            "an audit row that does not say what changed cannot answer a review's question");
    }

    [Fact]
    public async Task A_deactivated_suppliers_user_cannot_log_in()
    {
        var (referenceCode, email) = await ApprovedSupplierAsync($"Lifecycle Login {Guid.NewGuid():N}"[..30]);
        var staff = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var anonymous = fixture.CreateClient();
        var before = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = SupplierTestClient.Password });
        before.StatusCode.Should().Be(HttpStatusCode.OK,
            "the login must work first, or this test proves nothing about deactivation");

        await TransitionAsync(staff, referenceCode, "suspend", "Prior suspension");
        await TransitionAsync(staff, referenceCode, "deactivate", "Ceased trading");

        var after = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = SupplierTestClient.Password });

        after.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "BRULE-008: a deactivated supplier's logins are revoked, not merely marked");
    }

    [Fact]
    public async Task A_deactivated_suppliers_live_session_cannot_be_refreshed()
    {
        var (referenceCode, email) = await ApprovedSupplierAsync($"Lifecycle Refresh {Guid.NewGuid():N}"[..30]);
        var staff = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var session = fixture.CreateClient();
        var login = await session.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = SupplierTestClient.Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshCookie = login.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("mots_refresh_token=", StringComparison.Ordinal))
            .Split(';')[0];
        session.DefaultRequestHeaders.Add("Cookie", refreshCookie);

        var refreshBefore = await session.PostAsync("/api/v1/auth/refresh", null);
        refreshBefore.StatusCode.Should().Be(HttpStatusCode.OK,
            "the session must be refreshable first, or the assertion below is vacuous");

        var rotated = refreshBefore.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("mots_refresh_token=", StringComparison.Ordinal))
            .Split(';')[0];
        session.DefaultRequestHeaders.Remove("Cookie");
        session.DefaultRequestHeaders.Add("Cookie", rotated);

        await TransitionAsync(staff, referenceCode, "suspend", "Prior suspension");
        await TransitionAsync(staff, referenceCode, "deactivate", "Ceased trading");

        var refreshAfter = await session.PostAsync("/api/v1/auth/refresh", null);

        refreshAfter.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "an existing session must not survive deactivation - otherwise the supplier is " +
            "deactivated in the database and fully operational in the browser");
    }

    [Fact]
    public async Task An_illegal_transition_is_refused_with_the_domains_own_reason()
    {
        var (referenceCode, _) = await ApprovedSupplierAsync($"Lifecycle Illegal {Guid.NewGuid():N}"[..30]);
        var staff = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var response = await TransitionAsync(staff, referenceCode, "deactivate", "Skipping a step");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the request is well formed but conflicts with the supplier's state");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Suspended",
            "NFR-CMP-003/BRULE-097: the caller is told which state is required, not merely refused");
    }

    [Fact]
    public async Task A_transition_without_a_reason_is_rejected()
    {
        var (referenceCode, _) = await ApprovedSupplierAsync($"Lifecycle Reason {Guid.NewGuid():N}"[..30]);
        var staff = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var response = await TransitionAsync(staff, referenceCode, "suspend", "   ");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "BRULE-096: the reason is mandatory");
    }

    [Fact]
    public async Task A_supplier_user_cannot_move_another_suppliers_lifecycle()
    {
        var (referenceCode, _) = await ApprovedSupplierAsync($"Lifecycle Guard {Guid.NewGuid():N}"[..30]);
        var supplierClient = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Lifecycle Guard Other");

        var response = await TransitionAsync(supplierClient, referenceCode, "suspend", "Not my call");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }
}
