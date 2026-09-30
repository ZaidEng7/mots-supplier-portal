// What an import does about the ERP's standing for a supplier the portal already holds: the change it makes, whether
// the plan holds that change back, and the notes that say so. ErpImportPreviewBuilder reports it and
// RunErpImportHandler applies it.
//
// THIS IS ONE PLACE BECAUSE THE PREVIEW AND THE RUN MUST SAY THE SAME THING. Both call Decide with the facts they
// each read - the supplier's memory and lifecycle, the ERP's standing and why it turns the supplier away, whether its
// documents allow a release, whether the plan holds turned-away suppliers back, and whether it is marked as gone - so
// a note or a held-back rule changed here changes both. A forecast that promises what the run does not do is worse
// than none, because it was believed.
//
// IT DECIDES AND CHANGES NOTHING. Supplier.ErpDisabledChangeFor works out the change, and the run makes it through
// Supplier.RecordErpStanding, which calls the same method on the same memory, so the change the run records is the
// Change decided here. The run writes the audit row for it; there is no database and no audit here.
//
// A CHANGE HELD BACK BY THE PLAN RECORDS NOTHING. While the plan holds a mass turn-away back, a supplier the ERP turns
// away keeps its memory as it is, because the read that caused the hold is not believed: Change is None, the run does
// not call RecordErpStanding, and a supplier marked as gone keeps the mark - MarksSynced is false - so nothing
// reinstates it automatically until a run the plan believes. Forecast is the change there would otherwise have been,
// which is how the note tells a change held back in this run from one an earlier run already made.
//
// WHEN A MARK CLEARS, THE AUTOMATIC REINSTATEMENT IS ASKED AGAIN. AsksReinstatement is set when the ERP returns a
// supplier marked as gone, or lets one marked as turned away be used again. It is set for a supplier marked as gone
// even when the plan holds it back and the mark stays; AutomaticReinstatement refuses while a mark stands, so that
// question is asked in vain. The preview does not forecast the reinstatement, because whether it happens depends on
// the supplier's documents at the moment the run asks.
//
// THE NOTES ARE WORDED IN ErpImportAdmission; this picks which one a row carries. A supplier the portal does not hold
// yet is not decided here: it arrives with the standing ErpImportAdmission gave it, and its ArrivalNote says so.

namespace MotsSupplierPortal.Application.Integration;

using MotsSupplierPortal.Domain.Suppliers;

public sealed record ErpStandingDecision(
    ErpDisabledChange Change,
    bool HeldByLimit,
    ErpDisabledChange Forecast,
    IReadOnlyList<string> Notes,
    bool MarksSynced,
    bool AsksReinstatement)
{
    public static ErpStandingDecision Decide(
        SupplierErpDisabledState memory,
        bool isActive,
        ErpStanding standing,
        string? turnedAway,
        bool documentsAllowRelease,
        bool planHoldsTurnedAway,
        bool markedRemovedFromErp)
    {
        var forecast = Supplier.ErpDisabledChangeFor(memory, isActive, standing, documentsAllowRelease);
        var heldByLimit = planHoldsTurnedAway && standing != ErpStanding.Usable;
        var change = heldByLimit ? ErpDisabledChange.None : forecast;

        var notes = new List<string>();

        if (turnedAway is not null)
        {
            notes.Add(heldByLimit && forecast != ErpDisabledChange.None
                ? ErpImportAdmission.HeldBackNote(turnedAway)
                : ErpImportAdmission.TurnedAwayNote(turnedAway, change));
        }

        if (change == ErpDisabledChange.Released)
        {
            notes.Add(ErpImportAdmission.ReleasedNote);
        }

        if (change == ErpDisabledChange.ReleaseWaitsForDocuments)
        {
            notes.Add(ErpImportAdmission.ReleaseWaitsNote);
        }

        return new ErpStandingDecision(
            Change: change,
            HeldByLimit: heldByLimit,
            Forecast: forecast,
            Notes: notes,
            MarksSynced: !(heldByLimit && markedRemovedFromErp),
            AsksReinstatement: markedRemovedFromErp || change == ErpDisabledChange.Cleared);
    }
}
