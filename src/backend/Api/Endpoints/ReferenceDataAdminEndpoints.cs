using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record ReferenceItemRequest(
    string NameAr, string NameEn, bool? IsRequired, bool? ExpiryTracked);

public sealed class ReferenceItemRequestValidator : AbstractValidator<ReferenceItemRequest>
{
    public ReferenceItemRequestValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
    }
}

/// <summary>
/// T-034/T-059/FR-ADM-004: the admin surface for the five reference tables, which were seed-only - a
/// ministry could not add a document type without a deploy.
///
/// <para><b>The table is a path segment, validated against a list.</b> One route family rather than
/// five, because the operations are identical; an unrecognised table is a 404 rather than a silent
/// no-op, which is the same answer §6.3 gives an unknown filter value.</para>
///
/// <para><b>There is no DELETE.</b> Deactivation is the only removal - see DECISIONS-TAKEN.md D-28.
/// Every one of these tables is referenced BY CODE from live rows with no cascade, so deleting a
/// Category a published RFQ points at would leave that RFQ describing something that no longer
/// exists.</para>
/// </summary>
public static class ReferenceDataAdminEndpoints
{
    private static IResult Map(ReferenceDataResult result) => result switch
    {
        ReferenceDataResult.Success s => Results.Ok(s.Item),
        // An unknown table and an unknown code are both 404: the caller named something that is not
        // there, and which of the two it was is not a distinction worth a separate status.
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
                table, code, request.NameAr, request.NameEn, request.IsRequired, request.ExpiryTracked), ct));
        })
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithName("CreateReferenceItem");

        group.MapPut("/{code}", async (
            string table, string code, ReferenceItemRequest request,
            IValidator<ReferenceItemRequest> validator,
            IReferenceDataAdminHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return Map(await handler.UpdateAsync(new UpdateReferenceItemCommand(
                table, code, request.NameAr, request.NameEn, request.IsRequired, request.ExpiryTracked), ct));
        })
        .RequirePermission(Permissions.ReferenceDataManage)
        .WithName("UpdateReferenceItem");

        // Named sub-resources rather than a PATCH of IsActive: §3's own convention for a state change,
        // and it makes the audit action unambiguous at the route.
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
