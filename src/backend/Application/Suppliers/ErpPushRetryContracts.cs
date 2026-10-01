// A system administrator starting again the push of a supplier that could not be created in the ERP.
//
// A push fails for good when the ERP refused something it will refuse again, or after eight attempts, and then it
// waits for a person: the job never picks up a failed push by itself. Retrying moves it back to where it stopped -
// Requested, or Linked when the ERP already has the Supplier record - and the push runs again at once.
//
// Out of reach and absent are one answer, as for the other supplier routes. A push that is not failed is a typed
// refusal carrying the domain's own sentence, so a retry pressed twice, or on a push the job is still working on, says
// why nothing changed.

namespace MotsSupplierPortal.Application.Suppliers;

public abstract record RetryErpPushResult
{
    public sealed record Success(ErpSyncDto ErpSync) : RetryErpPushResult;

    public sealed record NotFound : RetryErpPushResult;

    public sealed record Invalid(string Message) : RetryErpPushResult;
}

public interface IRetryErpPushHandler
{
    Task<RetryErpPushResult> HandleAsync(string referenceCode, CancellationToken ct);
}
