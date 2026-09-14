// Sending one email.
//
// The interface exists so the durable job queue can be built and tested with delivery stubbed, before a
// real transport is wired up.
//
// The user's identifier is a parameter alongside the address so that an implementation can identify the
// send event in a log without writing the address itself. Personal data is forbidden in logs, and an email
// address is exactly that.
//
// The address is still needed, because a real transport cannot send a message without it. The requirement
// is that whatever a sender logs about the event uses the identifier rather than the address.

namespace MotsSupplierPortal.Application.Common;

public interface IEmailSender
{
    Task SendAsync(Guid userId, string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
