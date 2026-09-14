// The vocabulary for a supplier's own team: listing the people who can sign in for the company, inviting one,
// disabling one, and accepting an invitation.
//
// The list is scoped to the caller's own company and never crosses suppliers. It is paged by cursor on the
// email address, which is the order it displays in.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Application.Common;

public sealed record SupplierUserDto(Guid UserId, string Email, string FullName, bool IsActive);

public sealed record InviteSupplierUserCommand(string Email, string FullName);

public abstract record InviteSupplierUserResult
{
    public sealed record Success(SupplierUserDto User) : InviteSupplierUserResult;
    public sealed record NotFoundOrOutOfScope : InviteSupplierUserResult;
    public sealed record DuplicateEmail : InviteSupplierUserResult;
}

public interface IInviteSupplierUserHandler
{
    Task<InviteSupplierUserResult> HandleAsync(InviteSupplierUserCommand command, CancellationToken ct);
}

public interface IListSupplierUsersHandler
{
    Task<ListEnvelope<SupplierUserDto>> HandleAsync(string? cursor, int? limit, bool withCount, CancellationToken ct);
}

public sealed record DisableSupplierUserCommand(Guid UserId);

public abstract record DisableSupplierUserResult
{
    public sealed record Success : DisableSupplierUserResult;
    public sealed record NotFoundOrOutOfScope : DisableSupplierUserResult;
}

public interface IDisableSupplierUserHandler
{
    Task<DisableSupplierUserResult> HandleAsync(DisableSupplierUserCommand command, CancellationToken ct);
}

public sealed record AcceptSupplierUserInviteCommand(string Token, string Password);

public abstract record AcceptSupplierUserInviteResult
{
    public sealed record Success : AcceptSupplierUserInviteResult;
    public sealed record InvalidOrExpiredToken : AcceptSupplierUserInviteResult;
    public sealed record WeakPassword(IReadOnlyList<string> Errors) : AcceptSupplierUserInviteResult;
}

public interface IAcceptSupplierUserInviteHandler
{
    Task<AcceptSupplierUserInviteResult> HandleAsync(AcceptSupplierUserInviteCommand command, CancellationToken ct);
}
