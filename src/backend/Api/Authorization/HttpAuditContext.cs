// Supplies the two facts every audit row needs about where it came from: a correlation identifier and
// the caller's network address.
//
// It is registered per request, and that is what makes the correlation identifier mean anything: every
// audit row written while handling one request resolves the same instance and therefore the same
// identifier. Registering it per use would silently restore the old behaviour, a fresh identifier per
// call, while looking correct.
//
//
// THE CORRELATION IDENTIFIER
//
// It is the distributed trace identifier, reinterpreted as a standard identifier, so an audit row joins
// directly to its trace. A trace identifier is sixteen bytes and so is the identifier type, so this is
// a reinterpretation rather than a hash: no collisions introduced, and the value can be turned back
// into the trace identifier. That is the difference between a column that correlates and one that
// merely looks unique.
//
// A caller's own identifier wins when they send one, which is the whole point of echoing it back: their
// log line and these audit rows have to carry the same value. The trace identifier stays the fallback,
// so nothing changes for the callers who send nothing, which today is all of them.
//
// When there is no trace at all, in a background job or a test that bypasses the request pipeline, one
// identifier is generated and cached for the scope rather than made fresh per call. Rows written by one
// job run still share an identifier. That is degraded, since it joins to no trace, but still internally
// correlated rather than falling back to the exact defect this class removes.
//
// OverrideCorrelationId sets the caller's identifier once and never overwrites it. The middleware calls
// it at most once, but the guard lives here rather than there: an identifier that could change
// mid-request would let two audit rows from one unit of work disagree, which is the property this class
// exists to hold.
//
//
// THE INTEGRATION LABEL
//
// It is the API key's prefix when the caller authenticated with a key, and null for a person. The prefix is the
// part of a key that is safe to write down - it identifies the credential and cannot be used as one - which is
// why it is what lands in the audit row rather than the key's name or its identifier.
//
//
// THE NETWORK ADDRESS, AND WHY IT IS TRUNCATED
//
// Addresses are truncated before they are stored, to the network rather than the host. This is a
// privacy judgement rather than a technical one, so it is recorded here.
//
// Storing the full address was the alternative, and forensically it is the more useful one. It was
// rejected because of retention. The audit log is kept indefinitely in this version, and whether
// retention or erasure obligations apply at all is still an open question. A full address is personal
// data, and keeping it forever under an unresolved erasure question is the most exposed form this data
// can take. It is also the one choice here that would be hard to walk back, whereas truncating is a
// decision that can be made now without waiting for the answer.
//
// The audit trail loses little. Its purpose is procurement transparency and defending a dispute, which
// is a question about which person did what, and the person is already identified exactly by their user
// identifier. The address adds network provenance rather than identity, and a truncated one still
// answers "did this come from somewhere unusual for this account", which is what the trail is actually
// asked at review time. It no longer singles out a household or a device forever.
//
// This is not the same question as the rules forbidding personal data in web addresses, logs and
// notification payloads. The audit table is a deliberate, append-only, access-controlled store and is
// not a log, so those rules do not prohibit storing an address here. The case for truncating is
// retention, not those rules.
//
// If the retention question resolves towards a defined period, this is worth revisiting: bounded
// retention would make the full address defensible again.
//
// Truncate handles one case that would otherwise be silently wrong. A dual-stack server reports an
// old-style caller as a new-style address wrapping it, and left as-is that would take the wrong branch
// and mask the wrong bytes, recording a value that is neither the right network nor obviously wrong.
//
// Old-style addresses are cut to the last quarter of their bits. New-style ones are cut to the smallest
// block routinely allocated to a single subscriber site, which is the equivalent cut rather than an
// arbitrary one.
//
// Truncate is public rather than internal only so a test can pin the behaviour without opening this
// project's internals to the test project, which this solution does not otherwise do. It is a pure
// function with no state and nothing sensitive in it.

namespace MotsSupplierPortal.Api.Authorization;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MotsSupplierPortal.Application.Common;

public sealed class HttpAuditContext(IHttpContextAccessor accessor) : IAuditContext
{
    private Guid? _fallback;
    private Guid? _supplied;

    public Guid CorrelationId
    {
        get
        {
            if (_supplied is { } supplied) return supplied;

            var traceId = Activity.Current?.TraceId;
            if (traceId is { } id && id != default)
            {
                Span<byte> bytes = stackalloc byte[16];
                id.CopyTo(bytes);
                return new Guid(bytes);
            }

            return _fallback ??= Guid.CreateVersion7();
        }
    }

    public void OverrideCorrelationId(Guid correlationId) => _supplied ??= correlationId;

    public string? IpAddress
    {
        get
        {
            var remote = accessor.HttpContext?.Connection.RemoteIpAddress;
            return remote is null ? null : Truncate(remote);
        }
    }

    public string? IntegrationLabel =>
        accessor.HttpContext?.User.FindFirst(ApiKeyAuthentication.KeyPrefixClaim)?.Value;

    public static string Truncate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            bytes[3] = 0;
            return $"{new IPAddress(bytes)}/24";
        }

        for (var i = 6; i < bytes.Length; i++)
        {
            bytes[i] = 0;
        }

        return $"{new IPAddress(bytes)}/48";
    }
}
