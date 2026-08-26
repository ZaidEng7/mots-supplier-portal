using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Storage;

/// <summary>
/// Talks to a clamd daemon over its raw INSTREAM protocol (docs/security/SECURITY-ARCHITECTURE.md
/// §4.1: "background scan job... integrates a scanner (e.g., ClamAV / cloud AV)
/// [ASSUMPTION on scanner]"). ClamAV was chosen as the scanner because it's the docs' own named
/// example, open-source, and self-hostable via docker-compose - no cloud AV account/API-key
/// dependency needed for local dev or this environment.
///
/// Fail-closed: any scanner error (connection refused, timeout, malformed reply) is treated as
/// Infected rather than Clean - the spec's ScanState only names {Pending, Clean, Rejected}, and
/// silently letting an unscanned file through on a transport error would defeat the
/// quarantine-first invariant.
/// </summary>
public sealed class ClamAvScanner(IOptions<ClamAvOptions> options) : IVirusScanner
{
    private const int ChunkSize = 8192;
    private readonly ClamAvOptions _options = options.Value;

    public async Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, ct);
            await using var stream = client.GetStream();

            var command = Encoding.ASCII.GetBytes("zINSTREAM\0");
            await stream.WriteAsync(command, ct);

            var buffer = new byte[ChunkSize];
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, ChunkSize), ct)) > 0)
            {
                var lengthPrefix = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(read);
                await stream.WriteAsync(BitConverter.GetBytes(lengthPrefix), ct);
                await stream.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            // Zero-length chunk terminates the stream.
            await stream.WriteAsync(BitConverter.GetBytes(0), ct);

            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var response = await reader.ReadLineAsync(ct) ?? string.Empty;

            if (response.Contains("FOUND", StringComparison.Ordinal))
            {
                return ScanOutcome.Infected;
            }
            if (response.Contains("OK", StringComparison.Ordinal))
            {
                return ScanOutcome.Clean;
            }
            // ERROR or unrecognized reply - fail closed.
            return ScanOutcome.Infected;
        }
        catch
        {
            return ScanOutcome.Infected;
        }
    }
}
