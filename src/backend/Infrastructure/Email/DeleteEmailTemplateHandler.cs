// Removing an administrator's rewording of one email, which restores the shipped wording.

namespace MotsSupplierPortal.Infrastructure.Email;

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class DeleteEmailTemplateHandler(AppDbContext db) : IDeleteEmailTemplateHandler
{
    public async Task<bool> HandleAsync(string key, CancellationToken ct)
    {
        var existing = await db.EmailTemplateOverrides.FirstOrDefaultAsync(o => o.Key == key, ct);
        if (existing is null) return false;

        db.EmailTemplateOverrides.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
