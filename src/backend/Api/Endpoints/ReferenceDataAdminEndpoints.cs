// The administrator's surface for the five reference tables: categories, document types, currencies, units and
// delivery terms. They used to be seed-only, so a ministry could not add a document type without a
// deployment.
//
// The table is a path segment checked against a list, so there is one family of routes rather than five,
// because the operations are identical. An unrecognised table answers not-found rather than quietly doing
// nothing, which is the same answer an unknown filter value gets.
//
// An unknown table and an unknown code both answer not-found. The caller named something that is not there,
// and which of the two it was is not a distinction worth a separate status.
//
// There is no delete. Deactivating is the only removal. Every one of these tables is referenced by code from
// live rows with nothing cascading, so deleting a category that a published tender points at would leave that
// tender describing something which no longer exists.
//
// Activation and deactivation are named sub-routes rather than a partial update of a flag. That is this API's
// own convention for a state change, and it makes the audit entry unambiguous from the route alone.
//
// The category links on a document type are a separate route because they apply to document types only.
// Recording a link narrows a document type to the categories named, and a type with no links stays required of
// everyone. A link to a category that does not exist is refused with the codes named, because it would be a
// requirement no supplier could ever match, and it would stay invisible until the narrowing was switched on,
// at which point it silently excludes a document from everybody.
//
// The required-for-these-categories flag is left alone when the caller sends nothing for it. On every table
// but document types there is no such flag at all, and "this table has no such flag" and "this row has it
// switched off" are different facts, so the handler keeps the existing value rather than letting somebody
// editing a name silently clear it.

namespace MotsSupplierPortal.Api.Endpoints;

using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.Identity;

public sealed record SetDocumentTypeCategoriesRequest(IReadOnlyList<string>? CategoryCodes);

public sealed record ReferenceItemRequest(
    string NameAr, string NameEn, bool? IsRequired, bool? ExpiryTracked,
    bool? IsAwardCritical = null);

public sealed class ReferenceItemRequestValidator : AbstractValidator<ReferenceItemRequest>
{
    public ReferenceItemRequestValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
    }
}

public static class ReferenceDataAdminEndpoints
{
    private static IResult Map(ReferenceDataResult result) => result switch
    {
        ReferenceDataResult.Success s => Results.Ok(s.Item),
        ReferenceDataResult.UnknownTable => Results.NotFound(),
        ReferenceDataResult.NotFound => Results.NotFound(),
        ReferenceDataResult.DuplicateCode => Results.Conflict(new { error = "duplicate_resource" }),
        ReferenceDataResult.Invalid invalid =>
            Results.UnprocessableEntity(new { error = "invalid_reference_item", message = invalid.Message }),
        _ => Results.Problem(),
    };

    public static void MapReferenceDataAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/reference/{table}").WithTags("Admin");

        group.MapGet("/", async (
            string table, bool? includeInactive,
            IReferenceDataAdminHandler handler, CancellationToken ct) =>
        {
            var items = await handler.ListAsync(table, includeInactive ?? false, ct);
            return items is null ? Results.NotFound() : Results.Ok(items);
        })
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithName("ListReferenceItems");

        group.MapPost("/{code}", async (
            string table, string code, ReferenceItemRequest request,
            IValidator<ReferenceItemRequest> validator,
            IReferenceDataAdminHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return Map(await handler.CreateAsync(new CreateReferenceItemCommand(
                table, code, request.NameAr, request.NameEn, request.IsRequired, request.ExpiryTracked, request.IsAwardCritical), ct));
        })
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithName("CreateReferenceItem");

        app.MapGet("/api/v1/admin/document-type-categories", async (
            IGetDocumentTypeCategoriesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithTags("Admin")
        .WithName("GetDocumentTypeCategories");

        app.MapPut("/api/v1/admin/document-type-categories/{documentTypeCode}", async (
            string documentTypeCode,
            SetDocumentTypeCategoriesRequest request,
            ISetDocumentTypeCategoriesHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new SetDocumentTypeCategoriesCommand(documentTypeCode, request.CategoryCodes ?? []), ct);

            return result switch
            {
                SetDocumentTypeCategoriesResult.Success s => Results.Ok(s.Links),
                SetDocumentTypeCategoriesResult.UnknownDocumentType => Results.NotFound(),
                SetDocumentTypeCategoriesResult.UnknownCategories u =>
                    UnknownReferenceCodesResult.For("categoryCodes", u.Codes),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithTags("Admin")
        .WithName("SetDocumentTypeCategories");

        group.MapPut("/{code}", async (
            string table, string code, ReferenceItemRequest request,
            IValidator<ReferenceItemRequest> validator,
            IReferenceDataAdminHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return Map(await handler.UpdateAsync(new UpdateReferenceItemCommand(
                table, code, request.NameAr, request.NameEn, request.IsRequired, request.ExpiryTracked, request.IsAwardCritical), ct));
        })
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithName("UpdateReferenceItem");

        group.MapPost("/{code}/deactivate", async (
            string table, string code, IReferenceDataAdminHandler handler, CancellationToken ct) =>
            Map(await handler.SetActiveAsync(new SetReferenceItemActiveCommand(table, code, false), ct)))
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithName("DeactivateReferenceItem");

        group.MapPost("/{code}/reactivate", async (
            string table, string code, IReferenceDataAdminHandler handler, CancellationToken ct) =>
            Map(await handler.SetActiveAsync(new SetReferenceItemActiveCommand(table, code, true), ct)))
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithName("ReactivateReferenceItem");
    }
}
