// The compliance documents: uploading one, listing them, downloading one, reading its version history, and a
// reviewer approving or rejecting one.
//
//
// ONE PATH, TWO SHAPES
//
// The contract gives one path for the document list and describes it as paged by default for the back
// office, while the supplier's own checklist lives at the same resource. So the route serves two shapes and
// decides between them by who is asking, rather than inventing a second address. That is the same persona
// dispatch the tender routes use, for the same reason.
//
// The supplier keeps an unpaged view of their own checklist, which is what the registration wizard renders
// and is not a back-office grid. The reviewer gets the paged form.
//
// An unrecognised state filter is refused rather than dropped. Dropping the only value leaves an empty
// filter, and an empty filter returns everything, so the caller asked to narrow and got the opposite with no
// way to tell.
//
// The paging cap is checked before the query runs, which is the point of a cap.
//
//
// UPLOADING
//
// Scope is checked before the body is read. An out-of-scope caller must not be able to tell a malformed
// upload from an unauthorised one, and must not stream a file at all.
//
// Dates are parsed with the invariant calendar rather than the host's. A host whose locale defaults to a
// non-Gregorian calendar silently fails to parse the plain date string that a browser's own date input
// always sends, because those inputs are locale-independent by specification.
//
// The answer is accepted rather than created, which is the honest code: the row exists but the pipeline is
// not finished with it, and the scanning job is what finishes it. Created would have promised a completed
// creation. The location header points at the path the contract names, and that path is real because the
// read behind it was added in the same change. An earlier pass left the header non-conforming on the
// grounds that conforming the string alone would point at nothing; the answer was to make the documented
// path real rather than keep the divergence.
//
// A disallowed file type and an oversized file get their own statuses, which the contract names explicitly.
// They used to be generic refusals, which tell a client the request was malformed rather than that the file
// was too big or of the wrong kind.
//
// The expiry-date refusal carries the domain's own message, which says what is wrong with the date rather
// than leaving the uploader to guess between missing, past and misformatted.
//
// The upload has its own request-size limit. Without it the framework's multipart reader accepts up to its
// own default, which is far larger, before the application's own check ever runs, so an oversized body was
// already fully buffered by then. The limit is derived from the single source of truth for the file cap,
// plus a little headroom for the multipart boundaries and the other form fields rather than slack on the
// file itself.
//
//
// WHY THE UPLOAD IS NOT GUARDED BY A WRITE PRECONDITION
//
// I added the guard first and then removed it, for two reasons, one of which a test found.
//
// There is no lost update to refuse. Uploading adds a document; it cannot overwrite another upload, and two
// of a supplier's users adding two different documents both succeeding is the correct outcome. That is the
// same reasoning that leaves a supplier posting a clarification unguarded: concurrent additions are not a
// conflict.
//
// And the hazard is real and one-sided. This route answers with the document, so there is no root version to
// hand back and no filter to supply one, while the interface drops its cached version on every successful
// write. A supplier uploading two documents in a row would therefore be refused on the second with nothing
// on screen to explain it. The streaming upload test caught exactly that shape immediately.
//
// The decisions below are guarded, because that is where the lost update lives: two reviewers deciding the
// same document is one decision silently replacing the other.
//
//
// DOWNLOADING
//
// There are two download routes on purpose. One returns a short-lived link as data, which is what the
// interface calls and can read. The other redirects to that link, which is what the contract documents, and
// which a browser can follow but application code cannot hand back to itself.
//
// They share one handler, so the two cannot authorise differently. The redirect is a temporary one that must
// not be cached, because the link it points at expires in minutes.
//
// Both carry only a requirement to be signed in, and that is not an oversight. The handler serves both a
// supplier reading their own document and a reviewer reading somebody else's, and it does the scoping
// itself. A permission here would have to name one of the two personas and would lock out the other, and
// two routes onto one handler must not authorise differently.
//
//
// VERSION HISTORY
//
// A document has carried a version number and a latest-version marker since it was first built, and nothing
// returned the history, so a supplier could see a document's current state and never why it got there. A
// rejection followed by a re-upload looked exactly like a first upload that was approved.
//
//
// THE REVIEWER'S DECISION
//
// The path names the supplier as well as the document, and the guard is not "is this mine" but "does the
// path name the document's real owner". Otherwise a reviewer could act on one supplier's document through
// another supplier's address, and the audit row would name the wrong supplier.
//
// Both decisions require a write precondition, because a document decision is a write on the supplier as a
// whole, and two reviewers deciding one document at once is the lost update worth refusing. The precondition
// comes from the reviewer's own read of the application, which issues it.
//
// The response carries the document, with the supplier's new version on the header rather than in the body.
// The shared fresh-version filter is not used, because it looks for a version on the response body, and a
// document has no version of its own. Putting the supplier's version into the document's shape to satisfy
// the filter would ship a field whose name lies about what it describes, and the contract documents this
// response as the document.
//
// The supplier's version is the right one anyway, because that is what the precondition guards and what the
// reviewer's read issues. So a reviewer deciding a second document already holds the value the next write
// needs, instead of meeting a refusal only a re-read could clear.

