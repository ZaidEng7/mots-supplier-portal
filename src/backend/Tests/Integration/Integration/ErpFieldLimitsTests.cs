// The lengths the ERP import measures against are the lengths the database enforces.
//
// ErpFieldLimits holds them as numbers so the preview can apply them without a database. Numbers written twice drift,
// and a limit longer than its column is the defect it exists to prevent: a value that passes the check and is then
// refused by the database, taking the supplier with it. So each is read back from the model the database was built
// from.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ErpFieldLimitsTests(PostgresApiFixture fixture)
{
    [Fact]
    public void Every_limit_the_import_measures_is_the_column_length_the_database_enforces()
    {
        using var scope = fixture.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;

        int Length(Type type, string property, Type? owned = null)
        {
            var entity = model.FindEntityType(type)!;
            var target = owned is null
                ? entity
                : entity.GetNavigations().Single(n => n.ClrType == owned).TargetEntityType;
            return target.FindProperty(property)!.GetMaxLength()!.Value;
        }

        Length(typeof(Supplier), nameof(Supplier.DisplayNameAr)).Should().Be(ErpFieldLimits.Name);
        Length(typeof(Supplier), nameof(Supplier.Description)).Should().Be(ErpFieldLimits.Description);
        Length(typeof(Supplier), nameof(Supplier.SupplierGroup)).Should().Be(ErpFieldLimits.SupplierGroup);
        Length(typeof(Supplier), nameof(LegalInfo.RegistrationNumber), typeof(LegalInfo))
            .Should().Be(ErpFieldLimits.RegistrationNumber);
        Length(typeof(Supplier), nameof(LegalInfo.RegistrationType), typeof(LegalInfo))
            .Should().Be(ErpFieldLimits.RegistrationType);
        Length(typeof(Supplier), nameof(LegalInfo.LegalNameAr), typeof(LegalInfo)).Should().Be(ErpFieldLimits.Name);
        Length(typeof(Representative), nameof(Representative.FullName)).Should().Be(ErpFieldLimits.PersonName);
        Length(typeof(Address), nameof(Address.Line1)).Should().Be(ErpFieldLimits.AddressLine);
        Length(typeof(Address), nameof(Address.Line2)).Should().Be(ErpFieldLimits.AddressLine);
        Length(typeof(Address), nameof(Address.City)).Should().Be(ErpFieldLimits.City);
    }

    // THE ERP'S NAME FOR A SUPPLIER IS SAVED WHOLE, never measured and cut, because it is what every run matches on.
    // The ERP names a record in up to 140 characters, and a server that names suppliers by supplier_name lets a pushed
    // name be that long; the column held 100, so the push created such a supplier and could never save its link.
    [Fact]
    public void The_erp_identifier_column_holds_the_longest_name_the_erp_gives_a_record()
    {
        using var scope = fixture.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;

        model.FindEntityType(typeof(Supplier))!.FindProperty(nameof(Supplier.ExternalId))!.GetMaxLength()
            .Should().Be(140);
    }
}
