// The administration of all six reference tables, in one handler.
//
// The contract's own header explains why this is one handler rather than six.
//
//
// DEACTIVATE, NEVER DELETE
//
// There is no delete operation, on purpose. Every one of these tables is referenced BY CODE from live rows, with
// no cascade and no fallback.
//
// Deleting a category a published tender points at would leave that tender describing a category that no longer
// exists. Deactivating it hides the code from new selections and leaves every existing row intact and readable.
//
// Reactivation is the same operation with the flag the other way, and it gets its own distinguishable audit action
// rather than sharing one. An audit reader asking "when did this stop being offered" should not have to read a
// boolean out of the row's history.
//
// Inactive rows are HIDDEN from the list by default and reachable by asking. An administrator editing the
// catalogue needs to see what they deactivated; otherwise deactivation looks like deletion and the next
// administrator re-creates the code, which is the one thing the no-delete rule exists to avoid.
//
//
// EVERY WRITE IS AUDITED
//
// Reference data decides what suppliers may register against and which documents they must produce, so "who added
// this document type, and when" is a governance question rather than a debugging one.
//
//
// EVERY TABLE IS NAMED IN EVERY SWITCH, AND AN UNKNOWN ONE THROWS
//
// This file has already paid for the alternative once. Every switch used a catch-all that meant document types, so
// when a sixth table was added it would have listed, created and deactivated DOCUMENT TYPES while the caller said
// something else.
//
// A failure naming the table is a bug report. A wrong answer that looks right is not. The exception is unreachable
// by construction and exists to stay that way.
//
//
// CODE LENGTH IS CHECKED HERE, PER TABLE
//
// Because the columns genuinely differ: two of the tables carry three-letter international standards and the other
// four carry this product's own codes and allow fifty.
//
// Checked here rather than left to the database, because a too-long code was answering with a server error from a
// string-truncation failure, which tells an administrator nothing about what to fix.
//
// A standards code is upper-cased on the way in, because the standard's own codes are and a bid is matched against
// them exactly. The same word in two cases naming two rows is the free-text problem this table exists to end,
// arriving through the administration screen instead of the bid form.
//
//
// THE DEFAULTS ON A NEW DOCUMENT TYPE, AND WHAT AN EDIT MUST NOT CLEAR
//
// A new type is not required and not tracked for expiry when the caller says nothing. Required by default would
// retroactively make every existing supplier's profile incomplete the moment the row is created, which is a live
// consequence for people who did nothing.
//
// It is not award-critical either, matching the migration that added that column. A new type is not award-critical
// until somebody says it is, and defaulting the other way would suspend suppliers over a type nobody had assessed.
//
// On an edit, an omitted flag means unchanged rather than false. A caller fixing an Arabic typo must not silently
// un-require a document type, and the award-critical flag is the one on this screen whose accidental change
// suspends live suppliers.
//
//
// THE LIST FILTERS IN THE DATABASE AND PROJECTS AND ORDERS IN MEMORY
//
// Deliberate rather than lazy. Projecting to a record with optional constructor parameters and then ordering by one
// of its properties is exactly the expression that either translates or throws depending on the provider version,
// and the first version of this method answered with a server error on every list.
//
// These are lookup tables of tens of rows, so the round trip is the same either way and this form cannot fail to
// translate.
//
// A write's response is read back through that same list projection, including inactive rows, so it is the same
// shape the list returns rather than a second hand-built one that could drift from it.

namespace MotsSupplierPortal.Infrastructure.ReferenceData;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ReferenceDataAdminHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IReferenceDataAdminHandler
{
    public async Task<IReadOnlyList<ReferenceItemDto>?> ListAsync(string table, bool includeInactive, CancellationToken ct)
    {
        if (!ReferenceTables.All.Contains(table)) return null;

        return table switch
        {
            ReferenceTables.Categories => await Project(db.Set<Category>(), includeInactive,
                c => new ReferenceItemDto(c.Code, c.NameAr, c.NameEn, c.IsActive), c => c.IsActive, ct),
            ReferenceTables.Currencies => await Project(db.Set<Currency>(), includeInactive,
                c => new ReferenceItemDto(c.Code, c.NameAr, c.NameEn, c.IsActive), c => c.IsActive, ct),
            ReferenceTables.UnitsOfMeasure => await Project(db.Set<UnitOfMeasure>(), includeInactive,
                u => new ReferenceItemDto(u.Code, u.NameAr, u.NameEn, u.IsActive), u => u.IsActive, ct),
            ReferenceTables.Regions => await Project(db.Set<Region>(), includeInactive,
                r => new ReferenceItemDto(r.Code, r.NameAr, r.NameEn, r.IsActive), r => r.IsActive, ct),
            ReferenceTables.Incoterms => await Project(db.Set<Incoterm>(), includeInactive,
                i => new ReferenceItemDto(i.Code, i.NameAr, i.NameEn, i.IsActive), i => i.IsActive, ct),
            ReferenceTables.DocumentTypes => await Project(db.Set<DocumentType>(), includeInactive,
                d => new ReferenceItemDto(d.Code, d.NameAr, d.NameEn, d.IsActive, d.IsRequired, d.ExpiryTracked, d.IsAwardCritical),
                d => d.IsActive, ct),
            _ => throw new UnreachableTableException(table),
        };
    }

