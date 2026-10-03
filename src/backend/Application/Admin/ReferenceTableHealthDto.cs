// The health of one reference table: how many of its codes are active and how many are switched off.
//
// Health rather than a listing. A table with no active codes is a configuration fault that blocks registration, and
// before the administrator's screens reported it, it was invisible. The dashboard's system health section reports one
// of these per table the registry declares, read through OperationalHealthReads.

namespace MotsSupplierPortal.Application.Admin;

public sealed record ReferenceTableHealthDto(string Table, int Active, int Inactive);
