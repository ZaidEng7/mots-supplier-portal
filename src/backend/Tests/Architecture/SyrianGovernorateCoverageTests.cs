// All fourteen Syrian governorates are seeded, and none of them is spelled in a way the reference list cannot
// read back.
//
// WHY A TEST AND NOT JUST A SEED. The list held four for most of this product's life, and that was a decision
// rather than an oversight - the four with the ministry's own directorates, the rest to be added on the
// reference-data screen. It became a defect the moment supplier records started arriving from the ministry's
// ERP, whose address carries a free-text province: a supplier in Tartus had no code to be filed under, and
// nothing in the system would have said so. There is no foreign key on Address.RegionCode and no validation
// behind the address routes, so a missing governorate does not fail - it writes a value with no row behind it,
// which is how 29 demo suppliers once came to live in a governorate called "DM".
//
// So the completeness of this list is load-bearing, and a list whose shortness cost real data once should not be
// able to get short again in silence.
//
// WHY IT READS SOURCE RATHER THAN A DATABASE. The same reason SeededReferenceCodeTests does: the integration
// fixture deliberately does not seed, so there is no seeded database to ask. The configuration file is the
// source of truth for what ships, and reading it is the only check available without a database.
//
// THE CODE SHAPE IS ASSERTED TOO, and that is not pedantry. SeededReferenceCodeTests finds codes with the
// pattern [A-Z]+, so a governorate added as "SY-TA" or "Tartus" would be invisible to the very test that
// catches seeder codes pointing at nothing. A code this file cannot match is a code that silently leaves that
// guard.
//
// THE CONTROL is the count together with the names: a regex that matched nothing, or a file that moved, fails
// on the count before it can pass vacuously on a subset.

namespace MotsSupplierPortal.Tests.Architecture;

using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public sealed class SyrianGovernorateCoverageTests
{
    private static readonly string[] Governorates =
    [
        "Damascus", "Rif Dimashq", "Aleppo", "Homs", "Hama", "Latakia", "Idlib", "Al-Hasakah", "Deir ez-Zor",
        "Tartus", "Raqqa", "Daraa", "As-Suwayda", "Quneitra",
    ];

    [Fact]
    public void Every_syrian_governorate_is_seeded_with_a_code_the_reference_guard_can_read()
    {
        var source = File.ReadAllText(RegionConfigurationPath());

        var seededNames = Regex.Matches(source, @"NameEn\s*=\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        seededNames.Should().HaveCount(
            Governorates.Length,
            "Syria has fourteen governorates and a supplier in any of them must have somewhere to be filed; a "
            + "parse that matched nothing would otherwise pass this test on an empty subset");

        seededNames.Should().BeEquivalentTo(
            Governorates,
            "a governorate missing from the seed is not a refused write - Address.RegionCode has no foreign key "
            + "and no validator, so it becomes a stored code with no row behind it");

        var codes = Regex.Matches(source, @"Code\s*=\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        codes.Should().HaveCount(Governorates.Length);
        codes.Should().OnlyContain(
            code => Regex.IsMatch(code, "^[A-Z]+$"),
            "SeededReferenceCodeTests finds codes with the pattern [A-Z]+, so a code outside that shape leaves "
            + "the guard that catches seeder codes pointing at a governorate that does not exist");
    }

    [Fact]
    public void The_erp_address_mapper_knows_every_seeded_governorate()
    {
        var seeded = Regex.Matches(File.ReadAllText(RegionConfigurationPath()), @"Code\s*=\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        MotsSupplierPortal.Application.Integration.ErpAddressMapper.Governorates
            .Select(g => g.Code)
            .Should().BeEquivalentTo(
                seeded,
                "an imported address is filed under one of these codes, and a code the seed does not have is the "
                + "same stored-code-with-no-row-behind-it this file exists to prevent");
    }

    private static string RegionConfigurationPath() =>
        Path.Combine(
            RepositoryRoot(), "src", "backend", "Infrastructure", "Persistence", "Configurations",
            "RegionConfiguration.cs");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test assembly.");
    }
}
