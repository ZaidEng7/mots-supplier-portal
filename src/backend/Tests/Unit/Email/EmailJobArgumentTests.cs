using System.Reflection;
using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Email;

namespace MotsSupplierPortal.Tests.Unit.Email;

/// <summary>
/// MSP-89: nothing an email job receives may be an email address or a token.
///
/// <para><b>Why this is a reflection test rather than a set of behavioural ones.</b> Hangfire
/// persists job arguments as plaintext JSON, and the exposure is a property of the method
/// signatures, not of any single send. A behavioural test proves one path is clean; this proves
/// there is no unclean path to find. It also fails on a method added later, which is the whole
/// point - MSP-87's finding was not that one job leaked, it was that ten of them did and nobody had
/// looked.</para>
///
/// <para>This is the MSP-88 principle applied while we have the choice: a comment saying "pass ids,
/// not addresses" is a note for someone already looking. This is a control.</para>
/// </summary>
public sealed class EmailJobArgumentTests
{
    private static IEnumerable<MethodInfo> JobMethods =>
        typeof(EmailJobs).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    /// <summary>
    /// The single documented exception, kept as an explicit allow-list rather than a looser rule.
    ///
    /// A rejection reason is not persisted on the Supplier aggregate, so unlike every other value
    /// here it cannot be resolved from an id. Listing it by name means adding a second string
    /// argument anywhere fails this test and forces the same conversation again - which is what a
    /// narrow exception is for. See SendApplicationRejectedEmailAsync for the full reasoning.
    /// </summary>
    private static readonly (string Method, string Parameter)[] AllowedStringArguments =
    [
        (nameof(EmailJobs.SendApplicationRejectedEmailAsync), "reason"),
    ];

    [Fact]
    public void Every_email_job_takes_only_identifiers()
    {
        var offenders = new List<string>();

        foreach (var method in JobMethods)
        {
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(Guid)) continue;
                if (parameter.ParameterType == typeof(CancellationToken)) continue;

                if (AllowedStringArguments.Contains((method.Name, parameter.Name ?? "")))
                {
                    continue;
                }

                offenders.Add($"{method.Name}({parameter.ParameterType.Name} {parameter.Name})");
            }
        }

        offenders.Should().BeEmpty(
            "a job argument is stored in Hangfire's tables in plaintext for the whole retention " +
            "window. MSP-87 read 15 suppliers' addresses and a working password-reset token out of " +
            "that store. Resolve the value inside the job from an id instead");
    }

    [Fact]
    public void No_job_method_hints_at_carrying_a_url_or_an_address()
    {
        // Names, not just types. A `string acceptUrl` would already fail the test above, but a
        // `Guid verifyUrlId` would not - and would mean somebody had found a way to keep passing the
        // thing around. Cheap to assert, and it fails loudly on the naming rather than quietly on
        // the intent.
        var suspicious = JobMethods
            .SelectMany(m => m.GetParameters().Select(p => (Method: m.Name, Parameter: p.Name ?? "")))
            .Where(p => p.Parameter.Contains("url", StringComparison.OrdinalIgnoreCase)
                || p.Parameter.Contains("email", StringComparison.OrdinalIgnoreCase)
                || p.Parameter.Contains("token", StringComparison.OrdinalIgnoreCase))
            .ToList();

        suspicious.Should().BeEmpty(
            "tokens are minted inside the job and addresses are resolved there; a parameter named " +
            "for one of them means that stopped being true");
    }

    [Fact]
    public void The_allow_list_still_describes_something_real()
    {
        // A guard on the guard. If SendApplicationRejectedEmailAsync ever loses its reason parameter
        // - because the reason gets persisted and resolved like everything else - the exception
        // should go with it rather than sitting here permitting a string nobody needs any more.
        foreach (var (methodName, parameterName) in AllowedStringArguments)
        {
            var method = JobMethods.SingleOrDefault(m => m.Name == methodName);

            method.Should().NotBeNull($"the allow-list names {methodName}, which no longer exists");
            method!.GetParameters().Select(p => p.Name).Should().Contain(parameterName,
                "an allow-list entry that permits nothing is an exception nobody will think to remove");
        }
    }
}
