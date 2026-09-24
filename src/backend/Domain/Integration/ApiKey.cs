// A credential belonging to another system rather than to a person.
//
// WHY THIS IS NOT A USER ACCOUNT. The ministry's dashboard pulls the supplier and tender feeds on a schedule,
// and everything a user account carries - a password to rotate, an email to reach, a role, a TOTP enrolment -
// is either useless to a nightly job or actively in the way. system_admin requires a second factor, which a
// job at 3am cannot answer, so the account that could read every feed is precisely the account no job can sign
// in as. A key is a row, a hash and a set of permissions, and it can be revoked without deleting a person.
//
// THE PREFIX IS THE PART THAT MAY BE WRITTEN DOWN. It identifies the key in logs, in audit rows and on screen,
// and it is what a lookup is done by, so verifying a key is one indexed read rather than a scan that hashes
// every row. The secret beside it is never stored, only its hash: a key that cannot be recovered can only be
// replaced, which is the behaviour we want from anything anyone might be tempted to email.
//
// THREE SEPARATE REASONS A KEY IS DEAD - revoked, expired, or never usable in the first place - and IsUsable is
// the only place that knows all three. A caller that checked two of them would be wrong in a way no test of
// the other two would catch, which is exactly how a revoked credential goes on working.
//
// EXPIRY IS A YEAR BY DEFAULT AND IS A DELIBERATE COMPROMISE. A key that never expires is a key nobody ever
// rotates; a key that expires is a dashboard that breaks at 3am on a date nobody remembered. A year, with the
// date shown wherever the key is listed, makes the choice visible rather than absent. The caller passes the
// date in, because the domain should not be reading a clock.
//
// ALLOWEDIPRANGES EXISTS AND IS EMPTY. The ministry's own requirements ask for an IP allow-list for the server
// that runs the nightly load, and nobody yet knows that server's address. An empty list means no restriction,
// so the column is carried and unenforced rather than the requirement being forgotten - and the day the address
// is known, this is a data change rather than a migration.
//
// LASTUSEDAT IS RECORDED BECAUSE THE FIRST QUESTION ABOUT ANY CREDENTIAL IS WHETHER IT IS STILL IN USE. Without
// it, a key nobody can account for cannot be revoked safely, and so it is never revoked.

namespace MotsSupplierPortal.Domain.Integration;

public sealed class ApiKey
{
    public const int PrefixLength = 8;
    public const int DefaultLifetimeDays = 365;

    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Prefix { get; init; }
    public required string SecretHash { get; init; }
    public required string Salt { get; init; }
    public required string[] Permissions { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public string[] AllowedIpRanges { get; init; } = [];

    public bool IsUsable(DateTimeOffset now) =>
        RevokedAt is null && ExpiresAt > now && Permissions.Length > 0;

    public void Revoke(Guid byUserId, DateTimeOffset now)
    {
        if (RevokedAt is not null) return;

        RevokedAt = now;
        RevokedByUserId = byUserId;
    }

    public void RecordUse(DateTimeOffset now) => LastUsedAt = now;
}
