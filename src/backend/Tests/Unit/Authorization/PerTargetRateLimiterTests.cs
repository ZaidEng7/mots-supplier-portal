using FluentAssertions;
using MotsSupplierPortal.Api.Authorization;

namespace MotsSupplierPortal.Tests.Unit.Authorization;

/// <summary>SECURITY-ARCHITECTURE.md §5.1: proves the per-target dimension is a genuinely
/// independent budget, not just a duplicate of the per-IP middleware policy - two different
/// targets never share a budget, and hitting one target's limit never blocks another target.
/// (Can't be shown end-to-end over HTTP from a single test machine/IP - both dimensions would
/// exhaust in lockstep since every request shares one source IP - so this exercises the class
/// directly instead.)</summary>
public sealed class PerTargetRateLimiterTests
{
    [Fact]
    public void Tenth_request_for_a_target_succeeds_eleventh_is_blocked()
    {
        using var limiter = new PerTargetRateLimiter();

        for (var i = 0; i < 10; i++)
        {
            limiter.TryAcquire("login", "victim@example.com").Should().BeTrue($"request {i + 1} is within the 10/min budget");
        }

        limiter.TryAcquire("login", "victim@example.com").Should().BeFalse("the 11th request in the same window exceeds the budget");
    }

    [Fact]
    public void Different_targets_have_independent_budgets()
    {
        using var limiter = new PerTargetRateLimiter();

        for (var i = 0; i < 10; i++)
        {
            limiter.TryAcquire("login", "victim@example.com").Should().BeTrue();
        }
        limiter.TryAcquire("login", "victim@example.com").Should().BeFalse("victim@example.com's budget is exhausted");

        // A distributed-IP attacker probing a DIFFERENT account is unaffected by the first
        // account's exhausted budget - this is exactly the gap SECURITY-ARCHITECTURE §5.1 flags
        // an IP-only limiter as missing.
        limiter.TryAcquire("login", "someone-else@example.com").Should().BeTrue();
    }

    [Fact]
    public void Different_surfaces_have_independent_budgets_for_the_same_target()
    {
        using var limiter = new PerTargetRateLimiter();

        // "login", not "register": NFR-SEC-009 gave "register" its own tighter 5/min budget, so a
        // 10-iteration loop against it would no longer prove what this test is about (it would
        // exhaust the budget early and fail for the wrong reason). Any two DIFFERENT surfaces
        // demonstrate the point; "login" keeps this test's iteration count meaningful against the
        // still-10/min default.
        for (var i = 0; i < 10; i++)
        {
            limiter.TryAcquire("login", "shared@example.com").Should().BeTrue();
        }
        limiter.TryAcquire("login", "shared@example.com").Should().BeFalse();

        // Same email, different surface (e.g. resend-verification) - not the same budget, so one
        // surface being hammered doesn't lock a legitimate user out of an unrelated one.
        limiter.TryAcquire("resend-verification", "shared@example.com").Should().BeTrue();
    }

    [Fact]
    public void Registration_surface_has_its_own_tighter_budget()
    {
        // NFR-SEC-009: registration is more consequential per-request than login (writes rows,
        // sends email), so it gets a lower budget than the shared 10/min default - proven directly
        // against the class here, complementing RegistrationRateLimitTests.cs's HTTP-level proof.
        using var limiter = new PerTargetRateLimiter();

        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquire("register", "abuse-target@example.com").Should().BeTrue($"request {i + 1} is within the 5/min registration budget");
        }
        limiter.TryAcquire("register", "abuse-target@example.com").Should().BeFalse("the 6th request in the same window exceeds the tighter registration budget");
    }
}
