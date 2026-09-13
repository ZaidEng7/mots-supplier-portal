// Nothing an email job receives may be an email address or a token.
//
//
// WHY THIS IS A REFLECTION TEST RATHER THAN A SET OF BEHAVIOURAL ONES
//
// The job framework persists arguments as plain text, and the exposure is a property of the method SIGNATURES
// rather than of any single send.
//
// A behavioural test proves one path is clean. This proves there is no unclean path to find. It also fails on a
// method added later, which is the whole point: the finding behind it was not that one job leaked, it was that
// ten of them did and nobody had looked.
//
// This is the principle applied while there is still a choice: a comment saying "pass identifiers, not
// addresses" is a note for somebody already looking, and this is a control.
//
//
// THERE IS A STRONGER CONTROL THAN THIS ONE, AND IT IS WORTH KNOWING IT IS HERE
//
// Reintroducing an address argument does not merely fail this test. It stops the integration project COMPILING,
// because the behavioural email tests call these methods directly.
//
// A compiler that refuses beats a test that fails, because a test can be deleted by somebody who thinks it is
// noise and a compile error cannot be. Same technique as removing a parameter from an interface outright: make
// the wrong thing impossible to express.
//
// The honest boundary: that mechanism proves SHAPE, never behaviour. It cannot tell you the job mints the right
// token for the right user. The behavioural tests exist for that, and a coverage floor caught the gap precisely
// because only the shape half had been built.
//
//
// THE SINGLE DOCUMENTED EXCEPTION
//
// A rejection reason is not persisted on the supplier record, so unlike every other value here it cannot be
// resolved from an identifier.
//
// It is kept as an explicit allow-list by name rather than a looser rule, so adding a second text argument
// anywhere fails this test and forces the same conversation again, which is what a narrow exception is for.
//
// Two further assertions guard that guard. Names are checked as well as types, because a text argument would
// already fail on its type while an identifier named after a URL would not, and would mean somebody had found a
// way to keep passing the thing around. And if the exception's own parameter ever disappears, because the
// reason gets persisted and resolved like everything else, the allow-list entry has to go with it rather than
// sitting here permitting a string nobody needs any more.

namespace MotsSupplierPortal.Tests.Unit.Email;

using System.Reflection;
using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Email;

public sealed class EmailJobArgumentTests
{
    private static IEnumerable<MethodInfo> JobMethods =>
        typeof(EmailJobs).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

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
        foreach (var (methodName, parameterName) in AllowedStringArguments)
        {
            var method = JobMethods.SingleOrDefault(m => m.Name == methodName);

            method.Should().NotBeNull($"the allow-list names {methodName}, which no longer exists");
            method!.GetParameters().Select(p => p.Name).Should().Contain(parameterName,
                "an allow-list entry that permits nothing is an exception nobody will think to remove");
        }
    }
}
