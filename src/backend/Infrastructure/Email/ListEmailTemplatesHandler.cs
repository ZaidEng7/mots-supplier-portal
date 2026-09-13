using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Email;

public sealed class ListEmailTemplatesHandler(AppDbContext db) : IListEmailTemplatesHandler
{
    public async Task<IReadOnlyList<EmailTemplateRowDto>> HandleAsync(CancellationToken ct)
    {
        var overrides = await db.EmailTemplateOverrides.AsNoTracking()
            .ToDictionaryAsync(o => o.Key, o => new EmailTemplateOverrideDto(
                o.Key, o.SubjectAr, o.SubjectEn, o.BodyAr, o.BodyEn, o.UpdatedAt), ct);

        // Driven by the CATALOGUE, not by the override table, so a template nobody has touched still appears
        // with its shipped words - otherwise this screen would start empty and an administrator would have no
        // way to discover which emails exist.
        return EmailTemplateKeys.All
            .Select(definition => new EmailTemplateRowDto(
                definition.Key,
                definition.RequiredTokens,
                definition.OptionalTokens,
                overrides.GetValueOrDefault(definition.Key),
                EmailTemplateCatalogue.ShippedFor(definition.Key)))
            .ToList();
    }
}
