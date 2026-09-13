// Creating buying organizations, managing their department trees, and the manual act of linking a
// supplier to one. Administrators only.
//
// All three writes here run their request's validator. They did not: the validators were declared in
// this file, registered by the assembly scan that picks up every validator in the project, and never
// called, because the routes went straight to the handler.
//
// Nothing therefore enforced a non-empty legal name, the column widths behind those names, or a contact
// address that is an address. The over-long name was the one that reached the database, where a
// two-hundred-character column refused it and the caller saw an unexpected failure naming neither the
// field nor the limit. The Arabic wording for all of those refusals existed and was approved, and could
// never be shown.
//
// That is the shape this whole class of gap takes: two lines inside a route, omitted, with nothing that
// could notice. The permission on each route below cannot be forgotten the same way, because it is
// declared rather than written out.
//
// There is no automatic linking anywhere. A link between a supplier and an organization exists only when
// somebody holding the organization-management permission explicitly creates it here, which is the
// ministry approving that link.

namespace MotsSupplierPortal.Api.Endpoints;

using FluentValidation;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Organizations;

public sealed record CreateOrganizationRequest(string LegalNameAr, string LegalNameEn, OrganizationType OrganizationType, string? ContactEmail, string? ContactPhone);

public sealed class CreateOrganizationRequestValidator : AbstractValidator<CreateOrganizationRequest>
{
    public CreateOrganizationRequestValidator()
    {
        RuleFor(x => x.LegalNameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LegalNameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).EmailAddress().When(x => !string.IsNullOrEmpty(x.ContactEmail));
    }
}

public sealed record AddOrgUnitRequest(string Name, Guid? ParentOrgUnitId);

public sealed class AddOrgUnitRequestValidator : AbstractValidator<AddOrgUnitRequest>
{
    public AddOrgUnitRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed record CreateSupplierOrgLinkRequest(Guid OrganizationId);

public sealed class CreateSupplierOrgLinkRequestValidator : AbstractValidator<CreateSupplierOrgLinkRequest>
{
    public CreateSupplierOrgLinkRequestValidator() => RuleFor(x => x.OrganizationId).NotEmpty();
}

public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations").WithTags("Organizations");

        group.MapGet("/", async (IListOrganizationsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .WithName("ListOrganizations");

        group.MapPost("/", async (
            CreateOrganizationRequest request,
            ICreateOrganizationHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new CreateOrganizationCommand(request.LegalNameAr, request.LegalNameEn, request.OrganizationType, request.ContactEmail, request.ContactPhone), ct);
            return MapOrganizationMutation(result);
        })
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .Validate<CreateOrganizationRequest>()
        .WithName("CreateOrganization");

        group.MapPost("/{organizationId:guid}/org-units", async (
            Guid organizationId,
            AddOrgUnitRequest request,
            IManageOrgUnitHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.AddAsync(new AddOrgUnitCommand(organizationId, request.Name, request.ParentOrgUnitId), ct);
            return MapOrganizationMutation(result);
        })
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .Validate<AddOrgUnitRequest>()
        .WithName("AddOrgUnit");

        group.MapDelete("/{organizationId:guid}/org-units/{orgUnitId:guid}", async (Guid organizationId, Guid orgUnitId, IManageOrgUnitHandler handler, CancellationToken ct) =>
        {
            var result = await handler.RemoveAsync(new RemoveOrgUnitCommand(organizationId, orgUnitId), ct);
            return MapOrganizationMutation(result);
        })
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .WithName("RemoveOrgUnit");

        group.MapGet("/supplier-links/{supplierReferenceCode}", async (string supplierReferenceCode, IManageSupplierOrgLinkHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ListForSupplierAsync(supplierReferenceCode, ct)))
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .WithName("ListSupplierOrgLinks");

        group.MapPost("/supplier-links/{supplierReferenceCode}", async (
            string supplierReferenceCode,
            CreateSupplierOrgLinkRequest request,
            IManageSupplierOrgLinkHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.CreateAsync(new CreateSupplierOrgLinkCommand(supplierReferenceCode, request.OrganizationId), ct);
            return result switch
            {
                SupplierOrgLinkMutationResult.Success s => Results.Ok(s.Link),
                SupplierOrgLinkMutationResult.NotFound => Results.NotFound(),
                SupplierOrgLinkMutationResult.AlreadyLinked => Results.Conflict(new { error = "already_linked" }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .Validate<CreateSupplierOrgLinkRequest>()
        .WithName("CreateSupplierOrgLink");

        group.MapDelete("/supplier-links/{linkId:guid}", async (Guid linkId, IManageSupplierOrgLinkHandler handler, CancellationToken ct) =>
            await handler.RemoveAsync(new RemoveSupplierOrgLinkCommand(linkId), ct) ? Results.NoContent() : Results.NotFound())
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .WithName("RemoveSupplierOrgLink");
    }

    private static IResult MapOrganizationMutation(OrganizationMutationResult result) => result switch
    {
        OrganizationMutationResult.Success s => Results.Ok(s.Organization),
        OrganizationMutationResult.NotFound => Results.NotFound(),
        OrganizationMutationResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
        _ => Results.Problem(),
    };
}
