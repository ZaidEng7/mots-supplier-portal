// A sign-out racing a refresh of the same session, and two refreshes racing on one token.
//
// Rotating a token and ending a session each used to read the tokens and write them in separate steps, with nothing
// stopping another request in between. A sign-out that read the family just before a refresh committed revoked the
// old token a second time, never saw the successor that refresh had issued, and left it live after a sign-out that
// was recorded as done. It also overwrote the old token's rotation time, so a later replay of that token no longer
// read as a rotated one. Two refreshes of one token both found it active and issued two successors: two live
// sessions where there had been one. Every one of those answered success.
//
// The requests are released at the same instant and each case is repeated over many rounds, because a race shows
// only when the two land inside each other's window and one attempt proves nothing either way. Every round signs in
// afresh, so each races a live session of its own, and does so as an account of its own, because sign-in is limited
// per address and twenty sign-ins to one address inside a minute would be refused.
//
// SIGN-OUT AND REFRESH TOGETHER. Whichever wins, nothing in the family is live afterwards: either the sign-out came
// first and the refresh found the session ended, or the refresh came first and the sign-out revoked its successor.
// When the refresh won, the token it rotated still carries the instant of that rotation, the same instant its
// successor was created at, because that is what lets a later replay be recognised as one.
//
// A SIGN-OUT ARRIVING WHILE A ROTATION IS HALF DONE. The rounds above depend on the two requests landing inside
// each other's window, which they often do not, so a sign-out that skipped the lock could still pass them. Here
// the moment is made rather than hoped for: the refresh is held at the point where it has retired the old token and
// is about to store the successor, and the sign-out is sent while it is held. A sign-out that does not wait for the
// refresh reads the family before the successor exists, so when the refresh is let go the successor is left live and
// no sign-out is recorded. One that waits sees the successor and ends it.
//
// TWO REFRESHES TOGETHER. Exactly one is answered with a new session and exactly one successor exists. The other is
// refused as superseded, with the cookie left alone, because the browser may already hold the successor the first
// one set.

namespace MotsSupplierPortal.Tests.Integration.Auth;

using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class SessionConcurrencyTests(PostgresApiFixture fixture)
{
    private const int Rounds = 20;

    private async Task<string> StaffEmailAsync()
    {
        var (_, email) = await StaffTestClient.CreateWithEmailAsync(fixture, Roles.OnboardingReviewer, null);
        return email;
    }

    private async Task<List<RefreshToken>> FamilyAsync(Guid familyId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RefreshTokens.AsNoTracking().Where(t => t.FamilyId == familyId).ToListAsync();
    }

    [Fact]
    public async Task A_sign_out_sent_with_a_refresh_of_the_same_token_never_leaves_a_live_token_in_the_family()
    {
        for (var round = 1; round <= Rounds; round++)
        {
            var (_, cookie) = await SessionSteps.SignInAsync(fixture, await StaffEmailAsync(), StaffTestClient.Password);
            var familyId = await SessionSteps.FamilyOfAsync(fixture, cookie!);
            var presentedHash = TokenHasher.Hash(SessionSteps.TokenOf(cookie!));

            var answers = await SessionSteps.AtOnceAsync(
                async () => (await SessionSteps.RefreshAsync(fixture, cookie)).Response,
                () => SessionSteps.LogoutAsync(fixture, cookie));

            answers[1].StatusCode.Should().Be(HttpStatusCode.NoContent, $"round {round}: signing out always answers 204");

            var family = await FamilyAsync(familyId);
            family.Where(t => t.RevokedAt == null).Should().BeEmpty(
                $"round {round}: the sign-out ended the session whichever request reached it first");

            if (answers[0].StatusCode == HttpStatusCode.OK)
            {
                var presented = family.Single(t => t.TokenHash == presentedHash);
                family.Should().ContainSingle(t => t.Id != presented.Id && t.CreatedAt == presented.RevokedAt,
                    $"round {round}: the refresh won, so the token it rotated keeps the instant of that rotation, "
                    + "and a later replay of it can still be told from an ended session");
            }
        }
    }

    [Fact]
    public async Task Two_refreshes_of_one_token_sent_together_issue_exactly_one_successor()
    {
        for (var round = 1; round <= Rounds; round++)
        {
            var (_, cookie) = await SessionSteps.SignInAsync(fixture, await StaffEmailAsync(), StaffTestClient.Password);
            var familyId = await SessionSteps.FamilyOfAsync(fixture, cookie!);

            var answers = await SessionSteps.AtOnceAsync(
                async () => (await SessionSteps.RefreshAsync(fixture, cookie)).Response,
                async () => (await SessionSteps.RefreshAsync(fixture, cookie)).Response);

            answers.Count(a => a.StatusCode == HttpStatusCode.OK).Should().Be(1,
                $"round {round}: one token can be exchanged once, however many requests carry it");

            var refused = answers.Single(a => a.StatusCode != HttpStatusCode.OK);
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await SessionSteps.CodeOf(refused)).Should().Be("REFRESH_SUPERSEDED",
                $"round {round}: the loser carried the token the winner had just rotated");
            SessionSteps.SetCookieFor(refused).Should().BeNull(
                $"round {round}: the browser may already hold the winner's successor");

            var family = await FamilyAsync(familyId);
            family.Should().HaveCount(2, $"round {round}: the sign-in's token and exactly one successor");
            family.Where(t => t.RevokedAt == null).Should().ContainSingle($"round {round}: one live session, not two");
        }
    }

    // Holds the first statement that stores a session token, which in a refresh is the successor, until released.
    private sealed class SuccessorStoreGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _held;

        public Task Reached => _reached.Task;

        public void Release() => _released.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            await HoldIfSuccessorAsync(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            await HoldIfSuccessorAsync(command);
            return result;
        }

        private async Task HoldIfSuccessorAsync(DbCommand command)
        {
            var storesToken = command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("user_session", StringComparison.OrdinalIgnoreCase);

            if (storesToken && Interlocked.Exchange(ref _held, 1) == 0)
            {
                _reached.TrySetResult();
                await _released.Task;
            }
        }
    }

    [Fact]
    public async Task A_sign_out_sent_while_a_refresh_is_storing_its_successor_waits_and_ends_that_successor()
    {
        var email = await StaffEmailAsync();
        var userId = await SessionSteps.UserIdAsync(fixture, email);
        var (_, cookie) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        var familyId = await SessionSteps.FamilyOfAsync(fixture, cookie!);

        var gate = new SuccessorStoreGate();
        await using var held = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(gate))));

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshRequest.Headers.Add("Cookie", cookie);
        var refresh = held.CreateClient().SendAsync(refreshRequest);

        await gate.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var signOut = SessionSteps.LogoutAsync(fixture, cookie);
        await Task.Delay(TimeSpan.FromSeconds(1));
        gate.Release();

        (await refresh).StatusCode.Should().Be(HttpStatusCode.OK, "the refresh began first and finishes");
        (await signOut).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var family = await FamilyAsync(familyId);
        family.Should().HaveCount(2, "the sign-in's token and the successor the refresh stored");
        family.Where(t => t.RevokedAt == null).Should().BeEmpty(
            "the sign-out waited for the refresh, saw its successor and ended it");
        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.Logout)).Should().Be(1,
            "the sign-out ended a session, so it is recorded");
    }
}
