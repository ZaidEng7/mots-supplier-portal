// The check that a request body is valid, declared on the route rather than written out inside it.
//
// A route says what it accepts, and this runs that request's validator before the handler sees it. If
// anything fails, the caller gets the standard field-level bilingual refusal and the handler is never
// called.
//
//
// WHY THIS IS A FILTER AND NOT TWO LINES IN EACH HANDLER
//
// It used to be two lines in each handler, in sixty-five places, plus a validator parameter each route
// had to remember to ask for. The lines were easy to read past and the cost of that was not the
// repetition.
//
// The permission on a route is declared, so a route cannot quietly lack one. The precondition is
// declared, so the same is true. Validation was the odd one out: two ordinary lines inside a lambda,
// which a new route could simply omit, validating nothing, with nothing anywhere able to notice.
//
// That is not hypothetical. The organization routes declared three validators, had them registered, and
// never called any of them, so a legal name longer than its database column reached the database and came
// back to the caller as an unexpected failure. It was fixed by hand first; this is what makes the shape
// of that mistake impossible rather than merely absent.
//
// ValidatedRequestMetadata is the half that does that work. It puts the request type on the route table,
// so a test can ask the application which routes validate what and compare it against which routes bind a
// type that has a validator. A filter alone is invisible to anything that does not send a request.
//
//
// WHY THE VALIDATOR IS DEMANDED RATHER THAN LOOKED UP
//
// A missing validator throws rather than skipping validation. A route that declares this and has no
// validator registered is a wiring mistake, and the one thing this file exists to prevent is validation
// that silently does not happen. Failing loudly on the first request is the lesser harm, and the
// architecture test catches it before that.
//
//
// WHERE IT GOES IN THE CHAIN, WHICH IS NOT A DETAIL
//
// Filters run in the order they are declared, so the order on the route is the order a caller meets the
// refusals: permission first, then the write precondition, then this.
//
// That order is deliberate and it was the order before this change, when validation ran inside the
// handler and therefore after every filter. Declared before the permission, this would tell a caller
// which fields are wrong on a route they are not allowed to call at all. Declared before the
// precondition, it would answer a validation failure where a caller used to be told their precondition
// was missing.
//
// Nothing about getting that wrong fails to compile, which is why the ordering has its own tests.
//
//
// THREE ROUTES DELIBERATELY STILL VALIDATE INSIDE THE HANDLER
//
// Each one does something first that has to happen before validation, and a filter runs before the
// handler by definition.
//
// Registering a supplier checks whether registration is open at all. A closed portal should not tell an
// applicant their password is weak, and should not spend one of their few attempts a minute to say the
// front door is shut.
//
// Confirming a second factor checks whether that surface is switched on, so a deployment with it off
// answers not-found rather than commenting on the code somebody sent.
//
// Updating a supplier profile resolves the caller's own scope first, so somebody asking about another
// company's profile is told it does not exist rather than which of its fields are wrong.

namespace MotsSupplierPortal.Api.Endpoints;

using FluentValidation;
using MotsSupplierPortal.Api.Errors;

internal sealed class ValidatedRequestMetadata(Type requestType)
{
    public Type RequestType { get; } = requestType;
}

internal sealed class ValidationEndpointFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<TRequest>().FirstOrDefault() is not { } request)
        {
            return await next(context);
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<TRequest>>();
        var validation = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        return validation.IsValid ? await next(context) : ValidationProblems.From(validation);
    }
}

internal static class RequestValidation
{
    internal static RouteHandlerBuilder Validate<TRequest>(this RouteHandlerBuilder builder) =>
        builder
            .AddEndpointFilter(new ValidationEndpointFilter<TRequest>())
            .WithMetadata(new ValidatedRequestMetadata(typeof(TRequest)));
}
