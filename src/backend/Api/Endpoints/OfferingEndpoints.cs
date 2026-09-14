// A supplier's own catalogue: creating, editing and deactivating what the company says it can supply, plus
// the separate search buying staff use across every supplier's catalogue.
//
// Everything on the supplier side is scoped to the caller's own company, taken from their token and never
// from the request.
//
// The create and update share one request shape, because they take the same fields under the same rules.
//
// The single-item read exists to make the guarded writes usable. A write precondition needs a response that
// handed the caller a version to send back, and this resource had only a list, so requiring the precondition
// on deactivation made it unobtainable and refused every caller.
//
// Both the update and the deactivation require that precondition. A supplier's catalogue is edited by every
// one of that company's users, so two people editing one entry is the ordinary case rather than the exotic
// one, and until this was added the second write silently overwrote the first. A deactivation is a change to
// the same record and races the same way: an edit and a deactivation arriving together must not both quietly
// win.
//
// Both put the new version on their response, so a second change has a precondition without a re-read.
//
// The search is a different route with a different permission, because it is buying staff reading across all
// suppliers rather than a supplier managing its own catalogue.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

public sealed record CreateOfferingRequest(string NameAr, string NameEn, string? Description, string CategoryCode, string UnitOfMeasureCode, decimal? PriceAmount, string? CurrencyCode, IReadOnlyDictionary<string, string>? Attributes);

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

        group.MapGet("/{offeringId:guid}", async (Guid offeringId, IGetOfferingHandler handler, CancellationToken ct) =>
        {
            var offering = await handler.HandleAsync(offeringId, ct);
            return offering is null ? Results.NotFound() : Results.Ok(offering);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithETag()
        .WithName("GetOffering");

        group.MapPost("/", async (
            CreateOfferingRequest request,
            ICreateOfferingHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new CreateOfferingCommand(request.NameAr, request.NameEn, request.Description, request.CategoryCode, request.UnitOfMeasureCode, request.PriceAmount, request.CurrencyCode, request.Attributes), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .Validate<CreateOfferingRequest>()
        .WithName("CreateOffering");

        group.MapPut("/{offeringId:guid}", async (
            Guid offeringId,
            CreateOfferingRequest request,
            IUpdateOfferingHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new UpdateOfferingCommand(offeringId, request.NameAr, request.NameEn, request.Description, request.CategoryCode, request.UnitOfMeasureCode, request.PriceAmount, request.CurrencyCode, request.Attributes), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .Validate<CreateOfferingRequest>()
        .WithETag()
        .WithFreshETag()
.WithName("UpdateOffering");

        group.MapPost("/{offeringId:guid}/deactivate", async (Guid offeringId, IDeactivateOfferingHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(offeringId, ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithETag()
        .WithFreshETag()
.WithName("DeactivateOffering");

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
