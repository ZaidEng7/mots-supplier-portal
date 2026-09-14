// The number of versioned roots, and the two comments that state it, agree with the code.
//
//
// WHY THIS IS A TEST
//
// The versioned-aggregate interface's own comment said nine roots implement it and named them, and the mapping
// helper said all nine versioned roots. There were thirteen.
//
// The four it left out are the configuration roots, added later and correctly given a version, while the prose
// stayed where it was.
//
// A count that is NEARLY right is the harmful kind. A reader auditing "the nine" finds them consistent,
// concludes the mechanism is sound, and never looks at the other four. That same comment is the first thing this
// repository's own onboarding tells a newcomer to read.
//
// So the number is derived here rather than trusted, and both sentences have to match it. If a fourteenth root
// is added, this fails and names it.
//
// The other direction is checked too, because a comment can also be wrong by naming something that no longer
// exists, which reads as authoritative and sends a reader looking for a class that is not there.
//
// A reflection walk that found no roots would make every check pass, so the non-empty assertion comes first.
//
// It walks up from the test binaries to the repository, so this works from the command line, from an editor, and
// in continuous integration without any of them agreeing on a working directory.

namespace MotsSupplierPortal.Tests.Architecture;

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using MotsSupplierPortal.Domain.Common;

public sealed class VersionedRootCountTests
{
    private static Type[] VersionedRoots() =>
        [.. typeof(IVersionedAggregate).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IVersionedAggregate).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

    [Fact]
    public void The_domain_declares_the_roots_the_comments_claim()
    {
        var roots = VersionedRoots();

        roots.Should().HaveCountGreaterThan(5, "the walk must be finding real aggregate roots");

        var stated = DocCommentCount("Domain", "Common", "IVersionedAggregate.cs");
        stated.Should().Be(
            roots.Length,
            "IVersionedAggregate's own comment names the count, and a reader auditing the named ones "
            + $"would never look at the rest. Roots found: {string.Join(", ", roots.Select(r => r.Name))}");
    }

    [Fact]
    public void The_persistence_comment_states_the_same_number()
    {
        DocCommentCount("Infrastructure", "Persistence", "AppManagedVersion.cs")
            .Should().Be(VersionedRoots().Length,
                "the two comments are read by different people and must not disagree with each other either");
    }

    [Fact]
    public void Every_named_root_in_the_comment_is_a_real_type()
    {
        var source = File.ReadAllText(SolutionFile("Domain", "Common", "IVersionedAggregate.cs"));
        var listed = Regex.Match(source, @"records implement this:(?<names>[^.]+?)\.", RegexOptions.Singleline);
        listed.Success.Should().BeTrue("the comment must still carry the list this check reads");

        var names = listed.Groups["names"].Value
            .Replace("//", " ", StringComparison.Ordinal)
            .Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(part => part.Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(name => name.Length > 0)
            .ToArray();

        var actual = VersionedRoots().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        names.Where(name => !actual.Contains(name)).Should().BeEmpty(
            "the comment names a type that does not implement IVersionedAggregate");
        names.Should().HaveCount(actual.Count, "and it must name all of them, not some");
    }

    private static int DocCommentCount(params string[] path)
    {
        var source = File.ReadAllText(SolutionFile(path));
        var words = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11,
            ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15,
        };

        var match = Regex.Match(source, @"\b(eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen)\b", RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue($"{path[^1]} must still state the count in words for this check to read");

        return words[match.Groups[1].Value];
    }

    private static string SolutionFile(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MotsSupplierPortal.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the check cannot find the backend solution root from the test binaries");

        var file = Path.Combine([directory!.FullName, .. path]);
        File.Exists(file).Should().BeTrue($"expected {file}");
        return file;
    }
}
