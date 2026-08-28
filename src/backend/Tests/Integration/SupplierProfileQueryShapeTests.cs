using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-66: <c>IncludeProfile</c> loads six child collections. In one statement that is a CARTESIAN
/// PRODUCT - the supplier row repeats once per combination across all six, so rows multiply rather
/// than add. EF de-duplicates the entities afterwards, which is why the DTO looked correct while
/// the database did asymptotically more work.
///
/// This compares the two shapes in a single test using <c>AsSingleQuery()</c> to reproduce the old
/// behaviour, rather than relying on someone reverting the fix to see the difference. The
/// comparison cannot rot: if the production extension stops splitting, the split figures become the
/// single figures and the assertions fail.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierProfileQueryShapeTests(PostgresApiFixture fixture)
{
    /// <summary>Counts executed commands. Row multiplication is measured separately with raw SQL,
    /// because the whole point is that EF hides it from the materialised result.</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref Count);
            return base.ReaderExecuting(command, eventData, result);
        }

        // The async override matters: EF routes async queries here and not through the sync method,
        // so overriding only the sync one silently counts nothing - the counter read 0 and the test
        // failed for a reason unrelated to query shape.
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

        // Child rows are inserted directly rather than through the aggregate's Add* methods. Those
        // also advance the supplier's onboarding state, which UPDATEs the supplier row - and a
        // background job triggered by registration bumps its xmin concurrently, so the save fails
        // with DbUpdateConcurrencyException for reasons that have nothing to do with what this
        // class measures. Inserting children touches no supplier row.
        //
        // Several rows in EVERY collection: with one row each the product is 1x1x1x1x1x1 and the
        // defect is invisible, which is exactly why it survived review.
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

        // What the six-way join actually asks Postgres to build, measured rather than reasoned
        // about. EF would collapse these back into one aggregate, hiding the cost completely.
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

        // 4 rows in each of three collections, and none in the other three, gives
        // 1 x 4 x 4 x 4 x 1 x 1 = 64 rows to describe 13 child records. An exact assertion rather
        // than "greater than": the arithmetic IS the finding, and a vague assertion would still
        // pass if the product quietly grew.
        joinedRows.Should().Be(64,
            $"the join builds {joinedRows} rows to describe {actualChildRows} child records; the " +
            "gap is the cartesian product and it grows with the PRODUCT of collection sizes, not " +
            "their sum - a supplier with 5 of each would be 15,625 rows for one DTO");
        actualChildRows.Should().Be(13);
    }
}
