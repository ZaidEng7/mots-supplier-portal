using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Auth;

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
    /// <summary>Keyset-paged on (email, id), the same shape as the supplier-user list - MSP-84's
    /// reasoning applies identically.</summary>
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
    /// <summary>Clears the authenticator enrolment so the holder re-enrols on next sign-in, and kills
    /// every live session - a reset that left the old sessions alive would hand an attacker who already
    /// has one a way to stay.</summary>
    Task<StaffAccountResult> HandleAsync(Guid userId, CancellationToken ct);
}
