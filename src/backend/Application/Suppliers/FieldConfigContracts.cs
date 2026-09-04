namespace MotsSupplierPortal.Application.Suppliers;

public sealed record FieldConfigDto(string Category, string FieldCode, bool IsEnabled);

/// <summary>
/// The single-row shape. Separate from FieldConfigDto rather than a nullable field on it, for two
/// reasons: an ETag over a collection would be an ETag for a different resource than the one a PUT
/// addresses, so the list must not carry a version at all; and the ETag filter matches
/// <c>RowVersion</c> only when it is <c>uint</c> or <c>long</c>, so a <c>uint?</c> would have been
/// silently ignored and the guarded PUT would have had no obtainable precondition. That failure is
/// invisible in a build and shows up as a 428 nobody can satisfy.
/// </summary>
public sealed record FieldConfigDetailDto(string Category, string FieldCode, bool IsEnabled, uint RowVersion);

public interface IGetOneFieldConfigHandler
{
    /// <summary>The single-item read T-029's guard needs. §8.1's contract is a response carrying an
    /// ETag the caller sends back as If-Match; this aggregate had only a list, so requiring the
    /// header on the PUT without adding this read would have refused every caller - the batch-3
    /// Offering lesson, applied before shipping rather than after.</summary>
    Task<FieldConfigDetailDto?> HandleAsync(string category, string fieldCode, CancellationToken ct);
}

public interface IGetFieldConfigHandler
{
    /// <summary>category is optional - omit to list every configured field across all categories.</summary>
    Task<IReadOnlyList<FieldConfigDto>> HandleAsync(string? category, CancellationToken ct);
}

public abstract record UpdateFieldConfigResult
{
    public sealed record Success(FieldConfigDetailDto Config) : UpdateFieldConfigResult;
    public sealed record NotFound : UpdateFieldConfigResult;
}

public interface IUpdateFieldConfigHandler
{
    Task<UpdateFieldConfigResult> HandleAsync(string category, string fieldCode, bool isEnabled, CancellationToken ct);
}
