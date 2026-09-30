// Which ERP supplier may carry which registration number, given the portal allows one supplier per number.
//
// THE SHARED NUMBER IS REAL. The Seven Gates server gives 14142 to two suppliers, and written straight through the
// second would hit the unique index and not be imported at all. So the test uses that shape: the first in the ERP's
// order keeps the number, the second arrives without it and is told who has it.
//
// THE SAME SUPPLIER HOLDING ITS OWN NUMBER IS THE CONTROL. A second run of the import finds every number already in
// the portal - held by the very supplier being updated - and must keep them all; a rule that refused a number
// because somebody held it would empty the registry's registration numbers on the second run.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpRegistrationNumbersTests
{
    private static ErpSupplier Supplier(string id, string? registration) =>
        ErpSupplierTestFactory.Supplier(id) with { RegistrationNumber = registration };

    private static readonly Dictionary<string, RegistrationNumberHolder> NobodyHoldsAny = new(StringComparer.Ordinal);

    [Fact]
    public void Two_erp_suppliers_with_one_number_the_first_keeps_it_and_the_second_is_told()
    {
        var decisions = ErpRegistrationNumbers.Decide(
            [Supplier("Ziad Al - Khen", "14142"), Supplier("Alloush Co", "14142")], NobodyHoldsAny);

        decisions["Ziad Al - Khen"].Number.Should().Be("14142");
        decisions["Alloush Co"].Number.Should().BeNull(
            "written straight through, the unique index would refuse the whole supplier over this one field");
        decisions["Alloush Co"].Note.Should().Contain("Ziad Al - Khen");
    }

    [Fact]
    public void A_number_on_another_portal_supplier_is_not_taken_from_them()
    {
        var held = new Dictionary<string, RegistrationNumberHolder>(StringComparer.Ordinal)
        {
            ["73260"] = new RegistrationNumberHolder(null, "SUP-2026-000004"),
        };

        var decision = ErpRegistrationNumbers.Decide([Supplier("AL-Zaeim", "73260")], held)["AL-Zaeim"];

        decision.Number.Should().BeNull();
        decision.Note.Should().Contain("SUP-2026-000004");
    }

    [Fact]
    public void The_supplier_that_already_holds_its_number_keeps_it()
    {
        var held = new Dictionary<string, RegistrationNumberHolder>(StringComparer.Ordinal)
        {
            ["73260"] = new RegistrationNumberHolder("AL-Zaeim", "SUP-2026-000004"),
        };

        var decision = ErpRegistrationNumbers.Decide([Supplier("AL-Zaeim", " 73260 ")], held)["AL-Zaeim"];

        decision.Number.Should().Be("73260", "numbers are compared trimmed, as the unique index compares them");
        decision.Note.Should().BeNull();
    }

    [Fact]
    public void No_number_is_no_decision_to_explain()
    {
        var decision = ErpRegistrationNumbers.Decide([Supplier("A", "  ")], NobodyHoldsAny)["A"];

        decision.Number.Should().BeNull();
        decision.Note.Should().BeNull();
    }

    [Fact]
    public void A_number_longer_than_its_column_is_left_empty_rather_than_cut()
    {
        var decision = ErpRegistrationNumbers.Decide([Supplier("A", new string('1', 120))], NobodyHoldsAny)["A"];

        decision.Number.Should().BeNull("a registration number cut short is a different number");
        decision.Note.Should().Contain("120 characters");
    }
}
