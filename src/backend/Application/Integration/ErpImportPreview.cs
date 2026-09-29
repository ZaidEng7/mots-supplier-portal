// What importing the ERP's suppliers would do, before anything does it.
//
// WHY A PREVIEW IS THE FIRST THING SHIPPED. This import creates approved supplier accounts in a registry the
// ministry's dashboard reads. It lands roughly eighty rows that nobody in this product has seen, from a system
// whose data entry conventions nobody here controls. An import that writes first and reports afterwards gives
// its operator one choice - undo eighty rows by hand - so the reporting comes first and the writing follows in
// its own change.
//
// REFUSE IS NOT AN ERROR. A supplier the portal cannot admit is an ordinary outcome of this import, not a
// failure of it: the whole run should finish and hand back a list. A run that stopped at the first unusable row
// would have to be re-run after every fix, and eighty rows means eighty possible stops.
//
// THE NOTES ARE THE DELIVERABLE. Counts tell an operator how big the job is; the notes tell them what they are
// agreeing to. "Category left unset" and "no address" are consequences somebody has to accept before the write
// happens, and they are invisible in a summary.
//
// ROWS ARE RETURNED IN FULL RATHER THAN SAMPLED. Eighty rows is a list a person reads, and a preview that
// truncated would hide exactly the unusual rows it exists to surface.

namespace MotsSupplierPortal.Application.Integration;

public enum ErpImportAction
{
    Create,
    Update,
    Refuse,
    Suspend,
}

public sealed record ErpImportPreviewRow(
    string ExternalId,
    string? Name,
    ErpImportAction Action,
    IReadOnlyList<string> Notes,
    string? MatchedReferenceCode);

// WouldSuspend counts every supplier the run would suspend: portal suppliers the ERP no longer returns, and ERP rows it
// now turns away. So the ERP's rows add up as WouldCreate + WouldUpdate + Refused + the Suspend rows the ERP returned;
// the rest of WouldSuspend is the portal's. SuspensionsHeldBack is set when the run would refuse to suspend some or
// all of them, and says why - see ErpMissingSupplierPolicy.
public sealed record ErpImportPreviewReport(
    int ErpSupplierCount,
    int WouldCreate,
    int WouldUpdate,
    int Refused,
    IReadOnlyList<ErpImportPreviewRow> Rows,
    int WouldSuspend = 0,
    string? SuspensionsHeldBack = null);

public interface IPreviewErpImportHandler
{
    Task<ErpImportPreviewReport> HandleAsync(CancellationToken ct);
}
