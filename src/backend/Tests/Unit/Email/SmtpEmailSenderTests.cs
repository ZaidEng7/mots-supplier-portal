using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Infrastructure.Email;

namespace MotsSupplierPortal.Tests.Unit.Email;

/// <summary>
/// Task #35. Same MSP-93/BRULE-091 property LoggingEmailSenderTests proves for the stub, extended
/// to SmtpEmailSender's failure path - the one LoggingEmailSender never had. A real SMTP rejection
/// commonly echoes the recipient address back in its own error text, so the naive `logger.LogError(ex, ...)`
/// this class deliberately avoids would have reintroduced exactly what LoggingEmailSenderTests
/// guards against, just through the exception path instead of the success path.
/// </summary>
public sealed class SmtpEmailSenderTests
{
    private sealed class CapturingLogger : ILogger<SmtpEmailSender>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task A_failed_send_throws_and_never_logs_the_email_address()
    {
        var logger = new CapturingLogger();
        // Port 1 on loopback: nothing listens there, so ConnectAsync fails fast and deterministically
        // without needing a real (or fake) SMTP server up for this test.
        var options = Options.Create(new SmtpOptions { Host = "127.0.0.1", Port = 1, FromAddress = "no-reply@example.com" });
        var sender = new SmtpEmailSender(options, logger);
        var userId = Guid.CreateVersion7();
        const string email = "prober-target@example.com";

        var act = async () => await sender.SendAsync(userId, email, "Verify your MOTS Supplier Portal account", "<p>body with a token</p>");

        var thrown = await act.Should().ThrowAsync<EmailDeliveryException>();
        thrown.Which.Message.Should().NotContain(email,
            "the wrapped exception must not carry the recipient address, even though the underlying " +
            "SMTP/connect exception's own message might");
        thrown.Which.InnerException.Should().BeNull(
            "wrapping the raw exception as InnerException would let its Message - which can echo the " +
            "recipient address - surface anywhere this exception gets formatted");

        logger.Messages.Should().ContainSingle();
        logger.Messages[0].Should().NotContain(email,
            "BRULE-091: an email address is PII on its own and must never reach the log stream, " +
            "including on the failure path");
        logger.Messages[0].Should().Contain(userId.ToString());
    }
}
