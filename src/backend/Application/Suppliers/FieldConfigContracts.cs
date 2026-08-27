namespace MotsSupplierPortal.Application.Suppliers;

public sealed record FieldConfigDto(string Category, string FieldCode, bool IsEnabled);

public interface IGetFieldConfigHandler
{
    /// <summary>category is optional - omit to list every configured field across all categories.</summary>
    Task<IReadOnlyList<FieldConfigDto>> HandleAsync(string? category, CancellationToken ct);
}

public abstract record UpdateFieldConfigResult
{
    public sealed record Success(FieldConfigDto Config) : UpdateFieldConfigResult;
    public sealed record NotFound : UpdateFieldConfigResult;
}

public interface IUpdateFieldConfigHandler
{
    Task<UpdateFieldConfigResult> HandleAsync(string category, string fieldCode, bool isEnabled, CancellationToken ct);
}
