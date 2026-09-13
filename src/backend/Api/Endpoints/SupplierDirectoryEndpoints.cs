// The two supplier directories, for the two people who need to read across the whole registry: a buying
// officer choosing who to invite, and a reviewer looking at the state of the registry.
//
// They are two routes rather than one that changes shape by role. They answer different questions and
// return different columns: a buyer asks who can supply a category, a reviewer asks what state the
// registry is in. One route branching on permission would have to decide which shape an account holding
// both gets, and whichever it picked would be wrong for one of the two screens.
//
// Neither is scoped to an organization, and that follows from the rules rather than ignoring them. The
// supplier registry is national. A supplier is not owned by a buying body and carries no organization at
// all, so scoping these lists would return nothing.
//
// An unrecognised lifecycle filter is refused rather than ignored. This is the silent-widening case: the
// handler's parse simply fails, no filter is applied, and a misspelt value returns suspended suppliers
// inside a list the screen labels as active.
//
// Sorting is alphabetical and only alphabetical. A directory is read by name, and offering a second order
// would mean offering a sort nobody asked for on a list whose first page is the answer.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

public static class SupplierDirectoryEndpoints
{
    public static void MapSupplierDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/supplier-directory", async (
            string? cursor, int? pageSize, string? withCount, string? category, string? lifecycleState, string? q,
            HttpContext httpContext, IListSupplierDirectoryHandler handler, CancellationToken ct) =>
        {
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            if (!FilterValues.IsAllowed(lifecycleState, SupplierDirectoryFilterValues.LifecycleStates, out var badState))
            {
                return FilterValues.InvalidFilterValue("lifecycleState", badState!);
            }

            return ListResponse.Ok(httpContext,
                await handler.HandleAsync(cursor, pageSize, FilterValues.BoolOrFalse(withCount), category, lifecycleState, q, ct),
                pageSize);
        })
        .RequirePermission(Permissions.SupplierDirectoryRead)
        .WithListQuery(ListQueryPolicy.Create("displayNameEn", ["displayNameEn"], "category", "lifecycleState", "q"))
        .WithTags("Suppliers")
        .WithName("ListSupplierDirectory");

        app.MapGet("/api/v1/review/suppliers", async (
            string? cursor, int? pageSize, string? withCount, string? onboardingState, string? documentHealth, string? q,
            HttpContext httpContext, IListComplianceDirectoryHandler handler, CancellationToken ct) =>
        {
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            if (!FilterValues.IsAllowed(onboardingState, SupplierDirectoryFilterValues.OnboardingStates, out var badState))
            {
                return FilterValues.InvalidFilterValue("onboardingState", badState!);
            }

            if (!FilterValues.IsAllowed(documentHealth, SupplierDirectoryFilterValues.DocumentHealth, out var badHealth))
            {
                return FilterValues.InvalidFilterValue("documentHealth", badHealth!);
            }

            return ListResponse.Ok(httpContext,
                await handler.HandleAsync(cursor, pageSize, FilterValues.BoolOrFalse(withCount), onboardingState, documentHealth, q, ct),
                pageSize);
        })
        .RequirePermission(Permissions.SupplierReview)
        .WithListQuery(ListQueryPolicy.Create("displayNameEn", ["displayNameEn"], "onboardingState", "documentHealth", "q"))
        .WithTags("Review")
        .WithName("ListComplianceDirectory");
    }
}
