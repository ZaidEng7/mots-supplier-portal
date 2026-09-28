// What actually happened when the import ran.
//
// IT MIRRORS THE PREVIEW'S SHAPE ON PURPOSE. An operator reads the forecast, presses the button, and reads this;
// the only way to tell whether the forecast was honest is for the two to be comparable at a glance. Different
// field names for the same four numbers would make that a translation exercise at exactly the moment somebody is
// deciding whether to trust the thing.
//
// FAILED IS ITS OWN COUNT, SEPARATE FROM REFUSED. A refusal is this product declining a supplier it cannot
// represent - no email, a currency it does not know - and is an expected outcome. A failure is the import going
// wrong: a duplicate account, a database refusal, something nobody anticipated. Collapsing them into one number
// would hide a defect inside an expected result, which is how a broken import reports a clean run.
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
}

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
    IReadOnlyList<ErpImportResultRow> Rows);

public interface IRunErpImportHandler
{
    Task<ErpImportRunReport> HandleAsync(CancellationToken ct);
}
