namespace MotsSupplierPortal.Application.Auth;

public sealed record StaffDto(Guid UserId, string Email, string FullName, string Role);

public sealed record InviteStaffCommand(string Email, string FullName, string Role);

public abstract record InviteStaffResult
{
    public sealed record Success(StaffDto Staff) : InviteStaffResult;
    public sealed record DuplicateEmail : InviteStaffResult;
    /// <summary>Role is not one of the invitable staff roles - see InviteStaffHandler for the
    /// list (supplier_admin/supplier_user are deliberately excluded; those come from supplier
    /// registration/the supplier-side invite, not this endpoint).</summary>
    public sealed record InvalidRole : InviteStaffResult;
}

public interface IInviteStaffHandler
{
    Task<InviteStaffResult> HandleAsync(InviteStaffCommand command, CancellationToken ct);
}

public sealed record AcceptStaffInviteCommand(string Token, string Password);

public abstract record AcceptStaffInviteResult
{
    public sealed record Success : AcceptStaffInviteResult;
    public sealed record InvalidOrExpiredToken : AcceptStaffInviteResult;
    public sealed record WeakPassword(IReadOnlyList<string> Errors) : AcceptStaffInviteResult;
}

public interface IAcceptStaffInviteHandler
{
    Task<AcceptStaffInviteResult> HandleAsync(AcceptStaffInviteCommand command, CancellationToken ct);
}