    private static async Task<IReadOnlyList<ReferenceItemDto>> Project<T>(
        IQueryable<T> set, bool includeInactive,
        Func<T, ReferenceItemDto> select,
        System.Linq.Expressions.Expression<Func<T, bool>> isActive,
        CancellationToken ct)
        where T : class
    {
        var rows = await (includeInactive ? set : set.Where(isActive)).AsNoTracking().ToListAsync(ct);
        return [.. rows.Select(select).OrderBy(i => i.Code, StringComparer.Ordinal)];
    }

    public async Task<ReferenceDataResult> CreateAsync(CreateReferenceItemCommand command, CancellationToken ct)
    {
        if (!ReferenceTables.All.Contains(command.Table)) return new ReferenceDataResult.UnknownTable();

        var code = command.Code.Trim();
        if (code.Length == 0) return new ReferenceDataResult.Invalid("A code is required.");

        var limit = MaxCodeLength(command.Table);
        if (code.Length > limit)
        {
            return new ReferenceDataResult.Invalid(
                $"A code for '{command.Table}' may be at most {limit} characters.");
        }

        if (await ExistsAsync(command.Table, code, ct)) return new ReferenceDataResult.DuplicateCode();

        switch (command.Table)
        {
            case ReferenceTables.Categories:
                db.Add(new Category { Id = Guid.CreateVersion7(), Code = code, NameAr = command.NameAr, NameEn = command.NameEn });
                break;
            case ReferenceTables.Currencies:
                db.Add(new Currency { Id = Guid.CreateVersion7(), Code = code, NameAr = command.NameAr, NameEn = command.NameEn });
                break;
            case ReferenceTables.UnitsOfMeasure:
                db.Add(new UnitOfMeasure { Id = Guid.CreateVersion7(), Code = code, NameAr = command.NameAr, NameEn = command.NameEn });
                break;
            case ReferenceTables.Regions:
                db.Add(new Region { Id = Guid.CreateVersion7(), Code = code, NameAr = command.NameAr, NameEn = command.NameEn });
                break;
            case ReferenceTables.Incoterms:
                db.Add(new Incoterm { Id = Guid.CreateVersion7(), Code = code.ToUpperInvariant(), NameAr = command.NameAr, NameEn = command.NameEn });
                break;
            case ReferenceTables.DocumentTypes:
                db.Add(new DocumentType
                {
                    Id = Guid.CreateVersion7(), Code = code, NameAr = command.NameAr, NameEn = command.NameEn,
                    IsRequired = command.IsRequired ?? false,
                    ExpiryTracked = command.ExpiryTracked ?? false,
                    IsAwardCritical = command.IsAwardCritical ?? false,
                });
                break;
        }

        await auditLogger.LogAsync("ReferenceData", Guid.Empty, $"reference.{command.Table}.created",
            scope.UserId, referenceCode: code, ct: ct);
        await db.SaveChangesAsync(ct);

        return await ReadBackAsync(command.Table, code, ct);
    }

    public async Task<ReferenceDataResult> UpdateAsync(UpdateReferenceItemCommand command, CancellationToken ct)
    {
        if (!ReferenceTables.All.Contains(command.Table)) return new ReferenceDataResult.UnknownTable();

        var found = await ApplyAsync(command.Table, command.Code, item =>
        {
            switch (item)
            {
                case Category c: c.NameAr = command.NameAr; c.NameEn = command.NameEn; break;
                case Currency c: c.NameAr = command.NameAr; c.NameEn = command.NameEn; break;
                case UnitOfMeasure u: u.NameAr = command.NameAr; u.NameEn = command.NameEn; break;
                case Region r: r.NameAr = command.NameAr; r.NameEn = command.NameEn; break;
                case Incoterm i: i.NameAr = command.NameAr; i.NameEn = command.NameEn; break;
                case DocumentType d:
                    d.NameAr = command.NameAr;
                    d.NameEn = command.NameEn;
                    if (command.IsRequired is { } required) d.IsRequired = required;
                    if (command.ExpiryTracked is { } tracked) d.ExpiryTracked = tracked;
                    if (command.IsAwardCritical is { } awardCritical) d.IsAwardCritical = awardCritical;
                    break;
            }
        }, ct);

        if (!found) return new ReferenceDataResult.NotFound();

        await auditLogger.LogAsync("ReferenceData", Guid.Empty, $"reference.{command.Table}.updated",
            scope.UserId, referenceCode: command.Code, ct: ct);
        await db.SaveChangesAsync(ct);

        return await ReadBackAsync(command.Table, command.Code, ct);
    }

