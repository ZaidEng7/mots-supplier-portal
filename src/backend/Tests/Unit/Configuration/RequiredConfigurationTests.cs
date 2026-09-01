using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MotsSupplierPortal.Api.Configuration;

namespace MotsSupplierPortal.Tests.Unit.Configuration;

/// <summary>
/// Guards the fail-fast rule. Before this existed, three settings fell back to localhost when
/// absent and degraded silently in production: the database connection, the public URL used to
/// build every verification/reset/invite link, and the CORS origin list. None threw, logged, or
/// failed a health check.
/// </summary>
public sealed class RequiredConfigurationTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    private static (string Key, string? Value)[] Complete() =>
    [
        ("ConnectionStrings:Default", "Host=db;Database=x;Username=u;Password=p"),
        ("App:PublicUrl", "https://suppliers.example.gov"),
        ("Jwt:Issuer", "https://suppliers.example.gov"),
        ("Jwt:Audience", "mots-supplier-portal"),
        ("Cors:AllowedOrigins:0", "https://suppliers.example.gov"),
        ("Smtp:Host", "smtp.example.gov"),
        ("Smtp:FromAddress", "no-reply@suppliers.example.gov"),
    ];

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Missing_App_PublicUrl_prevents_startup_outside_Development(string environmentName)
    {
        var settings = Complete().Where(e => e.Key != "App:PublicUrl").ToArray();

        var act = () => RequiredConfiguration.Validate(Config(settings), new FakeEnvironment(environmentName));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*App:PublicUrl*",
                "a missing public URL silently shipped localhost links in every password-reset email");
    }

    [Fact]
    public void Missing_connection_string_prevents_startup_outside_Development()
    {
        var settings = Complete().Where(e => e.Key != "ConnectionStrings:Default").ToArray();

        var act = () => RequiredConfiguration.Validate(Config(settings), new FakeEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings:Default*");
    }

    [Fact]
    public void Missing_cors_origins_prevents_startup_outside_Development()
    {
        var settings = Complete().Where(e => !e.Key.StartsWith("Cors:")).ToArray();

        var act = () => RequiredConfiguration.Validate(Config(settings), new FakeEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cors:AllowedOrigins*");
    }

    [Fact]
    public void Missing_smtp_host_prevents_startup_outside_Development()
    {
        // Task #35: SmtpOptions.Host is `required`, but that only fails at the first real send
        // (IOptions<SmtpOptions>.Value binding) - listed here so it fails at boot instead.
        var settings = Complete().Where(e => e.Key != "Smtp:Host").ToArray();

        var act = () => RequiredConfiguration.Validate(Config(settings), new FakeEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:Host*");
    }

    [Fact]
    public void Missing_smtp_from_address_prevents_startup_outside_Development()
    {
        var settings = Complete().Where(e => e.Key != "Smtp:FromAddress").ToArray();

        var act = () => RequiredConfiguration.Validate(Config(settings), new FakeEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:FromAddress*");
    }

    [Fact]
    public void All_missing_keys_are_reported_together()
    {
        // Discovering these one redeploy at a time is its own small outage.
        var act = () => RequiredConfiguration.Validate(Config(), new FakeEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
                .Contain("ConnectionStrings:Default").And
                .Contain("App:PublicUrl").And
                // Jwt is listed here rather than left to its own downstream throw: with only that
                // throw, a deployment missing both learned about them one redeploy apart. A live
                // boot test caught exactly that after the first version of this class shipped.
                .Contain("Jwt:Issuer").And
                .Contain("Jwt:Audience").And
                .Contain("Cors:AllowedOrigins").And
                .Contain("Smtp:Host").And
                .Contain("Smtp:FromAddress");
    }

    [Fact]
    public void Complete_configuration_starts_normally()
    {
        var act = () => RequiredConfiguration.Validate(Config(Complete()), new FakeEnvironment("Production"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Development_is_exempt_so_dotnet_run_needs_no_configuration()
    {
        // appsettings.Development.json supplies these; requiring them would add friction locally
        // without protecting anything.
        var act = () => RequiredConfiguration.Validate(Config(), new FakeEnvironment("Development"));

        act.Should().NotThrow();
    }
}
