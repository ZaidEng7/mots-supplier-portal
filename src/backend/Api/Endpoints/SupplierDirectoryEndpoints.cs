using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// SCR-402 and SCR-307: the two supplier directories, for the two personas who need to read across the
/// whole registry.
///
/// <para><b>Two endpoints rather than one shaped by the caller's role.</b> They answer different questions
/// and return different columns: a buyer asks who can supply a category, a reviewer asks what state the
/// registry is in. One endpoint branching on permission would have to decide which shape an account holding
/// both gets, and whichever it picked would be wrong for one of the two screens.</para>
///
/// <para><b>No organization scoping, and that is BRULE-029's own reading.</b> The supplier registry is
/// national - a supplier is not owned by a buying body, and `Supplier` carries no organization column.
/// Scoping these lists would return nothing.</para>
/// </summary>
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

            // Refused rather than ignored, §6.2. An unrecognised lifecycle state here is the silent-widening
            // case: the handler's Enum.TryParse simply fails, no predicate is applied, and ?lifecycleState=Activ
            // returns suspended suppliers inside a list the screen labels Active.
            if (!FilterValues.IsAllowed(lifecycleState, SupplierDirectoryFilterValues.LifecycleStates, out var badState))
            {
                return FilterValues.InvalidFilterValue("lifecycleState", badState!);
            }

            return ListResponse.Ok(httpContext,
                await handler.HandleAsync(cursor, pageSize, FilterValues.BoolOrFalse(withCount), category, lifecycleState, q, ct),
                pageSize);
        })
        .RequirePermission(Permissions.SupplierDirectoryRead)
        // §6.3: alphabetical and only alphabetical. A directory is read by name, and offering a second
        // order would mean offering a sort nobody asked for on a list whose first page is the answer.
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
