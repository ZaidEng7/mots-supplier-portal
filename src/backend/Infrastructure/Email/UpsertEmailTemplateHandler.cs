using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Email;

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
    /// <remarks>Given a timeout because this pattern runs over operator-supplied template bodies. This one
    /// cannot backtrack catastrophically, but the input is untrusted and the bound costs nothing.</remarks>
    private static readonly Regex TokenPattern =
        new(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

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
