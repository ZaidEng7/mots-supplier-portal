// What the scanner client reports for each kind of answer the daemon can give, and for no answer at all.
//
// A FAKE DAEMON SPEAKS THE REAL PROTOCOL. It reads the zINSTREAM command and the length-prefixed chunks up to the empty
// one that ends the stream, then answers with whatever the test chose, NUL-terminated as the real daemon does. So the
// client is exercised end to end over a socket, not through a seam that could agree with it about something the
// daemon never says.
//
// EVERY "COULD NOT ANSWER" CASE MUST COME BACK UNAVAILABLE, NOT INFECTED. They used to come back infected, and both
// callers delete an infected file, so a scanner outage destroyed uploads. The cases are the ones an outage produces: a
// port nobody listens on, a reply the client does not recognise (the daemon's own ERROR lines), and a daemon that
// accepts the file and never answers, which is what it does while reloading its signatures.
//
// THE CONTROLS ARE OK AND FOUND, which must still mean clean and infected - a client that reported every answer as
// unavailable would pass the outage tests.
//
// THE NO-ANSWER TEST HOLDS THE CONNECTION OPEN until it is over, and requires the scan to give up well inside that. A
// daemon that eventually hangs up would end the read on its own, so a version without the time limit passed this test
// just more slowly - it guarded nothing.

namespace MotsSupplierPortal.Tests.Unit.Storage;

using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class ClamAvScannerTests
{
    private static ClamAvScanner ScannerOn(int port, TimeSpan? timeout = null) =>
        new(Options.Create(new ClamAvOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        }));

    private static MemoryStream File() => new(Encoding.ASCII.GetBytes("%PDF-1.4 a supplier's licence"));

    private static async Task<ScanOutcome> ScanAgainstDaemonAsync(string? reply, TimeSpan? timeout = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var testOver = new TaskCompletionSource();

        var daemon = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            var command = new byte["zINSTREAM\0".Length];
            await stream.ReadExactlyAsync(command);

            var prefix = new byte[4];
            while (true)
            {
                await stream.ReadExactlyAsync(prefix);
                var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(prefix);
                if (length == 0) break;
                await stream.ReadExactlyAsync(new byte[length]);
            }

            if (reply is null)
            {
                await testOver.Task;
                return;
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\0"));
        });

        try
        {
            return await ScannerOn(port, timeout).ScanAsync(File(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            testOver.TrySetResult();
            listener.Stop();
            await daemon.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task A_clean_reply_is_clean()
    {
        (await ScanAgainstDaemonAsync("stream: OK")).Should().Be(ScanOutcome.Clean);
    }

    [Fact]
    public async Task A_found_reply_is_infected()
    {
        (await ScanAgainstDaemonAsync("stream: Eicar-Test-Signature FOUND")).Should().Be(ScanOutcome.Infected);
    }

    [Fact]
    public async Task An_error_reply_is_unavailable_not_infected()
    {
        (await ScanAgainstDaemonAsync("INSTREAM size limit exceeded. ERROR")).Should().Be(
            ScanOutcome.Unavailable,
            "the daemon did not look at the file; calling it infected deleted uploads during an outage");
    }

    [Fact]
    public async Task A_scanner_nobody_is_listening_on_is_unavailable_not_infected()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        (await ScannerOn(port).ScanAsync(File(), CancellationToken.None)).Should().Be(ScanOutcome.Unavailable);
    }

    [Fact]
    public async Task A_daemon_that_never_answers_is_unavailable_once_the_time_limit_passes()
    {
        (await ScanAgainstDaemonAsync(reply: null, timeout: TimeSpan.FromMilliseconds(500))).Should().Be(
            ScanOutcome.Unavailable,
            "while the daemon reloads its signatures it accepts a file and says nothing; waiting forever holds a worker");
    }

    [Fact]
    public async Task A_scan_its_caller_cancelled_is_not_reported_as_an_outage()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var scan = () => ScannerOn(port).ScanAsync(File(), cancelled.Token);

        await scan.Should().ThrowAsync<OperationCanceledException>();
        listener.Stop();
    }
}
