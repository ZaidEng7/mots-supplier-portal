using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Api.Authorization;

/// <summary>
/// Supplies audit provenance from the ambient request/trace context (MSP-64).
///
/// Registered SCOPED, which is what makes the correlation id mean something: every audit row
/// written while handling one request resolves the same instance and therefore the same id. A
/// transient registration would silently restore the previous behaviour - a fresh id per call -
/// while looking correct.
/// </summary>
public sealed class HttpAuditContext(IHttpContextAccessor accessor) : IAuditContext
{
    private Guid? _fallback;

    /// <summary>
    /// The distributed trace id, reinterpreted as a Guid, so an audit row joins directly to its
    /// trace (NFR-OBS-003, FR-AUD-005).
    ///
    /// A W3C trace id is 16 bytes and so is a Guid, so this is a reinterpretation rather than a
    /// hash: no collisions introduced, and the value is recoverable back to the trace id. That is
    /// the difference between a column that correlates and one that merely looks unique.
    /// </summary>
    public Guid CorrelationId
    {
        get
        {
            var traceId = Activity.Current?.TraceId;
            if (traceId is { } id && id != default)
            {
                Span<byte> bytes = stackalloc byte[16];
                id.CopyTo(bytes);
                return new Guid(bytes);
            }

            // No Activity: background jobs, and tests that bypass the request pipeline. Cached on
            // the scope rather than generated per call, so rows written by one job run still share
            // an id - degraded (not joinable to a trace) but still internally correlated, instead
            // of falling back to the exact defect this class removes.
            return _fallback ??= Guid.CreateVersion7();
        }
    }

    /// <summary>
    /// Caller IP, TRUNCATED: IPv4 to /24, IPv6 to /48. Null outside a request.
    ///
    /// The decision, recorded because it is a privacy judgement rather than a technical one:
    ///
    /// Storing the full address was the alternative, and it is the more useful one forensically.
    /// It was rejected because of retention. ASM-085 keeps the audit log INDEFINITELY in v1, and
    /// OQ-010 - whether retention or right-to-erasure obligations apply at all - is still open. A
    /// full IP is personal data; keeping it forever, under an unresolved erasure question, is the
    /// most exposed form this data can take, and it is the one choice here that is hard to walk
    /// back. Truncation is a decision we can make now without waiting on OQ-010.
    ///
    /// The audit trail loses little. Its purpose (FR-AUD-001, RISK-015) is procurement
    /// transparency and dispute defensibility - "which actor did what" - and the actor is already
    /// identified exactly by ActorUserId. The address adds network provenance, not identity: a /24
    /// still answers "did this come from somewhere unusual for this account", which is what the
    /// audit trail is actually asked at review time. It no longer singles out a household or device
    /// forever.
    ///
    /// Note this is NOT the same question as BRULE-091 / NFR-PRIV-004. Those forbid personal data
    /// in URLs, query strings, logs and notification payloads; the audit table is a deliberate,
    /// append-only, access-controlled store (FR-AUD-002) and is not a log. So the rules do not
    /// prohibit storing an address here - the case for truncating is retention, not those rules.
    ///
    /// If OQ-010 resolves toward a defined retention period, revisit: bounded retention would make
    /// the full address defensible again.
    /// </summary>
    public string? IpAddress
    {
        get
        {
            var remote = accessor.HttpContext?.Connection.RemoteIpAddress;
            return remote is null ? null : Truncate(remote);
        }
    }

    /// <summary>Public rather than internal only so the truncation can be pinned by test without
    /// introducing an InternalsVisibleTo, which this solution does not otherwise use. Pure
    /// function, no state, nothing sensitive.</summary>
    public static string Truncate(IPAddress address)
    {
        // IPv4-mapped IPv6 (::ffff:203.0.113.5) is how a dual-stack Kestrel reports IPv4 callers.
        // Left unmapped it would take the IPv6 branch and mask the wrong bytes, so a truncated
        // value would be recorded that is neither the right network nor obviously wrong.
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

        // /48 is the smallest block routinely allocated to a single subscriber site, so it is the
        // IPv6 analogue of /24 rather than an arbitrary cut.
        for (var i = 6; i < bytes.Length; i++)
        {
            bytes[i] = 0;
        }

        return $"{new IPAddress(bytes)}/48";
    }
}
