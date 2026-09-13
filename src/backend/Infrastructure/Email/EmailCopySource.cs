using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Email;

/// <summary>
/// What the send path uses.
///
/// <para><b>The shipped copy is passed in as a lambda, not looked up.</b> The caller already has the typed
/// arguments in hand, so this cannot render the fallback with the wrong ones - and the fallback is only
/// evaluated when no override exists, which is the normal case on every deployment that has not touched a
/// template.</para>
///
/// <para><b>A failure here falls back to the shipped copy rather than failing the send.</b> An email that
/// goes out in the shipped wording is a working system; one that does not go out because a database read
/// failed is a supplier who never learns their tender closed.</para>
/// </summary>
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
