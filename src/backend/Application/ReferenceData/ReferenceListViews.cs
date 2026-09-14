// The five public reference lists a caller can read: categories, currencies, regions, units of
// measure and delivery terms.
//
// Every one is the same shape, deliberately, so the interface renders them all through one select
// control with no special case per list.
//
// The code is what everything else points at. Reference data is referenced by code rather than by a
// database link throughout this system, so these codes appear on tenders, offerings and bids.
//
// Both names travel together rather than one being chosen on the server, because the reader's
// language is the reader's, and the same list is read by screens in both.
//
// They were five files each named for a handler they did not contain: the handler is in
// Infrastructure, and each file held one shape and one interface.

namespace MotsSupplierPortal.Application.ReferenceData;

public sealed record CategoryDto(Guid Id, string Code, string NameAr, string NameEn);

public sealed record CurrencyDto(Guid Id, string Code, string NameAr, string NameEn);

public sealed record RegionDto(Guid Id, string Code, string NameAr, string NameEn);

public sealed record UnitOfMeasureDto(Guid Id, string Code, string NameAr, string NameEn);

public sealed record IncotermDto(Guid Id, string Code, string NameAr, string NameEn);
