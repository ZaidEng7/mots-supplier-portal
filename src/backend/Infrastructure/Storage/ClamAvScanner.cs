// The virus scanner: talks to the scanning daemon over its own streaming protocol.
//
// This scanner was chosen because it is the written architecture's own named example, is open source, and is
// self-hostable alongside the rest of the local stack, so neither local development nor this environment needs a
// cloud account or an API key.
//
//
// FAIL-CLOSED
//
// Any error at all, a refused connection, a timeout, a malformed reply, is treated as infected rather than clean.
//
// The written scan states name only pending, clean and rejected, and silently letting an unscanned file through on
// a transport error would defeat the quarantine-first rule.
//
// The content is streamed in small chunks rather than buffered, and a zero-length chunk is what terminates the
// stream in this protocol.

namespace MotsSupplierPortal.Infrastructure.Storage;

using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;

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
            return ScanOutcome.Infected;
        }
        catch
        {
            return ScanOutcome.Infected;
        }
    }
}
