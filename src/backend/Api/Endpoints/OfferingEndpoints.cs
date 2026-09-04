using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record CreateOfferingRequest(string NameAr, string NameEn, string? Description, string CategoryCode, string UnitOfMeasureCode, decimal? PriceAmount, string? CurrencyCode, IReadOnlyDictionary<string, string>? Attributes);

/// <summary>Reused for both create and update - PUT /{offeringId} takes the same shape, same
/// rules (see UpdateOffering below).</summary>
public sealed class CreateOfferingRequestValidator : AbstractValidator<CreateOfferingRequest>
{
    public CreateOfferingRequestValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.CategoryCode).NotEmpty();
        RuleFor(x => x.UnitOfMeasureCode).NotEmpty();
        RuleFor(x => x.PriceAmount).GreaterThanOrEqualTo(0).When(x => x.PriceAmount is not null);
        RuleForEach(x => x.Attributes).ChildRules(attr =>
        {
            attr.RuleFor(kv => kv.Key).NotEmpty().MaximumLength(100);
            attr.RuleFor(kv => kv.Value).NotEmpty().MaximumLength(500);
        }).When(x => x.Attributes is not null);
    }
}

/// <summary>FEAT-06.1/FR-OFF-001: create/edit/deactivate an Offering, row-scoped to the caller's
/// own supplier (IScopeContext.SupplierId - never client input).</summary>
public static class OfferingEndpoints
{
    private static IResult MapMutation(OfferingMutationResult result) => result switch
    {
        OfferingMutationResult.Success s => Results.Ok(s.Offering),
        OfferingMutationResult.NotFoundOrOutOfScope => Results.NotFound(),
        OfferingMutationResult.InvalidCategory => Results.BadRequest(new { error = "invalid_category" }),
        OfferingMutationResult.InvalidUnitOfMeasure => Results.BadRequest(new { error = "invalid_unit_of_measure" }),
        OfferingMutationResult.InvalidCurrency => Results.BadRequest(new { error = "invalid_currency" }),
        _ => Results.Problem(),
    };

    public static void MapOfferingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/suppliers/me/offerings").WithTags("Offerings");

        group.MapGet("/", async (IListOfferingsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("ListOfferings");

        group.MapPost("/", async (
            CreateOfferingRequest request,
            IValidator<CreateOfferingRequest> validator,
            ICreateOfferingHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(
                new CreateOfferingCommand(request.NameAr, request.NameEn, request.Description, request.CategoryCode, request.UnitOfMeasureCode, request.PriceAmount, request.CurrencyCode, request.Attributes), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("CreateOffering");

        group.MapPut("/{offeringId:guid}", async (
            Guid offeringId,
            CreateOfferingRequest request,
            IValidator<CreateOfferingRequest> validator,
            IUpdateOfferingHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(
                new UpdateOfferingCommand(offeringId, request.NameAr, request.NameEn, request.Description, request.CategoryCode, request.UnitOfMeasureCode, request.PriceAmount, request.CurrencyCode, request.Attributes), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        // T-029: a supplier's catalogue is edited by every supplier_user at that supplier, so two
        // people editing one offering is the ordinary case, not the exotic one. Until now the second
        // write silently overwrote the first. Same contract as every other versioned aggregate - the
        // header is not a second concurrency path, it is the one from #96.
        .RequireIfMatch()
        .WithETag()
        .WithName("UpdateOffering");

        group.MapPost("/{offeringId:guid}/deactivate", async (Guid offeringId, IDeactivateOfferingHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(offeringId, ct)))
        .RequirePermission(Permissions.SupplierEdit)
        // Deactivation is a state change on the same aggregate and races the same way: an edit and a
        // deactivate arriving together must not both silently win.
        .RequireIfMatch()
        .WithETag()
        .WithName("DeactivateOffering");

        // FEAT-06.3/FR-OFF-004/FR-SRCH-001: a separate route from /suppliers/me/offerings above -
        // this is procurement staff searching across ALL suppliers' offerings, not a supplier
        // managing its own catalog, so it is gated on OfferingSearch rather than SupplierEdit.
        app.MapGet("/api/v1/offerings/search", async (
            string? categoryCode,
            string? query,
            ISearchBuyerOfferingsHandler handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(categoryCode, query, ct)))
        .RequirePermission(Permissions.OfferingSearch)
        .WithTags("Offerings")
        .WithName("SearchBuyerOfferings");
    }
}
