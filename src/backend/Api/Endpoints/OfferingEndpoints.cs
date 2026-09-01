using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record CreateOfferingRequest(string NameAr, string NameEn, string? Description, string CategoryCode, string UnitOfMeasureCode, decimal? PriceAmount, string? CurrencyCode);

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
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.HandleAsync(
                new CreateOfferingCommand(request.NameAr, request.NameEn, request.Description, request.CategoryCode, request.UnitOfMeasureCode, request.PriceAmount, request.CurrencyCode), ct);
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
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.HandleAsync(
                new UpdateOfferingCommand(offeringId, request.NameAr, request.NameEn, request.Description, request.CategoryCode, request.UnitOfMeasureCode, request.PriceAmount, request.CurrencyCode), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("UpdateOffering");

        group.MapPost("/{offeringId:guid}/deactivate", async (Guid offeringId, IDeactivateOfferingHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(offeringId, ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("DeactivateOffering");
    }
}
