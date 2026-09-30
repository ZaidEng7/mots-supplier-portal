// What actually happened when the import ran.
//
// IT MIRRORS THE PREVIEW'S SHAPE ON PURPOSE. An operator reads the forecast, presses the button, and reads this;
// the only way to tell whether the forecast was honest is for the two to be comparable at a glance. Different
// field names for the same four numbers would make that a translation exercise at exactly the moment somebody is
// deciding whether to trust the thing.
//
// FAILED IS ITS OWN COUNT, SEPARATE FROM REFUSED. A refusal is this product declining a supplier for a stated
// reason - a probable rename held for a person, or an address that already belongs to another account - and is an
// expected outcome. A failure is the import going wrong: a database refusal, something nobody anticipated.
// Collapsing them into one number would hide a defect inside an expected result, which is how a broken import
// reports a clean run.
//
// EVERY ROW SAYS WHAT BECAME OF IT, including the ones that did nothing. A supplier missing from this list is
// unaccounted for, and somebody reconciling eighty rows against the ERP by hand needs the list to be eighty long.

namespace MotsSupplierPortal.Application.Integration;

public enum ErpImportOutcome
{
    Created,
    Updated,
    Refused,
    Failed,
    Suspended,
}

// Who started a run. A scheduled run has no signed-in user, so its audit rows are attributed to the system rather
// than left without an actor - an unattributed change to the supplier registry is the kind somebody asks about.
public enum ErpImportTrigger
{
    Manual,
    Scheduled,
}

// Thrown when the ERP lock is taken: an import is already running, or a supplier push to the ERP is. The hourly job and
// a person pressing the button at the same moment would otherwise both find a supplier missing and both create it, and
// an import in the middle of a push would create a second supplier from the one the push has just made. The message
// says either may hold it, because telling a person another import is running while only a push is sends them looking
// for a run that does not exist.
public sealed class ErpImportBusyException()
    : Exception(
        "An import or a supplier push to the ERP is running, so the import did not start. Wait a minute or two and try "
        + "again.");

public sealed record ErpImportResultRow(
    string ExternalId,
    string? Name,
    ErpImportOutcome Outcome,
    string? ReferenceCode,
    IReadOnlyList<string> Notes);

public sealed record ErpImportRunReport(
    int ErpSupplierCount,
    int Created,
    int Updated,
    int Refused,
    int Failed,
    IReadOnlyList<ErpImportResultRow> Rows,
    int Suspended = 0,
    string? SuspensionsHeldBack = null);

public interface IRunErpImportHandler
{
    Task<ErpImportRunReport> HandleAsync(ErpImportTrigger trigger, CancellationToken ct);
}
