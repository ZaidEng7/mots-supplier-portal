// The published API document, and how this API reads and writes its wire format.
//
// The document is the contract source: the interface's types are generated from it, the
// finance-system client is generated from it, and the build compares it against what is deployed.
//
// Two transformers add what the generator cannot see on its own. One documents the write precondition
// each route requires, and the other documents the permission each route is gated on. Both read the
// same route metadata that the filters enforce, so a route that changes either changes its
// documentation in the same edit.
//
// Every named value reads and writes its name on the wire rather than a number, everywhere, so no
// client has to know the order in which somebody declared them.
//
// A payload carrying a field this API does not model is refused rather than quietly ignored. The
// default is to skip unknown members, which meant a misspelt or stale field name was swallowed and the
// caller told everything was fine, so a client could believe it had updated something it had not.
// Applied globally, so every route inherits it rather than only the one where it was noticed.

namespace MotsSupplierPortal.Api.Startup;

internal static class ApiContractRegistration
{
    internal static WebApplicationBuilder AddApiContract(this WebApplicationBuilder builder)
    {
        builder.Services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<MotsSupplierPortal.Api.Concurrency.ConcurrencyOpenApiTransformer>();
            options.AddOperationTransformer<MotsSupplierPortal.Api.Authorization.PermissionOpenApiTransformer>();
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

            options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
        });

        return builder;
    }
}
