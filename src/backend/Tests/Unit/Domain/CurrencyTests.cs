// The currency reference record.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.ReferenceData;

public class CurrencyTests
{
    [Fact]
    public void Currency_defaults_to_active()
    {
        var currency = new Currency { Id = Guid.NewGuid(), Code = "SYP", NameAr = "ليرة سورية", NameEn = "Syrian Pound" };

        currency.IsActive.Should().BeTrue();
    }
}
