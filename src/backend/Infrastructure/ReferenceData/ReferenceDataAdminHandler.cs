using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.ReferenceData;

/// <summary>
/// T-034/T-059: one handler for all five reference tables - see IReferenceDataAdminHandler on why
/// this is not five handlers.
///
/// <para><b>Deactivate, never delete (D-28).</b> There is no delete operation, on purpose. Every one
/// of these tables is referenced BY CODE from live rows - <c>RfqItem.CategoryCode</c>,
/// <c>Offering.UnitOfMeasureCode</c>, <c>SupplierDocument</c>'s type - with no cascade and no
/// nullable fallback. Deleting a Category a published RFQ points at would leave that RFQ describing a
/// category that no longer exists; deactivating it hides the code from new selections and leaves every
/// existing row intact and readable.</para>
///
/// <para><b>Every write is audited.</b> Reference data decides what suppliers may register against and
/// which documents they must produce, so "who added this document type, and when" is a governance
/// question, not a debugging one.</para>
/// </summary>
public sealed class ReferenceDataAdminHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IReferenceDataAdminHandler
{
    public async Task<IReadOnlyList<ReferenceItemDto>?> ListAsync(string table, bool includeInactive, CancellationToken ct)
    {
        if (!ReferenceTables.All.Contains(table)) return null;

        // Inactive rows are HIDDEN by default and reachable by asking. An admin editing the catalogue
        // needs to see what they deactivated - otherwise deactivation looks like deletion and the next
        // administrator re-creates the code, which is the one thing D-28's no-delete rule exists to
        // avoid.
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
            _ => await Project(db.Set<DocumentType>(), includeInactive,
                d => new ReferenceItemDto(d.Code, d.NameAr, d.NameEn, d.IsActive, d.IsRequired, d.ExpiryTracked),
                d => d.IsActive, ct),
        };
    }

    /// <summary>
    /// Filters in SQL, then projects and orders IN MEMORY.
    ///
    /// <para>Deliberate, not laziness: projecting to a record with optional constructor parameters and
    /// then ordering by a property of that projection is exactly the kind of expression that either
    /// translates or throws depending on the provider version, and the first version of this method
    /// answered 500 on every list. These are lookup tables of tens of rows, so the round trip is the
    /// same either way and this form cannot fail to translate.</para>
    /// </summary>
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

        // Per-table, because the columns genuinely differ: Currency.Code is 3 (ISO) while the others
        // are 50. Checked here rather than left to the database - a too-long code was answering 500
        // from a Postgres 22001, which tells an administrator nothing about what to fix.
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
            default:
                // A new DocumentType defaults to NOT required and NOT expiry-tracked when the caller
                // says nothing. Required-by-default would retroactively make every existing supplier's
                // profile incomplete the moment the row is created, which is a live consequence for
                // people who did nothing.
                db.Add(new DocumentType
                {
                    Id = Guid.CreateVersion7(), Code = code, NameAr = command.NameAr, NameEn = command.NameEn,
                    IsRequired = command.IsRequired ?? false,
                    ExpiryTracked = command.ExpiryTracked ?? false,
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
                case DocumentType d:
                    d.NameAr = command.NameAr;
                    d.NameEn = command.NameEn;
                    // Omitted means unchanged, not false. A caller editing an Arabic typo must not
                    // silently un-require a document type.
                    if (command.IsRequired is { } required) d.IsRequired = required;
                    if (command.ExpiryTracked is { } tracked) d.ExpiryTracked = tracked;
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
                case DocumentType d: d.IsActive = command.IsActive; break;
            }
        }, ct);

        if (!found) return new ReferenceDataResult.NotFound();

        // Reactivation is the same operation with the flag the other way, so it gets a distinguishable
        // action rather than sharing one - an audit reader asking "when did this stop being offered"
        // should not have to read a boolean out of the row's history.
        await auditLogger.LogAsync("ReferenceData", Guid.Empty,
            command.IsActive ? $"reference.{command.Table}.reactivated" : $"reference.{command.Table}.deactivated",
            scope.UserId, referenceCode: command.Code, ct: ct);
        await db.SaveChangesAsync(ct);

        return await ReadBackAsync(command.Table, command.Code, ct);
    }

    /// <summary>The column's own bound, so a refusal names the limit instead of surfacing a Postgres
    /// string-too-long as a 500.</summary>
    private static int MaxCodeLength(string table) =>
        table == ReferenceTables.Currencies ? 3 : 50;

    private Task<bool> ExistsAsync(string table, string code, CancellationToken ct) => table switch
    {
        ReferenceTables.Categories => db.Set<Category>().AnyAsync(c => c.Code == code, ct),
        ReferenceTables.Currencies => db.Set<Currency>().AnyAsync(c => c.Code == code, ct),
        ReferenceTables.UnitsOfMeasure => db.Set<UnitOfMeasure>().AnyAsync(u => u.Code == code, ct),
        ReferenceTables.Regions => db.Set<Region>().AnyAsync(r => r.Code == code, ct),
        _ => db.Set<DocumentType>().AnyAsync(d => d.Code == code, ct),
    };

    /// <summary>Loads the row by code and hands it to <paramref name="mutate"/>. False when there is
    /// no such code, which the callers turn into a 404.</summary>
    private async Task<bool> ApplyAsync(string table, string code, Action<object> mutate, CancellationToken ct)
    {
        object? item = table switch
        {
            ReferenceTables.Categories => await db.Set<Category>().FirstOrDefaultAsync(c => c.Code == code, ct),
            ReferenceTables.Currencies => await db.Set<Currency>().FirstOrDefaultAsync(c => c.Code == code, ct),
            ReferenceTables.UnitsOfMeasure => await db.Set<UnitOfMeasure>().FirstOrDefaultAsync(u => u.Code == code, ct),
            ReferenceTables.Regions => await db.Set<Region>().FirstOrDefaultAsync(r => r.Code == code, ct),
            _ => await db.Set<DocumentType>().FirstOrDefaultAsync(d => d.Code == code, ct),
        };

        if (item is null) return false;
        mutate(item);
        return true;
    }

    private async Task<ReferenceDataResult> ReadBackAsync(string table, string code, CancellationToken ct)
    {
        // Read back through the LIST projection, including inactive rows, so the response is the same
        // shape the list returns rather than a second hand-built one that could drift from it.
        var items = await ListAsync(table, includeInactive: true, ct);
        var item = items?.FirstOrDefault(i => i.Code == code);
        return item is null ? new ReferenceDataResult.NotFound() : new ReferenceDataResult.Success(item);
    }
}
