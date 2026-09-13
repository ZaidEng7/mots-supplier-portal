// The per-target rate limit is a genuinely independent budget, not a duplicate of the per-address one.
//
// Two different targets never share a budget, and exhausting one target's limit never blocks another.
//
// This cannot be shown end to end over HTTP from a single machine, because both dimensions would exhaust in
// lockstep when every request shares one source address, so it exercises the class directly.
//
// The case that matters is an attacker spread across addresses probing a DIFFERENT account: they are unaffected
// by the first account's exhausted budget, which is exactly the gap the written security architecture flags an
// address-only limiter as missing.
//
// The same address on a different surface is also a different budget, so one surface being hammered does not lock
// a legitimate user out of an unrelated one.
//
// The iteration tests use the sign-in surface rather than registration, because registration was given its own
// tighter budget and a ten-iteration loop against it would exhaust that budget early and fail for the wrong
// reason. Any two different surfaces demonstrate the point; this one keeps the iteration count meaningful
// against the shared default.
//
// And registration's tighter budget is proven directly against the class here, because it is more consequential
// per request than a sign-in: it writes rows and sends mail. The HTTP-level proof lives in its own suite.

namespace MotsSupplierPortal.Tests.Unit.Authorization;

using FluentAssertions;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Infrastructure.Observability;

public sealed class PerTargetRateLimiterTests
{
    [Fact]
    public void Tenth_request_for_a_target_succeeds_eleventh_is_blocked()
    {
        using var limiter = new PerTargetRateLimiter(new AppMetrics());

        for (var i = 0; i < 10; i++)
        {
            limiter.TryAcquire("login", "victim@example.com").Should().BeTrue($"request {i + 1} is within the 10/min budget");
        }

        limiter.TryAcquire("login", "victim@example.com").Should().BeFalse("the 11th request in the same window exceeds the budget");
    }

    [Fact]
    public void Different_targets_have_independent_budgets()
    {
        using var limiter = new PerTargetRateLimiter(new AppMetrics());

        for (var i = 0; i < 10; i++)
        {
            limiter.TryAcquire("login", "victim@example.com").Should().BeTrue();
        }
        limiter.TryAcquire("login", "victim@example.com").Should().BeFalse("victim@example.com's budget is exhausted");

        limiter.TryAcquire("login", "someone-else@example.com").Should().BeTrue();
    }

    [Fact]
    public void Different_surfaces_have_independent_budgets_for_the_same_target()
    {
        using var limiter = new PerTargetRateLimiter(new AppMetrics());

        for (var i = 0; i < 10; i++)
        {
            limiter.TryAcquire("login", "shared@example.com").Should().BeTrue();
        }
        limiter.TryAcquire("login", "shared@example.com").Should().BeFalse();

        limiter.TryAcquire("resend-verification", "shared@example.com").Should().BeTrue();
    }

    [Fact]
    public void Registration_surface_has_its_own_tighter_budget()
    {
        using var limiter = new PerTargetRateLimiter(new AppMetrics());

        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquire("register", "abuse-target@example.com").Should().BeTrue($"request {i + 1} is within the 5/min registration budget");
        }
        limiter.TryAcquire("register", "abuse-target@example.com").Should().BeFalse("the 6th request in the same window exceeds the tighter registration budget");
    }
}
