using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Unit;

/// <summary>
/// The model the code builds matches the committed migration snapshot.
///
/// <para><b>Why this exists.</b> Moving 56 entity configurations out of <c>OnModelCreating</c> and into
/// one class each had to produce a byte-identical model, and "it compiles" proves nothing about that: a
/// configuration silently not discovered would compile perfectly and quietly drop an index, a check
/// constraint or a seed row.</para>
///
/// <para>The usual way to check is <c>dotnet ef migrations add</c> and confirming the result is empty.
/// That tool cannot run on every machine - it needs a runtime the developer may not have - so this does
/// the same comparison in-process, using the same differ the tool uses.</para>
///
/// <para><b>It keeps earning its place after that change.</b> Edit a configuration without adding a
/// migration and this fails, naming the difference. Before, the only thing that noticed was a deployment
/// against a database whose shape no longer matched.</para>
/// </summary>
public sealed class ModelSnapshotTests
{
    [Fact]
    public void The_built_model_matches_the_committed_snapshot()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options);

        // Found by reflection rather than named: the snapshot type is internal to Infrastructure, and
        // this solution deliberately uses no InternalsVisibleTo. It also survives the snapshot being
        // renamed, which the tooling does when the context is renamed.
        var snapshotType = typeof(AppDbContext).Assembly.GetTypes()
            .Single(t => typeof(ModelSnapshot).IsAssignableFrom(t) && !t.IsAbstract);
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType)!;

        var snapshotModel = context.GetService<IModelRuntimeInitializer>()
            .Initialize(((IMutableModel)snapshot.Model).FinalizeModel(), designTime: true);

        var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshotModel.GetRelationalModel(),
            context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        differences.Should().BeEmpty(
            "the configurations build the same model the snapshot records - a difference here means either "
            + "a configuration was not discovered, or a real model change landed without a migration");
    }

    [Fact]
    public void Every_entity_the_context_exposes_is_in_the_model()
    {
        // The control. A differ comparing two empty models would report no differences and pass the test
        // above while checking nothing at all.
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options);

        context.Model.GetEntityTypes().Should().HaveCountGreaterThan(50,
            "this application maps dozens of entities, so a model with almost none means the "
            + "configurations were not applied");
    }
}
