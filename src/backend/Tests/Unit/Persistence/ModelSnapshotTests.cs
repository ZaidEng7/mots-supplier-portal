// The model the code builds matches the committed migration snapshot.
//
//
// WHY THIS EXISTS
//
// Moving fifty-six entity configurations out of one method and into a class each had to produce an identical
// model, and "it compiles" proves nothing about that: a configuration silently not discovered would compile
// perfectly and quietly drop an index, a check constraint or a seeded row.
//
// The usual way to check is to generate a migration and confirm it is empty. That tool cannot run on every
// machine, because it needs a runtime the developer may not have, so this does the same comparison in process,
// using the same differ the tool uses.
//
//
// IT KEEPS EARNING ITS PLACE AFTER THAT CHANGE
//
// Edit a configuration without adding a migration and this fails, naming the difference. Before, the only thing
// that noticed was a deployment against a database whose shape no longer matched.
//
// The snapshot type is found by reflection rather than named, because it is internal to the infrastructure
// assembly and this solution deliberately exposes no internals to tests. That also survives the snapshot being
// renamed, which the tooling does when the context is renamed.
//
// And a control, because a differ comparing two empty models would report no differences and pass the assertion
// above while checking nothing at all.

namespace MotsSupplierPortal.Tests.Unit.Persistence;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ModelSnapshotTests
{
    [Fact]
    public void The_built_model_matches_the_committed_snapshot()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options);

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
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options);

        context.Model.GetEntityTypes().Should().HaveCountGreaterThan(50,
            "this application maps dozens of entities, so a model with almost none means the "
            + "configurations were not applied");
    }
}
