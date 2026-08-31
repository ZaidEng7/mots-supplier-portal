using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Organizations;

namespace MotsSupplierPortal.Api.Endpoints;

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

/// <summary>Task #7/Stage C: admin-only. Organization creation, OrgUnit management, and the
/// manual "Ministry approves this link" SupplierOrgLink action (BRULE-010) - no auto-linking
/// anywhere; a link exists only when explicitly created here by an admin.organizations.manage
/// permission holder. See OrganizationHandlers.cs's doc comment for the internal-Guid-in-URL and
/// missing-Address decisions.</summary>
public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations").WithTags("Organizations");

        group.MapGet("/", async (IListOrganizationsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .WithName("ListOrganizations");

        group.MapPost("/", async (CreateOrganizationRequest request, ICreateOrganizationHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new CreateOrganizationCommand(request.LegalNameAr, request.LegalNameEn, request.OrganizationType, request.ContactEmail, request.ContactPhone), ct);
            return MapOrganizationMutation(result);
        })
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .WithName("CreateOrganization");

        group.MapPost("/{organizationId:guid}/org-units", async (Guid organizationId, AddOrgUnitRequest request, IManageOrgUnitHandler handler, CancellationToken ct) =>
        {
            var result = await handler.AddAsync(new AddOrgUnitCommand(organizationId, request.Name, request.ParentOrgUnitId), ct);
            return MapOrganizationMutation(result);
        })
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .WithName("AddOrgUnit");

        group.MapDelete("/{organizationId:guid}/org-units/{orgUnitId:guid}", async (Guid organizationId, Guid orgUnitId, IManageOrgUnitHandler handler, CancellationToken ct) =>
        {
            var result = await handler.RemoveAsync(new RemoveOrgUnitCommand(organizationId, orgUnitId), ct);
            return MapOrganizationMutation(result);
        })
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .WithName("RemoveOrgUnit");

        // --- SupplierOrgLink: the manual "Ministry approves this link" action ---

        group.MapGet("/supplier-links/{supplierReferenceCode}", async (string supplierReferenceCode, IManageSupplierOrgLinkHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ListForSupplierAsync(supplierReferenceCode, ct)))
        .RequirePermission(Permissions.AdminOrganizationsManage)
        .WithName("ListSupplierOrgLinks");

        group.MapPost("/supplier-links/{supplierReferenceCode}", async (string supplierReferenceCode, CreateSupplierOrgLinkRequest request, IManageSupplierOrgLinkHandler handler, CancellationToken ct) =>
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
