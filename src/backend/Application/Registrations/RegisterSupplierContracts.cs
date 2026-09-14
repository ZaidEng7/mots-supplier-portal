// The vocabulary for a supplier registering itself.
//
// Locale is the language the registrant actually saw the form in. The endpoint reads it from the
// request's language header, and that is a faithful signal rather than a guess, because the interface
// has no language switcher of its own and follows the browser.
//
// Three of the four outcomes are deliberately indistinguishable to the caller. Success, an email
// already registered, and a registration number already registered all map to the same response, so
// nobody can use the form to discover who is already on the portal. The distinction survives inside
// the system, because the existing account is notified.
//
// A weak password is the one refusal that is told plainly, because it is a fact about the password
// somebody just typed. It is true or false for any email address, including ones that will never
// exist, so it reveals nothing about the target.

namespace MotsSupplierPortal.Application.Registrations;

public sealed record RegisterSupplierCommand(
    string DisplayNameAr,
    string DisplayNameEn,
    string? RegistrationNumber,
    string RepresentativeName,
    string RepresentativePhone,
    string Email,
    string Password,
    string Locale);

public abstract record RegisterSupplierResult
{
    public sealed record Success(string SupplierReferenceCode) : RegisterSupplierResult;

    public sealed record DuplicateEmail : RegisterSupplierResult;

    public sealed record DuplicateRegistrationNumber : RegisterSupplierResult;
    public sealed record WeakPassword(IReadOnlyList<string> Errors) : RegisterSupplierResult;
}

public interface IRegisterSupplierHandler
{
    Task<RegisterSupplierResult> HandleAsync(RegisterSupplierCommand command, CancellationToken ct);
}
