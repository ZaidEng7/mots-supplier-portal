using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Observability;
using Serilog;
using Serilog.Events;

namespace MotsSupplierPortal.Tests.Unit.Observability;

/// <summary>
/// MSP-61: proves the redaction stage actually fires. The deny-list was previously referenced by a
/// comment in AuditChangeBuilder but no pipeline existed, so "PII is redacted in logs" was an
/// assertion nobody had ever executed.
/// </summary>
public sealed class RedactingEnricherTests
{
    private static LogEvent CaptureWithProperty(string propertyName, string value)
    {
        LogEvent? captured = null;

        using var logger = new LoggerConfiguration()
            .Enrich.With(new RedactingEnricher())
            .WriteTo.Sink(new DelegatingSink(e => captured = e))
            .CreateLogger();

        logger.Information("probe {" + propertyName + "}", value);

        captured.Should().NotBeNull();
        return captured!;
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("ResetToken")]
    [InlineData("token")]
    [InlineData("Authorization")]
    [InlineData("ClientSecret")]
    [InlineData("Iban")]
    [InlineData("OtpCode")]
    // Casing and embedding both matter: the deny-list is a case-insensitive substring match, so a
    // property named e.g. "RefreshTokenHash" must be caught as surely as a bare "token".
    [InlineData("RefreshTokenHash")]
    public void Deny_listed_property_names_are_redacted(string propertyName)
    {
        var captured = CaptureWithProperty(propertyName, "super-secret-value");

        captured.Properties[propertyName].ToString()
            .Should().Contain(RedactingEnricher.RedactedPlaceholder)
            .And.NotContain("super-secret-value");
    }

    [Theory]
    [InlineData("ToEmail")]
    [InlineData("ReferenceCode")]
    [InlineData("SupplierId")]
    public void Ordinary_property_names_pass_through_untouched(string propertyName)
    {
        var captured = CaptureWithProperty(propertyName, "ordinary-value");

        captured.Properties[propertyName].ToString().Should().Contain("ordinary-value");
    }

    [Fact]
    public void Redaction_does_not_drop_other_properties_on_the_same_event()
    {
        LogEvent? captured = null;
        using var logger = new LoggerConfiguration()
            .Enrich.With(new RedactingEnricher())
            .WriteTo.Sink(new DelegatingSink(e => captured = e))
            .CreateLogger();

        logger.Information("probe {Token} {ReferenceCode}", "secret", "SUP-2026-000001");

        captured!.Properties["Token"].ToString().Should().Contain(RedactingEnricher.RedactedPlaceholder);
        captured.Properties["ReferenceCode"].ToString().Should().Contain("SUP-2026-000001");
    }

    private sealed class DelegatingSink(Action<LogEvent> onEmit) : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) => onEmit(logEvent);
    }
}
