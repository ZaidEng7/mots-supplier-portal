// The vocabulary for the administrator's editable field configuration.
//
//
// WHY THE SINGLE-ROW SHAPE IS SEPARATE
//
// Two reasons, and both are about the write precondition.
//
// A version over a collection would be a version for a different resource than the one an update addresses,
// so the list must not carry one at all.
//
// And the filter that emits a version only recognises it as a plain number. An optional one would have been
// silently ignored, leaving the guarded update with no obtainable precondition.
//
// That failure is invisible in a build and shows up as a refusal nobody can satisfy.

namespace MotsSupplierPortal.Application.Suppliers;

public sealed record FieldConfigDto(string Category, string FieldCode, bool IsEnabled);

public sealed record FieldConfigDetailDto(string Category, string FieldCode, bool IsEnabled, uint RowVersion);

public interface IGetOneFieldConfigHandler
{
    Task<FieldConfigDetailDto?> HandleAsync(string category, string fieldCode, CancellationToken ct);
}

public interface IGetFieldConfigHandler
{
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
