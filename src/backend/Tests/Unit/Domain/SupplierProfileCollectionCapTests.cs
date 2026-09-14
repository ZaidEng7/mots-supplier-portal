// The six profile child collections have a cap, and both directions of it are proven.
//
// None of the six had one before this, which a scan during that work confirmed.
//
// Real cursor paging was scoped out for these, unlike the queue, the team list and the sessions list, because
// they are business-bounded rather than genuinely unbounded; the caps on the record itself carry that reasoning.
//
// What replaces paging here is proving both directions of the guard, per the standing rule: the cap is not
// silently absent, so exceeding it must fail, and not silently wrong in the other direction, so reaching exactly
// the cap must still succeed.
//
// One representative already exists from registration, so the setup fills up to the cap from there.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

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
