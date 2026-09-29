// The virus scanner: talks to the scanning daemon over its own streaming protocol.
//
// This scanner was chosen because it is the written architecture's own named example, is open source, and is
// self-hostable alongside the rest of the local stack, so neither local development nor this environment needs a
// cloud account or an API key.
//
//
// FAIL-CLOSED, WITHOUT DESTROYING ANYTHING
//
// Only the daemon saying FOUND is infected, and only it saying OK is clean. Anything else - a refused connection, a
// timeout, a reply this client does not recognise - is unavailable: the file is not released, because nothing has
// said it is clean, and it is not deleted, because nothing has said it is infected.
//
// It used to report all of those as infected, which kept unscanned files out but also meant a scanner outage
// deleted every upload scanned while it lasted. The quarantine already does the keeping-out; the scanner's job is
// only to answer, and "I could not look" is an answer.
//
// A SCANNER THAT DOES NOT ANSWER IN TIME CANNOT ANSWER. A daemon that is wedged or overloaded, or a connection that
// is accepted and then never answered, would otherwise hold a scan job's worker indefinitely. After ClamAv:Timeout the
// scan gives up and reports unavailable, and the job tries again later. The limit is three minutes unless configured,
// deliberately above clamd's own two-minute MaxScanTime: a slow file then gets clamd's verdict - including clamd giving
// up on it - before the portal gives up on clamd. Equal limits would cut off exactly the answer that was coming.
//
// Cancellation is not an outage: a scan cancelled by its caller is rethrown, so a request that was abandoned is not
// recorded as a scanner that failed.
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
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_options.Timeout);
        var token = limit.Token;

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, token);
            await using var stream = client.GetStream();

            var command = Encoding.ASCII.GetBytes("zINSTREAM\0");
            await stream.WriteAsync(command, token);

            var buffer = new byte[ChunkSize];
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, ChunkSize), token)) > 0)
            {
                var lengthPrefix = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(read);
                await stream.WriteAsync(BitConverter.GetBytes(lengthPrefix), token);
                await stream.WriteAsync(buffer.AsMemory(0, read), token);
            }

            await stream.WriteAsync(BitConverter.GetBytes(0), token);

            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var response = await reader.ReadLineAsync(token) ?? string.Empty;

            if (response.Contains("FOUND", StringComparison.Ordinal))
            {
                return ScanOutcome.Infected;
            }
            if (response.Contains("OK", StringComparison.Ordinal))
            {
                return ScanOutcome.Clean;
            }
            return ScanOutcome.Unavailable;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ScanOutcome.Unavailable;
        }
    }
}
