using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// MSP-84: the six profile child collections on Supplier (Representatives, Addresses, Contacts,
/// Branches, BankAccounts, CategoryLinks) had no cap on any Add* method before this - a scan
/// during MSP-84's investigation confirmed all six. Real cursor pagination was scoped out for
/// these (unlike Review Queue/Team Members/Sessions) since they are business-bounded, not
/// genuinely unbounded - see MaxRepresentatives etc. on Supplier.cs for the reasoning. What
/// replaces pagination here is proving both directions of the guard, per this week's standing
/// rule: the cap is not silently absent (exceeding it must fail) and not silently wrong in the
/// other direction (reaching exactly the cap must still succeed).
/// </summary>
public sealed class SupplierProfileCollectionCapTests
{
    private static Supplier EditableSupplier()
    {
        var supplier = Supplier.Register(
            $"SUP-2026-{Random.Shared.Next(1, 999_999):D6}", "شركة الاختبار", "Cap Test Co",
            "CR-CAP-1", "Tester", "tester@example.com");
        supplier.MarkEmailVerified();
        return supplier;
    }

    [Fact]
    public void Representatives_can_reach_the_cap_but_not_exceed_it()
    {
        var supplier = EditableSupplier();
        // One representative already exists from Register(); fill up to the cap.
        for (var i = supplier.Representatives.Count; i < 20; i++)
        {
            supplier.AddRepresentative($"Rep {i}", $"rep{i}@example.com", null, null);
        }
        supplier.Representatives.Should().HaveCount(20);

        var act = () => supplier.AddRepresentative("One Too Many", "over@example.com", null, null);
        act.Should().Throw<DomainException>().WithMessage("*at most 20 representatives*");
    }

    [Fact]
    public void Addresses_can_reach_the_cap_but_not_exceed_it()
    {
        var supplier = EditableSupplier();
        for (var i = 0; i < 20; i++)
        {
            supplier.AddAddress(AddressKind.Branch, $"Line {i}", null, "Damascus", "DM", "SY", null, null, null);
        }
        supplier.Addresses.Should().HaveCount(20);

        var act = () => supplier.AddAddress(AddressKind.Branch, "Overflow", null, "Damascus", "DM", "SY", null, null, null);
        act.Should().Throw<DomainException>().WithMessage("*at most 20 addresses*");
    }

    [Fact]
    public void Contacts_can_reach_the_cap_but_not_exceed_it()
    {
        var supplier = EditableSupplier();
        for (var i = 0; i < 20; i++)
        {
            supplier.AddContact($"Contact {i}", $"contact{i}@example.com", null, null);
        }
        supplier.Contacts.Should().HaveCount(20);

        var act = () => supplier.AddContact("Overflow", "overflow@example.com", null, null);
        act.Should().Throw<DomainException>().WithMessage("*at most 20 contacts*");
    }

    [Fact]
    public void Branches_can_reach_the_cap_but_not_exceed_it()
    {
        var supplier = EditableSupplier();
        for (var i = 0; i < 50; i++)
        {
            supplier.AddBranch($"فرع {i}", $"Branch {i}", null);
        }
        supplier.Branches.Should().HaveCount(50);

        var act = () => supplier.AddBranch("فرع زائد", "Overflow Branch", null);
        act.Should().Throw<DomainException>().WithMessage("*at most 50 branches*");
    }

    [Fact]
    public void BankAccounts_can_reach_the_cap_but_not_exceed_it()
    {
        var supplier = EditableSupplier();
        for (var i = 0; i < 10; i++)
        {
            supplier.AddBankAccount($"Holder {i}", "Bank", null, [1, 2, 3], $"****{i:D4}", null, "SYP", isComplianceCritical: false);
        }
        supplier.BankAccounts.Should().HaveCount(10);

        var act = () => supplier.AddBankAccount("Overflow", "Bank", null, [1, 2, 3], "****0000", null, "SYP", isComplianceCritical: false);
        act.Should().Throw<DomainException>().WithMessage("*at most 10 bank accounts*");
    }

    [Fact]
    public void CategoryLinks_can_reach_the_cap_but_not_exceed_it()
    {
        var supplier = EditableSupplier();
        for (var i = 0; i < 50; i++)
        {
            supplier.LinkCategory($"category-{i}", isComplianceCritical: false);
        }
        supplier.CategoryLinks.Should().HaveCount(50);

        var act = () => supplier.LinkCategory("category-overflow", isComplianceCritical: false);
        act.Should().Throw<DomainException>().WithMessage("*at most 50 categories*");
    }
}
