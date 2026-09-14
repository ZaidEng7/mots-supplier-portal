// The send-logging call site itself, not the email pipeline around it.
//
// The written rule is that personal or sensitive data is never placed in URLs, query strings, logs or
// notification payloads. An email address is personal data on its own.
//
// Before this fix, the logging sender logged the recipient's real address on every send: the one production
// log-writing call site in the email pipeline, and the exact thing that rule forbids.
//
// It captures the actual logger output rather than going through the sending interface, because the defect was
// never in what gets sent. It is in what gets written to the log stream, and only reading the real formatted line
// proves that.
//
// The capture is close enough to a real provider to prove what actually reaches the stream, without pulling in a
// logging framework's own test harness for a single call site.

namespace MotsSupplierPortal.Tests.Unit.Email;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Infrastructure.Email;

public sealed class LoggingEmailSenderTests
{
    private sealed class CapturingLogger : ILogger<LoggingEmailSender>
    {
        public List<string> Messages { get; } = [];
        public List<IReadOnlyList<KeyValuePair<string, object?>>> States { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
            {
                States.Add(pairs);
            }
        }
    }

    [Fact]
    public async Task A_send_logs_the_user_id_and_never_the_email_address()
    {
        var logger = new CapturingLogger();
        var sender = new LoggingEmailSender(logger);
        var userId = Guid.CreateVersion7();
        const string email = "prober-target@example.com";

        await sender.SendAsync(userId, email, "Verify your MOTS Supplier Portal account", "<p>body</p>");

        var line = logger.Messages.Should().ContainSingle().Subject;

        line.Should().Contain(userId.ToString(),
            "the log must still identify which send this line is about, just not by address");
        line.Should().NotContain(email,
            "BRULE-091 bars personal data from logs outright - an email address is PII on its own, " +
            "not only when it is paired with a token");

        var state = logger.States.Should().ContainSingle().Subject;
        var propertyValues = state.Select(kv => kv.Value as string).Where(v => v is not null);
        propertyValues.Should().NotContain(v => v!.Contains('@'),
            "no structured property may carry the address either - a dashboard or aggregator reading " +
            "properties instead of the formatted line must not recover it that way");
    }
}
