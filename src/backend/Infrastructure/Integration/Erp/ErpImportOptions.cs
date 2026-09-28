// The password every imported supplier account is created with.
//
// ONE SHARED PASSWORD IS A DELIBERATE CHOICE FOR A TEST AND A BAD ONE FOR PRODUCTION, and this comment is the
// record of that rather than an objection to it. Anyone who learns this value can sign in as ANY imported
// supplier and read what their competitors bid, which is the single most commercially sensitive thing this
// product holds. That is acceptable while the suppliers are three rows on a sandbox and nobody real is affected.
// Before the real registry is imported, this should become a per-account password nobody shares - the invitation
// path already exists, SendSupplierUserInviteEmailAsync, and is what this replaced.
//
// THERE IS NO FORCE-CHANGE-ON-FIRST-SIGN-IN IN THIS PRODUCT. Adding one is the obvious mitigation and it is not
// a line of configuration, so it is named here as absent rather than assumed.
//
// IT IS NOT IN THE REPOSITORY. user-secrets locally, the deployment's own store beyond that. A password that
// opens every supplier account is a credential, whatever it was chosen for.
//
// IT MUST SURVIVE THE PRODUCT'S OWN PASSWORD RULES, which are twelve characters and a check against known public
// breaches. A short or well-known value is refused by the identity framework at account creation - which would
// otherwise be discovered eighty accounts into a run, so the import checks it once before it writes anything.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpImportOptions
{
    public const string SectionName = "ErpImport";

    public string? InitialPassword { get; init; }
}
