// Creating or replacing an administrator's rewording of one email, and checking its tokens.
//
//
// CHECKED WHEN IT IS WRITTEN, NOT WHEN IT IS SENT
//
// A send-time check has nowhere to go: the job is already running, the recipient is waiting, and the only options
// are to send a broken email or to send nothing.
//
// At write time there is a person on a screen who can fix it.
//
// Both language bodies must keep every required token, checked and reported per language. "A token is missing" is
// not actionable; "the Arabic body no longer contains the link" is.
//
// A token outside the declared set is refused too, because it reaches the recipient as literal characters
// mid-sentence and cannot be diagnosed from the sent mail.
//
// The pattern that finds tokens is deliberately permissive: the point is to catch a token the payload cannot
// fill, so it has to find the ones nobody declared, including typos of real ones. It carries a timeout because it
// runs over operator-supplied text; this pattern cannot backtrack catastrophically, but the input is untrusted and
// the bound costs nothing.

namespace MotsSupplierPortal.Infrastructure.Email;

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class UpsertEmailTemplateHandler(AppDbContext db) : IUpsertEmailTemplateHandler
{
    private static readonly Regex TokenPattern =
        new(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public async Task<UpsertEmailTemplateResult> HandleAsync(UpsertEmailTemplateCommand command, CancellationToken ct)
    {
        var definition = EmailTemplateKeys.Find(command.Key);
        if (definition is null || !EmailTemplateCatalogue.Knows(command.Key))
        {
            return new UpsertEmailTemplateResult.UnknownKey();
        }

        var missing = new List<string>();
        foreach (var token in definition.RequiredTokens)
        {
            var placeholder = EmailTemplateCatalogue.Placeholder(token);
            if (!command.BodyAr.Contains(placeholder, StringComparison.Ordinal)) missing.Add($"ar:{token}");
            if (!command.BodyEn.Contains(placeholder, StringComparison.Ordinal)) missing.Add($"en:{token}");
        }
        if (missing.Count > 0) return new UpsertEmailTemplateResult.MissingRequiredTokens(missing);

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
