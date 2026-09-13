// Adding, editing and removing the lines a tender is asking to buy.
//
// The category and the unit of measure are checked against reference data by code rather than by a database
// foreign key, which is the same convention the supplier catalogue uses.
//
// The edit path repeats those checks. A correction can change either one, and a correction into a code that
// does not exist is the same defect as a creation into one.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

public sealed class ManageRfqItemHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageRfqItemHandler
{
    public async Task<RfqMutationResult> AddAsync(AddRfqItemCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        if (!await db.Categories.AnyAsync(c => c.Code == command.CategoryCode, ct))
        {
            return new RfqMutationResult.InvalidCategory();
        }
        if (!await db.UnitsOfMeasure.AnyAsync(u => u.Code == command.UnitOfMeasureCode, ct))
        {
            return new RfqMutationResult.InvalidUnitOfMeasure();
        }

        RfqItem item;
        try
        {
            item = rfq.AddItem(
                command.TitleAr, command.TitleEn, command.SpecificationAr, command.SpecificationEn,
                command.CategoryCode, command.Quantity, command.UnitOfMeasureCode, command.IsUnitPrice, command.IsOptional);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        db.RfqItems.Add(item);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_item_added", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }

    public async Task<RfqMutationResult> UpdateAsync(UpdateRfqItemCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        if (!await db.Categories.AnyAsync(c => c.Code == command.CategoryCode, ct))
        {
            return new RfqMutationResult.InvalidCategory();
        }
        if (!await db.UnitsOfMeasure.AnyAsync(u => u.Code == command.UnitOfMeasureCode, ct))
        {
            return new RfqMutationResult.InvalidUnitOfMeasure();
        }

        try
        {
            rfq.UpdateItem(
                command.ItemId, command.TitleAr, command.TitleEn, command.SpecificationAr, command.SpecificationEn,
                command.CategoryCode, command.Quantity, command.UnitOfMeasureCode, command.IsUnitPrice, command.IsOptional);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_item_updated", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }

    public async Task<RfqMutationResult> RemoveAsync(RemoveRfqItemCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.RemoveItem(command.ItemId);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_item_removed", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
