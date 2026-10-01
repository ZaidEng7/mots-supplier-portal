// What the import would do with each supplier the ERP sent.
//
// THE DENOMINATOR IS ASSERTED. Every test that counts outcomes also checks, through CountsAddUp, that each supplier the
// ERP sent has exactly one row, counted once as a create, an update, a refusal or a suspension, because a rule that
// quietly dropped a row would otherwise show up as a smaller number that still looks plausible - and an import whose
// preview undercounts is worse than no preview. A portal supplier the ERP no longer returns adds a Suspend row of its
// own, counted only in WouldSuspend, which is why the helper is given the list the ERP sent.
//
// A GAP IS NEVER A REFUSAL. Every supplier in the ERP is meant to appear in the portal, so a supplier with no email,
// no name or an unknown currency is a create with the gap filled and noted. How each gap is filled is tested directly
// in ErpImportAdmissionTests; these check the forecast carries it. The one row the preview refuses is a probable
// rename, which ErpSyncPlanTests covers.
//
// THE NULL-TAX-NUMBER CASE IS NOT PEDANTRY. Most suppliers have no tax number, and a duplicate check that
// grouped them under an empty key would flag a possible duplicate on every single row - which is how a warning
// becomes something people scroll past.
//
// AN UNKNOWN CURRENCY IS LEFT EMPTY, NOT DEFAULTED. Defaulting to SYP would silently reprice a relationship and
// the ministry's feed would report that price as fact; empty says "not known", which is true.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpImportPreviewBuilderTests
{
    private static readonly Dictionary<string, ErpImportCandidateMatch> NoMatches = [];
    private static readonly Dictionary<string, string> NoUnlinked = [];

    private static ErpSupplier Supplier(
        string externalId = "Damascus Supplies Co",
        string? name = "Damascus Supplies Co",
        string? email = "contact@example.sy",
        string? currency = "SYP",
        string? taxId = "TAX-100",
        string? phone = "+963 11 000 0000",
        string? group = "Local",
        string? primaryAddress = "Damascus Supplies Co-Billing",
        bool disabled = false) =>
        ErpSupplierTestFactory.Supplier(externalId) with
        {
            Name = name,
            SupplierGroup = group,
            TaxId = taxId,
            Email = email,
            Phone = phone,
            Disabled = disabled,
            Currency = currency,
            PrimaryAddressName = primaryAddress,
            PrimaryContactName = "Damascus Supplies Co-Contact",
            CreatedAt = DateTimeOffset.Parse("2026-09-16T12:33:48+03:00"),
            ModifiedAt = DateTimeOffset.Parse("2026-09-16T12:33:48+03:00"),
        };

    private static void CountsAddUp(ErpImportPreviewReport report, IReadOnlyList<ErpSupplier> sent)
    {
        var fromErp = sent.Select(s => s.ExternalId).ToHashSet(StringComparer.Ordinal);
        var erpRows = report.Rows.Where(r => fromErp.Contains(r.ExternalId)).ToList();
        var erpRowsSuspended = erpRows.Count(r => r.Action == ErpImportAction.Suspend);

        report.ErpSupplierCount.Should().Be(sent.Count);
        erpRows.Should().HaveCount(
            report.ErpSupplierCount,
            "a rule that dropped a row would otherwise show as a smaller number that still looks plausible");
        (report.WouldCreate + report.WouldUpdate + report.Refused + erpRowsSuspended).Should().Be(
            report.ErpSupplierCount, "each supplier the ERP sent is counted once");
        (report.WouldCreate + report.WouldUpdate + report.Refused + report.WouldSuspend).Should().Be(
            report.Rows.Count, "a supplier the ERP no longer returns is counted only as a suspension");
    }

    [Fact]
    public void A_supplier_with_everything_would_be_created_with_nothing_refused()
    {
        ErpSupplier[] sent = [Supplier()];
        var report = ErpImportPreviewBuilder.Build(sent, NoMatches, NoUnlinked);

        CountsAddUp(report, sent);
        report.WouldCreate.Should().Be(1);
        report.Refused.Should().Be(0);
        report.Rows[0].Action.Should().Be(ErpImportAction.Create);
        report.Rows[0].MatchedReferenceCode.Should().BeNull();
    }

    [Fact]
    public void A_supplier_with_no_email_would_be_created_with_a_placeholder_rather_than_refused()
    {
        ErpSupplier[] sent = [Supplier(email: null)];
        var report = ErpImportPreviewBuilder.Build(sent, NoMatches, NoUnlinked);

        CountsAddUp(report, sent);
        report.WouldCreate.Should().Be(1, "every supplier in the ERP is meant to appear in the portal");
        report.Refused.Should().Be(0);
        report.Rows[0].Notes.Should().ContainMatch("*placeholder*@erp-import.invalid*");
    }

    [Fact]
    public void A_supplier_with_no_name_would_be_created_under_its_identifier()
    {
        var report = ErpImportPreviewBuilder.Build([Supplier(name: "  ")], NoMatches, NoUnlinked);

        report.Rows[0].Action.Should().Be(ErpImportAction.Create);
        report.Rows[0].Name.Should().Be("Damascus Supplies Co");
    }

    [Fact]
    public void A_currency_the_portal_does_not_know_is_left_empty_rather_than_defaulted_to_syp()
    {
        var report = ErpImportPreviewBuilder.Build([Supplier(currency: "EUR")], NoMatches, NoUnlinked);

        report.Rows[0].Action.Should().Be(ErpImportAction.Create);
        report.Rows[0].Notes.Should().ContainMatch("*'EUR'*left empty*");
    }

    [Fact]
    public void A_supplier_the_portal_already_carries_would_be_updated()
    {
        var matches = new Dictionary<string, ErpImportCandidateMatch>
        {
            ["Damascus Supplies Co"] = new("SUP-2026-000004", "TAX-100"),
        };

        ErpSupplier[] sent = [Supplier()];
        var report = ErpImportPreviewBuilder.Build(sent, matches, NoUnlinked);

        CountsAddUp(report, sent);
        report.WouldUpdate.Should().Be(1);
        report.Rows[0].Action.Should().Be(ErpImportAction.Update);
        report.Rows[0].MatchedReferenceCode.Should().Be("SUP-2026-000004");
    }

    [Fact]
    public void A_tax_number_already_on_a_self_registered_supplier_is_reported_rather_than_merged()
    {
        var unlinked = new Dictionary<string, string> { ["TAX-100"] = "SUP-2026-000009" };

        var report = ErpImportPreviewBuilder.Build([Supplier()], NoMatches, unlinked);

        report.Rows[0].Action.Should().Be(
            ErpImportAction.Create,
            "merging on a field that is sometimes blank and sometimes shared between a company and its "
            + "subsidiary is not a decision this should make");
        report.Rows[0].Notes.Should().ContainMatch("*TAX-100*SUP-2026-000009*Needs a person*");
    }

    [Fact]
    public void A_supplier_with_no_tax_number_never_collides_with_one()
    {
        // The empty-string key is the trap, and it has to be IN the dictionary for this test to mean anything.
        // A lookup written as "TaxId ?? string.Empty" misses a dictionary that holds only real tax numbers, so a
        // version of this test without that entry passes whether the guard exists or not - which is how it was
        // first written here, and it caught nothing.
        var unlinked = new Dictionary<string, string>
        {
            ["TAX-100"] = "SUP-2026-000009",
            [string.Empty] = "SUP-2026-000010",
        };

        var report = ErpImportPreviewBuilder.Build([Supplier(taxId: null)], NoMatches, unlinked);

        report.Rows[0].Notes.Should().NotContainMatch("*Needs a person*",
            "most suppliers have no tax number, and flagging every one of them buries the real collisions");
    }

    [Fact]
    public void The_gaps_the_erp_cannot_fill_are_stated_on_every_row()
    {
        var report = ErpImportPreviewBuilder.Build(
            [Supplier(group: "Raw Material", primaryAddress: null, phone: null)], NoMatches, NoUnlinked);

        report.Rows[0].Notes.Should().ContainMatch("*'Raw Material' is kept*no portal category*");
        report.Rows[0].Notes.Should().ContainMatch("*No address*governorate*");
        report.Rows[0].Notes.Should().ContainMatch("*No Arabic name*starts with the English name*");
        report.Rows[0].Notes.Should().ContainMatch("*No phone*");
    }

    [Fact]
    public void A_supplier_disabled_in_the_erp_is_noted_as_arriving_suspended()
    {
        var report = ErpImportPreviewBuilder.Build([Supplier(disabled: true)], NoMatches, NoUnlinked);

        report.Rows[0].Notes.Should().ContainMatch("*Disabled in the ERP*suspended*");
    }

    [Fact]
    public void A_mixed_run_leaves_nobody_out()
    {
        ErpSupplier[] suppliers =
        [
            Supplier("A", "A"),
            Supplier("B", "B", email: null),
            Supplier("C", "C", currency: "EUR"),
            Supplier("D", "D"),
        ];

        var matches = new Dictionary<string, ErpImportCandidateMatch>
        {
            ["D"] = new("SUP-2026-000001", null),
        };

        var report = ErpImportPreviewBuilder.Build(suppliers, matches, NoUnlinked);

        CountsAddUp(report, suppliers);
        report.ErpSupplierCount.Should().Be(4);
        report.WouldCreate.Should().Be(3, "the two with gaps are filled in, not left out");
        report.WouldUpdate.Should().Be(1);
        report.Refused.Should().Be(0);
    }

    [Fact]
    public void A_supplier_the_run_would_suspend_is_counted_once_whether_the_erp_sent_it_or_dropped_it()
    {
        ErpSupplier[] sent = [Supplier("A", "A"), Supplier("B", "B", disabled: true)];

        var matches = new Dictionary<string, ErpImportCandidateMatch>
        {
            ["B"] = new("SUP-2026-000002", null),
            ["C"] = new("SUP-2026-000003", null, Name: "C"),
        };

        var report = ErpImportPreviewBuilder.Build(sent, matches, NoUnlinked);

        CountsAddUp(report, sent);
        report.WouldCreate.Should().Be(1);
        report.WouldSuspend.Should().Be(2, "B is disabled in the ERP and C is no longer in it");
        report.Rows.Single(r => r.ExternalId == "B").Action.Should().Be(ErpImportAction.Suspend);
        report.Rows.Single(r => r.ExternalId == "C").Action.Should().Be(ErpImportAction.Suspend);
    }

    // THE PORTAL'S OWN CREATE IS FORECAST AS THE RUN TREATS IT: refused with the note that says the push links it, and
    // never a Create, which would have made a second portal supplier the push then posted past.
    [Fact]
    public void An_erp_supplier_the_portals_push_created_and_nobody_carries_would_be_held_for_the_push()
    {
        ErpSupplier[] sent = [Supplier(externalId: "SUP-2026-00042", taxId: null) with { CreatedByPortal = true }];

        var report = ErpImportPreviewBuilder.Build(sent, NoMatches, NoUnlinked);

        CountsAddUp(report, sent);
        report.WouldCreate.Should().Be(0);
        var row = report.Rows.Should().ContainSingle().Subject;
        row.Action.Should().Be(ErpImportAction.Refuse);
        row.Notes.Should().Equal(ErpImportPreviewBuilder.HeldForPushNote);
    }

    [Fact]
    public void A_pushed_supplier_on_its_first_sighting_would_be_suspended_while_the_others_are_held_back()
    {
        var sent = Enumerable.Range(1, 20)
            .Select(i => Supplier(externalId: $"S{i}", taxId: null) with
            {
                WorkflowState = i <= 6 ? "Pending Chief Accountant Approval" : null,
            })
            .Append(Supplier(externalId: "P1", taxId: null) with { WorkflowState = "Draft" })
            .ToList();
        var matches = sent.ToDictionary(
            s => s.ExternalId,
            s => new ErpImportCandidateMatch(
                "REF-" + s.ExternalId, null, PushedByPortal: s.ExternalId == "P1", SeenByImport: s.ExternalId != "P1"));

        var report = ErpImportPreviewBuilder.Build(sent, matches, NoUnlinked);

        CountsAddUp(report, sent);
        report.SuspensionsHeldBack.Should().Contain("6 it turned away");
        report.Rows.Single(r => r.ExternalId == "P1").Action.Should().Be(
            ErpImportAction.Suspend, "the run suspends it on its own, and the forecast says the same");
        report.Rows.Single(r => r.ExternalId == "S1").Action.Should().Be(ErpImportAction.Update);
    }
}