namespace MotsSupplierPortal.Api.Endpoints;

using System.Globalization;
using MotsSupplierPortal.Api.Concurrency;
using Microsoft.AspNetCore.Mvc;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed record RejectDocumentRequest(string Reason);

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/suppliers/{supplierCode}/documents", async (
            string supplierCode,
            string? state,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            IScopeContext scope,
            ISupplierCodeScope codeScope,
            IListSupplierDocumentsHandler ownHandler,
            IListSupplierDocumentsPagedHandler pagedHandler,
            CancellationToken ct) =>
        {
            if (scope.SupplierId is not null)
            {
                if (await codeScope.ResolveOwnAsync(supplierCode, ct) is null) return Results.NotFound();
                return Results.Ok(await ownHandler.HandleOwnAsync(ct));
            }

            if (!FilterValues.TryParseEnumCsv<DocumentState>(state, out _, out var invalidState))
            {
                return FilterValues.InvalidFilterValue("state", invalidState!);
            }

            var requestedPage = page is null or < 1 ? 1 : page.Value;

            if (ListEnvelope<SupplierDocumentListItemDto>.ExceedsPageCap(requestedPage, pageSize))
            {
                return Results.Json(new
                {
                    type = "https://api.mots-portal.sy/errors/validation",
                    title = "Page offset too large.",
                    status = StatusCodes.Status422UnprocessableEntity,
                    code = "PAGE_OFFSET_TOO_LARGE",
                    detail = $"page * pageSize must not exceed {ListEnvelope<SupplierDocumentListItemDto>.MaxPageOffset}. Use cursor mode for deeper reads.",
                }, statusCode: StatusCodes.Status422UnprocessableEntity, contentType: "application/problem+json");
            }

            var paged = await pagedHandler.HandleAsync(supplierCode, state, requestedPage, pageSize, ct);
            return paged is null ? Results.NotFound() : ListResponse.Ok(httpContext, paged, pageSize);
        })
        .RequireAuthorization()
        .WithTags("Documents")
        .WithName("ListOwnDocuments");

        app.MapPost("/api/v1/suppliers/{supplierCode}/documents", async (
            string supplierCode,
            ISupplierCodeScope codeScope,
            HttpRequest request,
            IUploadDocumentHandler handler,
            CancellationToken ct) =>
        {
            if (await codeScope.ResolveOwnAsync(supplierCode, ct) is null) return Results.NotFound();

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "expected_multipart_form" });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "file_required" });
            }

            if (!Guid.TryParse(form["documentTypeId"], out var documentTypeId))
            {
                return Results.BadRequest(new { error = "documentTypeId_required" });
            }

            DateOnly? issueDate = DateOnly.TryParse(form["issueDate"], CultureInfo.InvariantCulture, out var issue) ? issue : null;
            DateOnly? expiryDate = DateOnly.TryParse(form["expiryDate"], CultureInfo.InvariantCulture, out var expiry) ? expiry : null;

            await using var stream = file.OpenReadStream();
            var command = new UploadDocumentCommand(
                documentTypeId, stream, file.FileName, file.ContentType, file.Length, issueDate, expiryDate);

            var result = await handler.HandleAsync(command, ct);

            return result switch
            {
                UploadDocumentResult.Success s => Results.Accepted(
                    $"/api/v1/suppliers/{supplierCode}/documents/{s.Document.DocumentId}", s.Document),
                UploadDocumentResult.NotFoundOrOutOfScope => Results.NotFound(),
                UploadDocumentResult.InvalidDocumentType => Results.BadRequest(new { error = "invalid_document_type" }),
                UploadDocumentResult.TooLarge => Results.StatusCode(StatusCodes.Status413PayloadTooLarge),
                UploadDocumentResult.UnsupportedType => Results.StatusCode(StatusCodes.Status415UnsupportedMediaType),
                UploadDocumentResult.InvalidExpiry e => Results.BadRequest(new { error = "invalid_expiry", message = e.Message }),
                UploadDocumentResult.ContentMismatch => Results.BadRequest(new { error = "content_type_mismatch" }),
                UploadDocumentResult.NotEditable n => Results.Conflict(new { error = n.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithTags("Documents")
        .WithName("UploadDocument")
        .DisableAntiforgery()
        .WithMetadata(new RequestFormLimitsAttribute
        {
            MultipartBodyLengthLimit = FileTypeSniffer.MaxSizeBytes + 1024 * 1024,
        });

        app.MapGet("/api/v1/suppliers/{supplierCode}/documents/{documentCode}", async (
            string supplierCode, string documentCode,
            IGetSupplierDocumentHandler handler, CancellationToken ct) =>
        {
            var document = await handler.HandleAsync(supplierCode, documentCode, ct);
            return document is null ? Results.NotFound() : Results.Ok(document);
        })
        .RequireAuthorization()
        .WithTags("Documents")
        .WithName("GetSupplierDocument");

        app.MapGet("/api/v1/suppliers/{supplierCode}/documents/types/{documentTypeCode}/history", async (
            string supplierCode, string documentTypeCode,
            IGetDocumentHistoryHandler handler, CancellationToken ct) =>
        {
            var history = await handler.HandleAsync(supplierCode, documentTypeCode, ct);
            return history is null ? Results.NotFound() : Results.Ok(history);
        })
        .RequireAuthorization()
        .WithTags("Documents")
        .WithName("GetDocumentHistory");

        app.MapGet("/api/v1/documents/{documentCode}/content", async (
            string documentCode,
            IGetDocumentDownloadUrlHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(documentCode, ct);
            return result switch
            {
                DocumentDownloadUrlResult.Success s => Results.Redirect(s.Url),
                DocumentDownloadUrlResult.NotFoundOrForbidden => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithTags("Documents")
        .WithName("GetDocumentContent");

        app.MapGet("/api/v1/documents/{documentCode}/download-url", async (
            string documentCode,
            IGetDocumentDownloadUrlHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(documentCode, ct);
            return result switch
            {
                DocumentDownloadUrlResult.Success s => Results.Ok(new { url = s.Url }),
                DocumentDownloadUrlResult.NotFoundOrForbidden => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithTags("Documents")
        .WithName("GetDocumentDownloadUrl");

        app.MapPost("/api/v1/suppliers/{supplierCode}/documents/{documentCode}/approve", async (
            string supplierCode,
            string documentCode,
            ISupplierCodeScope codeScope,
            IApproveDocumentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!await codeScope.DocumentBelongsToSupplierAsync(supplierCode, documentCode, ct)) return Results.NotFound();

            var result = await handler.HandleAsync(documentCode, ct);
            return result switch
            {
                ReviewDocumentResult.Success s => OkWithFreshSupplierETag(http, s),
                ReviewDocumentResult.NotFoundOrForbidden => Results.NotFound(),
                ReviewDocumentResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.DocumentReview)
        .RequireIfMatch()
        .WithMetadata(EmitsETagMetadata.Instance)
        .WithTags("Documents")
        .WithName("ApproveDocument");

        app.MapPost("/api/v1/suppliers/{supplierCode}/documents/{documentCode}/reject", async (
            string supplierCode,
            string documentCode,
            ISupplierCodeScope codeScope,
            RejectDocumentRequest request,
            IRejectDocumentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!await codeScope.DocumentBelongsToSupplierAsync(supplierCode, documentCode, ct)) return Results.NotFound();

            var result = await handler.HandleAsync(documentCode, request.Reason, ct);
            return result switch
            {
                ReviewDocumentResult.Success s => OkWithFreshSupplierETag(http, s),
                ReviewDocumentResult.NotFoundOrForbidden => Results.NotFound(),
                ReviewDocumentResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.DocumentReview)
        .RequireIfMatch()
        .WithMetadata(EmitsETagMetadata.Instance)
        .WithTags("Documents")
        .WithName("RejectDocument");
    }

    private static IResult OkWithFreshSupplierETag(HttpContext http, ReviewDocumentResult.Success success)
    {
        http.SetETag(success.SupplierRowVersion);
        return Results.Ok(success.Document);
    }
}
