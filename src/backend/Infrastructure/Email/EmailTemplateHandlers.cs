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

/// <summary>
/// T-076's substance: the token contract, checked before the row is written.
///
/// <para>Checked at WRITE time rather than at send time deliberately. A send-time check has nowhere to go -
/// the job is already running, the recipient is waiting, and the only options are to send a broken email or
/// to send nothing. At write time there is a person on a screen who can fix it.</para>
/// </summary>
public sealed class UpsertEmailTemplateHandler(AppDbContext db) : IUpsertEmailTemplateHandler
{
    /// <summary>Any <c>{word}</c>. Deliberately permissive: the point is to catch a token the payload cannot
    /// fill, so it has to find the ones nobody declared, including typos of real ones.</summary>
    private static readonly Regex TokenPattern = new(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    public async Task<UpsertEmailTemplateResult> HandleAsync(UpsertEmailTemplateCommand command, CancellationToken ct)
    {
        var definition = EmailTemplateKeys.Find(command.Key);
        if (definition is null || !EmailTemplateCatalogue.Knows(command.Key))
        {
            return new UpsertEmailTemplateResult.UnknownKey();
        }

        // Both bodies must keep every required token. Checked per locale and reported per locale: "a token is
        // missing" is not actionable, and "the Arabic body no longer contains {verifyUrl}" is.
        var missing = new List<string>();
        foreach (var token in definition.RequiredTokens)
        {
            var placeholder = EmailTemplateCatalogue.Placeholder(token);
            if (!command.BodyAr.Contains(placeholder, StringComparison.Ordinal)) missing.Add($"ar:{token}");
            if (!command.BodyEn.Contains(placeholder, StringComparison.Ordinal)) missing.Add($"en:{token}");
        }
        if (missing.Count > 0) return new UpsertEmailTemplateResult.MissingRequiredTokens(missing);

        // D-34, applied to email: a token outside the declared set reaches the recipient as literal
        // characters mid-sentence and cannot be diagnosed from the sent mail.
        var allowed = definition.RequiredTokens.Concat(definition.OptionalTokens).ToHashSet(StringComparer.Ordinal);
        var unknown = new[] { command.SubjectAr, command.SubjectEn, command.BodyAr, command.BodyEn }
            .SelectMany(text => TokenPattern.Matches(text).Select(m => m.Groups[1].Value))
            .Where(token => !allowed.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (unknown.Count > 0) return new UpsertEmailTemplateResult.UnknownTokens(unknown);

        var existing = await db.EmailTemplateOverrides.FirstOrDefaultAsync(o => o.Key == command.Key, ct);
        if (existing is null)
        {
            existing = new EmailTemplateOverride
            {
                Id = Guid.CreateVersion7(),
                Key = command.Key,
                SubjectAr = command.SubjectAr,
                SubjectEn = command.SubjectEn,
                BodyAr = command.BodyAr,
                BodyEn = command.BodyEn,
            };
            db.EmailTemplateOverrides.Add(existing);
        }
        else
        {
            existing.SubjectAr = command.SubjectAr;
            existing.SubjectEn = command.SubjectEn;
            existing.BodyAr = command.BodyAr;
            existing.BodyEn = command.BodyEn;
        }

        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedByUserId = command.ActorUserId;
        await db.SaveChangesAsync(ct);

        return new UpsertEmailTemplateResult.Success(new EmailTemplateOverrideDto(
            existing.Key, existing.SubjectAr, existing.SubjectEn, existing.BodyAr, existing.BodyEn, existing.UpdatedAt));
    }
}

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