    public async Task<ReferenceDataResult> SetActiveAsync(SetReferenceItemActiveCommand command, CancellationToken ct)
    {
        if (!ReferenceTables.All.Contains(command.Table)) return new ReferenceDataResult.UnknownTable();

        var found = await ApplyAsync(command.Table, command.Code, item =>
        {
            switch (item)
            {
                case Category c: c.IsActive = command.IsActive; break;
                case Currency c: c.IsActive = command.IsActive; break;
                case UnitOfMeasure u: u.IsActive = command.IsActive; break;
                case Region r: r.IsActive = command.IsActive; break;
                case Incoterm i: i.IsActive = command.IsActive; break;
                case DocumentType d: d.IsActive = command.IsActive; break;
            }
        }, ct);

        if (!found) return new ReferenceDataResult.NotFound();

        await auditLogger.LogAsync("ReferenceData", Guid.Empty,
            command.IsActive ? $"reference.{command.Table}.reactivated" : $"reference.{command.Table}.deactivated",
            scope.UserId, referenceCode: command.Code, ct: ct);
        await db.SaveChangesAsync(ct);

        return await ReadBackAsync(command.Table, command.Code, ct);
    }

    private static int MaxCodeLength(string table) => table switch
    {
        ReferenceTables.Currencies or ReferenceTables.Incoterms => 3,
        _ => 50,
    };

    private Task<bool> ExistsAsync(string table, string code, CancellationToken ct) => table switch
    {
        ReferenceTables.Categories => db.Set<Category>().AnyAsync(c => c.Code == code, ct),
        ReferenceTables.Currencies => db.Set<Currency>().AnyAsync(c => c.Code == code, ct),
        ReferenceTables.UnitsOfMeasure => db.Set<UnitOfMeasure>().AnyAsync(u => u.Code == code, ct),
        ReferenceTables.Regions => db.Set<Region>().AnyAsync(r => r.Code == code, ct),
        ReferenceTables.Incoterms => db.Set<Incoterm>().AnyAsync(i => i.Code == code.ToUpperInvariant(), ct),
        ReferenceTables.DocumentTypes => db.Set<DocumentType>().AnyAsync(d => d.Code == code, ct),
        _ => throw new UnreachableTableException(table),
    };

    private async Task<bool> ApplyAsync(string table, string code, Action<object> mutate, CancellationToken ct)
    {
        object? item = table switch
        {
            ReferenceTables.Categories => await db.Set<Category>().FirstOrDefaultAsync(c => c.Code == code, ct),
            ReferenceTables.Currencies => await db.Set<Currency>().FirstOrDefaultAsync(c => c.Code == code, ct),
            ReferenceTables.UnitsOfMeasure => await db.Set<UnitOfMeasure>().FirstOrDefaultAsync(u => u.Code == code, ct),
            ReferenceTables.Regions => await db.Set<Region>().FirstOrDefaultAsync(r => r.Code == code, ct),
            ReferenceTables.Incoterms => await db.Set<Incoterm>().FirstOrDefaultAsync(i => i.Code == code, ct),
            ReferenceTables.DocumentTypes => await db.Set<DocumentType>().FirstOrDefaultAsync(d => d.Code == code, ct),
            _ => throw new UnreachableTableException(table),
        };

        if (item is null) return false;
        mutate(item);
        return true;
    }

    private async Task<ReferenceDataResult> ReadBackAsync(string table, string code, CancellationToken ct)
    {
        var items = await ListAsync(table, includeInactive: true, ct);
        var item = items?.FirstOrDefault(i => i.Code == code);
        return item is null ? new ReferenceDataResult.NotFound() : new ReferenceDataResult.Success(item);
    }
}

public sealed class UnreachableTableException(string table)
    : InvalidOperationException($"'{table}' is in ReferenceTables.All but no branch handles it.");
