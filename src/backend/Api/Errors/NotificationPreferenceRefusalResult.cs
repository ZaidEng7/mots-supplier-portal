using System.Text.Json.Nodes;

namespace MotsSupplierPortal.Api.Errors;

/// <summary>
/// SCR-901: a preferences write refused, with the offending types on the response.
///
/// <para>Same shape and same reason as <see cref="UnknownReferenceCodesResult"/>: §7's middleware reshapes
/// every non-2xx into problem+json, so an anonymous <c>{ code, types }</c> body does not survive - it arrives
/// as VALIDATION_FAILED, which tells the caller that something was wrong with the request and not WHICH
/// preference the server would not honour. Found by asserting the code in the test.</para>
///
/// <para>The types travel on the body because the screen has to say which switch it could not accept. A
/// refusal a user cannot act on is a refusal they will retry.</para>
/// </summary>
internal sealed record NotificationPreferenceRefusalResult(string Code, string Title, string Detail, IReadOnlyList<string> Types)
    : IResult
{
    /// <summary>D-60's constraint: invitations, clarification requests, award outcomes and document expiry
    /// are always delivered.</summary>
    public static NotificationPreferenceRefusalResult NotMuteable(IReadOnlyList<string> types) =>
        new("NOTIFICATION_NOT_MUTEABLE",
            "Some notifications cannot be switched off.",
            "Invitations, clarification requests, award outcomes and document expiry are always delivered.",
            types);

    /// <summary>A type this system does not produce. Refused rather than stored: a row for a type nobody
    /// sends is a preference that can never be honoured and never be seen to fail.</summary>
    public static NotificationPreferenceRefusalResult Unknown(IReadOnlyList<string> types) =>
        new("UNKNOWN_NOTIFICATION_TYPES",
            "Unknown notification types.",
            "These are not notification types this system produces.",
            types);

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var problem = ProblemResponse.Build(
            httpContext, StatusCodes.Status422UnprocessableEntity, ProblemTypes.Validation,
            Title, code: Code, detail: Detail);

        problem["types"] = new JsonArray([.. Types.Select(type => JsonValue.Create(type))]);

        return ProblemResponse.WriteAsync(httpContext, problem);
    }
}
