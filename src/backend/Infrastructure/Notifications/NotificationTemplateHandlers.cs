// The administrator's rewording of notification copy, and the read the sender uses.
//
// A type with no stored row renders the shipped words, which is what makes this table safe to add to a running
// system: nothing changes until somebody changes it.
//
// An override replaces the four strings and nothing else. The record of which requirement asked for the
// notification stays with the shipped entry, because rewording it does not change that.
//
//
// THE TOKENS AN OVERRIDE MAY USE ARE THE SHIPPED COPY'S OWN
//
// A rewording is checked against the token set that type actually supplies, and an unknown token is refused with
// the list of offenders. So an override gains no capability the shipped copy lacks.
//
//
// THE LIST IS THE CATALOGUE, NOT THE ROWS
//
// The screen lists every type the system can send, including the ones nobody has reworded. A list built from the
// stored rows would be empty on every deployment that has not edited anything, which reads as "there is nothing
// to configure".
//
// Reverting a type that was never overridden is a success: the shipped copy is what the caller asked for and it
// is already in force.
//
//
// EVERY WRITE IS AUDITED, AND THE WORDS ARE NOT IN THE ROW
//
// These words are what a supplier is told about a rejection, a deadline or an award, so "who reworded the award
// notice" is a governance question.
//
// The structured change field is for a redacted field difference and these are four long bilingual strings. The
// action, the type and the actor are what the question needs, and the current words are readable from the table.

namespace MotsSupplierPortal.Infrastructure.Notifications;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class NotificationCopySource(AppDbContext db) : INotificationCopySource
{
    public async Task<NotificationCatalogue.Entry> ForAsync(string type, CancellationToken ct)
    {
        var shipped = NotificationCatalogue.For(type);

        var over = await db.Set<NotificationTemplate>().AsNoTracking()
            .Where(t => t.Type == type)
            .Select(t => new { t.TitleAr, t.TitleEn, t.BodyAr, t.BodyEn })
            .FirstOrDefaultAsync(ct);

        return over is null
            ? shipped
            : shipped with { TitleAr = over.TitleAr, TitleEn = over.TitleEn, BodyAr = over.BodyAr, BodyEn = over.BodyEn };
    }
}

public sealed class NotificationTemplateAdminHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : INotificationTemplateAdminHandler
{
    public async Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(CancellationToken ct)
    {
        var overrides = await db.Set<NotificationTemplate>().AsNoTracking().ToListAsync(ct);
        var byType = overrides.ToDictionary(t => t.Type, StringComparer.Ordinal);

        return
        [
            .. NotificationCatalogue.Types
                .OrderBy(type => type, StringComparer.Ordinal)
                .Select(type => ToDto(type, byType.GetValueOrDefault(type))),
        ];
    }

    public async Task<NotificationTemplateResult> UpdateAsync(UpdateNotificationTemplateCommand command, CancellationToken ct)
    {
        if (!NotificationCatalogue.Types.Contains(command.Type)) return new NotificationTemplateResult.UnknownType();

        var permitted = NotificationCatalogue.TokensFor(command.Type);
        var used = new[] { command.TitleAr, command.TitleEn, command.BodyAr, command.BodyEn }
            .SelectMany(NotificationCatalogue.TokensIn)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unknown = used.Where(token => !permitted.Contains(token)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0) return new NotificationTemplateResult.UnknownTokens(unknown);

        var existing = await db.Set<NotificationTemplate>().FirstOrDefaultAsync(t => t.Type == command.Type, ct);

        if (existing is null)
        {
            existing = new NotificationTemplate
            {
                Id = Guid.CreateVersion7(),
                Type = command.Type,
                TitleAr = command.TitleAr,
                TitleEn = command.TitleEn,
                BodyAr = command.BodyAr,
                BodyEn = command.BodyEn,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedByUserId = scope.UserId,
            };
            db.Add(existing);
        }
        else
        {
            existing.TitleAr = command.TitleAr;
            existing.TitleEn = command.TitleEn;
            existing.BodyAr = command.BodyAr;
            existing.BodyEn = command.BodyEn;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedByUserId = scope.UserId;
        }

        await auditLogger.LogAsync("NotificationTemplate", existing.Id, "notification.template.updated",
            scope.UserId, referenceCode: command.Type, ct: ct);
        await db.SaveChangesAsync(ct);

        return new NotificationTemplateResult.Success(ToDto(command.Type, existing));
    }

    public async Task<NotificationTemplateResult> RevertAsync(string type, CancellationToken ct)
    {
        if (!NotificationCatalogue.Types.Contains(type)) return new NotificationTemplateResult.UnknownType();

        var existing = await db.Set<NotificationTemplate>().FirstOrDefaultAsync(t => t.Type == type, ct);
        if (existing is not null)
        {
            db.Remove(existing);
            await auditLogger.LogAsync("NotificationTemplate", existing.Id, "notification.template.reverted",
                scope.UserId, referenceCode: type, ct: ct);
            await db.SaveChangesAsync(ct);
        }

        return new NotificationTemplateResult.Success(ToDto(type, null));
    }

    private static NotificationTemplateDto ToDto(string type, NotificationTemplate? over)
    {
        var shipped = NotificationCatalogue.For(type);

        return new NotificationTemplateDto(
            type,
            over?.TitleAr ?? shipped.TitleAr,
            over?.TitleEn ?? shipped.TitleEn,
            over?.BodyAr ?? shipped.BodyAr,
            over?.BodyEn ?? shipped.BodyEn,
            shipped.TitleAr, shipped.TitleEn, shipped.BodyAr, shipped.BodyEn,
            over is not null,
            over?.UpdatedAt,
            [.. NotificationCatalogue.TokensFor(type).Order(StringComparer.Ordinal)]);
    }
}
