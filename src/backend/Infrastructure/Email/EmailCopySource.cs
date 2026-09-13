// What the send path uses to get the words: the administrator's override if there is one, the shipped copy
// otherwise.
//
// The shipped copy is passed in as a lambda rather than looked up. The caller already has the typed arguments in
// hand, so this cannot render the fallback with the wrong ones, and the fallback is only evaluated when no
// override exists, which is the normal case on every deployment that has not touched a template.
//
// A failure reading the table falls back to the shipped copy rather than failing the send. An email that goes out
// in the shipped wording is a working system; one that does not go out because a database read failed is a
// supplier who never learns their tender closed.

namespace MotsSupplierPortal.Infrastructure.Email;

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class EmailCopySource(AppDbContext db) : IEmailCopySource
{
    public async Task<(string Subject, string Body)> ComposeAsync(
        string key,
        string? locale,
        IReadOnlyDictionary<string, string> tokens,
        Func<(string Subject, string Body)> shipped,
        CancellationToken ct)
    {
        var isEnglish = locale == "en";

        var over = await db.EmailTemplateOverrides.AsNoTracking()
            .Where(o => o.Key == key)
            .Select(o => new { o.SubjectAr, o.SubjectEn, o.BodyAr, o.BodyEn })
            .FirstOrDefaultAsync(ct);

        if (over is null) return shipped();

        return (
            EmailTemplateCatalogue.Interpolate(isEnglish ? over.SubjectEn : over.SubjectAr, tokens),
            EmailTemplateCatalogue.Interpolate(isEnglish ? over.BodyEn : over.BodyAr, tokens));
    }
}
