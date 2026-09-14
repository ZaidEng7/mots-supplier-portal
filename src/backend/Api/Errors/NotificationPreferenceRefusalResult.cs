// A notification-preferences write refused, with the offending notification types on the response.
//
// Two reasons to refuse. Some notifications can never be switched off: invitations, clarification
// requests, award outcomes and document expiry are always delivered. And a type this system does not
// produce is refused rather than stored, because a preference for a notification nobody sends can never
// be honoured and never be seen to fail.
//
// It is its own result type for the same reason the unknown-reference-codes refusal is. The middleware
// reshapes every failure into the standard problem format, so a plain object carrying the types arrives
// as a generic validation failure, which tells the caller something was wrong with the request but not
// which switch the server would not accept. Found by asserting the code in a test.
//
// The types travel on the body because the screen has to say which switch it could not accept. A
// refusal a user cannot act on is a refusal they will simply retry.

namespace MotsSupplierPortal.Api.Errors;

using System.Text.Json.Nodes;

internal sealed record NotificationPreferenceRefusalResult(string Code, string Title, string Detail, IReadOnlyList<string> Types)
    : IResult
{
    public static NotificationPreferenceRefusalResult NotMuteable(IReadOnlyList<string> types) =>
        new("NOTIFICATION_NOT_MUTEABLE",
            "Some notifications cannot be switched off.",
            "Invitations, clarification requests, award outcomes and document expiry are always delivered.",
            types);

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
