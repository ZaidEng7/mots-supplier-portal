namespace MotsSupplierPortal.Application.ReferenceData;

public interface IReferenceDataAdminHandler
{
    Task<IReadOnlyList<ReferenceItemDto>?> ListAsync(string table, bool includeInactive, CancellationToken ct);
    Task<ReferenceDataResult> CreateAsync(CreateReferenceItemCommand command, CancellationToken ct);
    Task<ReferenceDataResult> UpdateAsync(UpdateReferenceItemCommand command, CancellationToken ct);
    Task<ReferenceDataResult> SetActiveAsync(SetReferenceItemActiveCommand command, CancellationToken ct);
}

public interface IGetDocumentTypeCategoriesHandler
{
    Task<IReadOnlyList<DocumentTypeCategoryLinksDto>> HandleAsync(CancellationToken ct);
}

public interface ISetDocumentTypeCategoriesHandler
{
    Task<SetDocumentTypeCategoriesResult> HandleAsync(SetDocumentTypeCategoriesCommand command, CancellationToken ct);
}
