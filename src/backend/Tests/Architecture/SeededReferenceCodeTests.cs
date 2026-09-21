// The reference codes the development seeder writes have to exist in the reference tables it writes them
// against.
//
//
// WHAT WENT WRONG
//
// Every demo supplier carried the governorate code "DM". There is no such governorate. The seeded list is DIM,
// ALP, LAT and HOM, so 29 of the 38 suppliers in a freshly seeded database pointed at a region that does not
// exist.
//
// Nothing failed. RegionCode is a plain string on Address with no foreign key and no validation at the API
// either, so the write succeeded, the profile screen rendered the raw code, and the registry export - which
// joins the code to its name - printed "DM" as the name because the lookup missed and it falls back to the key
// rather than throwing. Three layers each degraded politely and the result was a database full of a value that
// means nothing.
//
// It surfaced only because somebody exported the registry and read the file.
//
//
// WHY THIS TEST IS SYNTACTIC
//
// The integration fixture deliberately does not run the seeder - it runs as development, and its suppliers once
// turned up in the middle of another suite's assertions - so there is no seeded database to assert against.
// Both facts are literals in source instead: the codes the seeder writes, and the codes the reference
// configuration seeds. Reading both files and comparing them is the only check available that does not need a
// database, and it is the same shape the row-scope guard uses.
//
// It is narrow on purpose. It covers the DEVELOPMENT SEEDER only, not the test fixtures, several of which also
// write "DM" - those rows never reach anybody's database and a developer reading a fixture is not misled by
// them. Widening it would mean editing a dozen unrelated tests for no gain.
//
//
// THE REAL HOLE IS LEFT OPEN, AND NAMED
//
// Nothing stops a supplier submitting any region code they like through the API: there is no foreign key on
// Address.RegionCode and no validator behind the address routes. This test does not close that. It closes the
// case that actually produced bad data, and records the wider gap here so the next person meets it as a known
// absence rather than a discovery.
//
//
// THE CONTROLS
//
// Non-vacuity first: a test that found no seeder codes at all would pass in silence, which is how an instrument
// that has stopped measuring anything looks from the outside. Both lists are asserted non-empty, and the
// reference list is asserted to hold the four governorates it is supposed to, so a parse that silently matched
// nothing cannot pass.

namespace MotsSupplierPortal.Tests.Architecture;

using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public sealed class SeededReferenceCodeTests
{
    [Fact]
    public void Every_region_code_the_seeder_writes_is_a_region_the_reference_data_defines()
    {
        var known = SeededRegionCodes();
        var used = SeederRegionCodes();

        known.Should().NotBeEmpty("the reference configuration is the source of the governorate list");
        known.Should().Contain(["DIM", "ALP", "LAT", "HOM"],
            "a parse that matched nothing would otherwise pass this test in silence");
        used.Should().NotBeEmpty("the seeder does write addresses, and a walk that found none proves nothing");

        used.Should().BeSubsetOf(
            known,
            "a region code with no row behind it writes a value that means nothing: there is no foreign key on "
            + "Address.RegionCode, no validation at the API, and the registry export falls back to printing the "
            + "code when the lookup misses - so bad data travels all the way to the file without one error");
    }

    private static IReadOnlyCollection<string> SeededRegionCodes()
    {
        var path = Path.Combine(
            RepositoryRoot(), "src", "backend", "Infrastructure", "Persistence", "Configurations",
            "RegionConfiguration.cs");

        return Regex.Matches(File.ReadAllText(path), @"Code\s*=\s*""([A-Z]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }

    private static IReadOnlyCollection<string> SeederRegionCodes()
    {
        var path = Path.Combine(
            RepositoryRoot(), "src", "backend", "Infrastructure", "Identity", "DevDataSeeder.cs");

        // AddAddress(kind, line1, line2, city, regionCode, ...) - the fifth argument.
        return Regex.Matches(
                File.ReadAllText(path),
                @"AddAddress\(\s*[^,]+,\s*(?:""[^""]*""|null)\s*,\s*(?:""[^""]*""|null)\s*,\s*(?:""[^""]*""|null)\s*,\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repository root is the directory holding docker-compose.yml");
        return dir!.FullName;
    }
}
