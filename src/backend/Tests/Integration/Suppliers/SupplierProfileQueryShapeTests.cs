// Loading a supplier's profile in one statement is a cartesian product; it is split instead.
//
// The shared include loads six child collections. In a single statement the supplier row repeats once per
// combination across all six, so rows multiply rather than add.
//
// The mapper de-duplicates the entities afterwards, which is why the read model looked correct while the database
// did asymptotically more work.
//
//
// THE COMPARISON CANNOT ROT
//
// It measures both shapes in one test, reproducing the old behaviour explicitly rather than relying on somebody
// reverting the fix to see the difference.
//
// If the production extension stops splitting, the split figures become the single figures and the assertions fail.
//
// The row multiplication is measured with raw statements, because the whole point is that the mapper hides it from
// the materialised result. The command counter overrides the asynchronous path as well as the synchronous one,
// because queries route through the former and overriding only the latter counted nothing: the counter read zero
// and the test failed for a reason unrelated to query shape.
//
//
// THE FIXTURE NEEDS SEVERAL ROWS IN EVERY COLLECTION
//
// With one row each the product is one, and the defect is invisible, which is exactly why it survived review.
//
// The exact arithmetic is asserted rather than "greater than", because the arithmetic IS the finding and a vague
// assertion would still pass if the product quietly grew.
//
// Child rows are inserted directly rather than through the aggregate's own methods, because those also advance the
// supplier's onboarding state, which writes the supplier row, and a background job triggered by registration
// advances its version concurrently, so the save fails for reasons that have nothing to do with what this measures.
// Inserting children touches no supplier row.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierProfileQueryShapeTests(PostgresApiFixture fixture)
{
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref Count);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private const int PerCollection = 4;

    private async Task<Guid> SeedSupplierWithChildrenAsync()
    {
        var name = $"Query Shape {Guid.NewGuid():N}"[..24];
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplierId = await db.Suppliers.Where(s => s.DisplayNameEn == name)
            .Select(s => s.Id).SingleAsync();

        for (var i = 0; i < PerCollection; i++)
        {
            db.Addresses.Add(new Address
            {
                Id = Guid.CreateVersion7(), SupplierId = supplierId, Kind = AddressKind.Billing,
                Line1 = $"Line {i}", City = "Damascus", RegionCode = "DAM", Country = "SY",
            });
            db.Contacts.Add(new Contact
            {
                Id = Guid.CreateVersion7(), SupplierId = supplierId,
                FullName = $"Contact {i}", Email = $"c{i}-{Guid.NewGuid():N}@example.com",
            });
            db.Branches.Add(new Branch
            {
                Id = Guid.CreateVersion7(), SupplierId = supplierId,
                NameAr = $"فرع {i}", NameEn = $"Branch {i}",
            });
        }

        await db.SaveChangesAsync();
        return supplierId;
    }

    private DbContextOptions<AppDbContext> OptionsWith(CommandCounter counter)
    {
        using var scope = fixture.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Database.GetConnectionString();

        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(counter)
            .Options;
    }

    [Fact]
    public async Task Split_query_issues_one_statement_per_collection_instead_of_one_cartesian_statement()
    {
        var supplierId = await SeedSupplierWithChildrenAsync();

        var singleCounter = new CommandCounter();
        await using (var db = new AppDbContext(OptionsWith(singleCounter)))
        {
            _ = await db.Suppliers
                .Include(s => s.Representatives).Include(s => s.Addresses).Include(s => s.Contacts)
                .Include(s => s.Branches).Include(s => s.BankAccounts).Include(s => s.CategoryLinks)
                .AsSingleQuery()
                .FirstAsync(s => s.Id == supplierId);
        }

        var splitCounter = new CommandCounter();
        await using (var db = new AppDbContext(OptionsWith(splitCounter)))
        {
            _ = await db.Suppliers.IncludeProfile().FirstAsync(s => s.Id == supplierId);
        }

        singleCounter.Count.Should().Be(1, "the old shape loaded everything in one joined statement");
        splitCounter.Count.Should().Be(7,
            "one statement for the supplier plus one per collection - cost adds instead of multiplying");
    }

    [Fact]
    public async Task The_single_statement_shape_returns_multiplicatively_more_rows_than_the_data_contains()
    {
        var supplierId = await SeedSupplierWithChildrenAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*)
            FROM supplier.supplier s
            LEFT JOIN supplier.representative  r ON r.\"SupplierId\" = s.\"Id\"
            LEFT JOIN supplier.address         a ON a.\"SupplierId\" = s.\"Id\"
            LEFT JOIN supplier.contact         c ON c.\"SupplierId\" = s.\"Id\"
            LEFT JOIN supplier.branch          b ON b.\"SupplierId\" = s.\"Id\"
            LEFT JOIN supplier.bank_account    k ON k.\"SupplierId\" = s.\"Id\"
            LEFT JOIN supplier.category_link   l ON l.\"SupplierId\" = s.\"Id\"
            WHERE s.\"Id\" = @id
            """.Replace("\\\"", "\"");
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = supplierId;
        command.Parameters.Add(parameter);

        var joinedRows = Convert.ToInt64(await command.ExecuteScalarAsync());

        var actualChildRows = await db.Addresses.CountAsync(a => a.SupplierId == supplierId)
            + await db.Contacts.CountAsync(c => c.SupplierId == supplierId)
            + await db.Branches.CountAsync(b => b.SupplierId == supplierId)
            + await db.Representatives.CountAsync(r => r.SupplierId == supplierId);

        joinedRows.Should().Be(64,
            $"the join builds {joinedRows} rows to describe {actualChildRows} child records; the " +
            "gap is the cartesian product and it grows with the PRODUCT of collection sizes, not " +
            "their sum - a supplier with 5 of each would be 15,625 rows for one DTO");
        actualChildRows.Should().Be(13);
    }
}
