using FluentAssertions;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Infrastructure.Email;

namespace MotsSupplierPortal.Tests.Unit.Email;

/// <summary>
/// MSP-93/BRULE-091: the send-logging call site itself, not the email pipeline around it.
///
/// <para>BRULE-091 (docs/product/BUSINESS-RULES.md): "Personal/sensitive data is never placed in
/// URLs, query strings, logs, or notification payloads." An email address is personal data on its
/// own. Before this fix, <see cref="LoggingEmailSender"/> logged the recipient's real address on
/// every send - the one production log-writing call site in the email pipeline, and the exact
/// thing this rule forbids.</para>
///
/// <para>Captures the actual <see cref="ILogger"/> output rather than going through
/// <c>IEmailSender</c> - the defect was never in what gets sent, it is in what gets written to the
/// log stream, and only reading the real formatted log line proves that.</para>
/// </summary>
public sealed class LoggingEmailSenderTests
{
    /// <summary>Captures every formatted log line and its structured state, close enough to a real
    /// provider to prove what actually reaches the log stream, without pulling in a logging
    /// framework's own test harness for a single call site.</summary>
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
