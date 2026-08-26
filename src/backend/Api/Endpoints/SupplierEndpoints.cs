using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record UpdateProfileRequest(
    string? RegistrationNumber,
    string? TaxId,
    string? AddressLine,
    string? City,
    string? Country,
    string? CurrencyCode,
    string? PrimaryContactPhone);

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.CurrencyCode).Length(3).When(x => x.CurrencyCode is not null);
    }
}

public static class SupplierEndpoints
{
    public static void MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/suppliers").WithTags("Suppliers");

        // Authenticated + row-scoped (STORY-01.8.1); no specific permission needed - any
        // authenticated supplier user may look up their own supplier record.
        group.MapGet("/{referenceCode}", async (
            string referenceCode,
            IGetSupplierHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(referenceCode, ct);

            return result switch
            {
                GetSupplierResult.Found f => Results.Ok(f.Supplier),
                GetSupplierResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithName("GetSupplier");

        group.MapGet("/me", async (IGetSupplierHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleOwnAsync(ct);

            return result switch
            {
                GetSupplierResult.Found f => Results.Ok(f.Supplier),
                GetSupplierResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithName("GetOwnSupplier");

        // Self-service: the caller's own supplier record, resolved from the JWT's supplierId
        // claim (row-scoped) rather than a path parameter - the SPA never needs to know its
        // own reference code to drive onboarding.
        group.MapPatch("/me/profile", async (
            UpdateProfileRequest request,
            IValidator<UpdateProfileRequest> validator,
            IUpdateProfileHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            var command = new UpdateProfileCommand(
                request.RegistrationNumber, request.TaxId, request.AddressLine,
                request.City, request.Country, request.CurrencyCode, request.PrimaryContactPhone);

            var result = await handler.HandleAsync(command, ct);

            return result switch
            {
                UpdateProfileResult.Success s => Results.Ok(s.Supplier),
                UpdateProfileResult.NotFoundOrOutOfScope => Results.NotFound(),
                UpdateProfileResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithName("UpdateSupplierProfile");

        group.MapPost("/me/submit-application", async (
            ISubmitApplicationHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);

            return result switch
            {
                SubmitApplicationResult.Success s => Results.Ok(s.Supplier),
                SubmitApplicationResult.NotFoundOrOutOfScope => Results.NotFound(),
                SubmitApplicationResult.Incomplete i => Results.UnprocessableEntity(new { error = "incomplete_profile", missingFields = i.MissingFields }),
                SubmitApplicationResult.InvalidState s => Results.Conflict(new { error = s.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierSubmit)
        .WithName("SubmitSupplierApplication");
    }
}
