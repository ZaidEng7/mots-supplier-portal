// What the staff-administration operations are called.
//
// The list is paged by cursor on the email address, the same shape the supplier's own team list uses.
//
// Resetting somebody's second factor clears the enrolment so the holder enrols again on their next sign-in,
// and kills every live session. A reset that left the old sessions alive would hand somebody who already has
// one a way to stay.

namespace MotsSupplierPortal.Application.Auth;

using MotsSupplierPortal.Application.Common;

public interface IInviteStaffHandler
{
    Task<InviteStaffResult> HandleAsync(InviteStaffCommand command, CancellationToken ct);
}

public interface IAcceptStaffInviteHandler
{
    Task<AcceptStaffInviteResult> HandleAsync(AcceptStaffInviteCommand command, CancellationToken ct);
}

public interface IListStaffHandler
{
    Task<ListEnvelope<StaffAccountDto>> HandleAsync(string? cursor, int? limit, bool withCount, CancellationToken ct);
}

public interface ISetStaffActiveHandler
{
    Task<StaffAccountResult> HandleAsync(Guid userId, bool isActive, CancellationToken ct);
}

public interface IChangeStaffRoleHandler
{
    Task<StaffAccountResult> HandleAsync(ChangeStaffRoleCommand command, CancellationToken ct);
}

public interface IResetStaffMfaHandler
{
    Task<StaffAccountResult> HandleAsync(Guid userId, CancellationToken ct);
}
