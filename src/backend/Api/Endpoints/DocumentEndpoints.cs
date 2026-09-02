using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record RejectDocumentRequest(string Reason);

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        // §12-A/C3, §12.3: "GET /suppliers/{supplierCode}/documents - list (page mode default for
        // back-office)". See ListSupplierDocumentsEndpoint below for the paged, reviewer-facing
        // form; this route keeps the supplier's own unpaginated view of their checklist, which is
        // what the onboarding wizard renders and is not a back-office grid.
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
            // Same persona dispatch as the converged /rfqs routes, and for the same reason: §12.3
            // gives ONE path and describes it as "page mode default for BACK-OFFICE", while the
            // supplier's own onboarding checklist lives at the same resource. Two shapes, one path,
            // decided by the caller's scope (§9.2) rather than by inventing a second URL.
            if (scope.SupplierId is not null)
            {
                if (await codeScope.ResolveOwnAsync(supplierCode, ct) is null) return Results.NotFound();
                return Results.Ok(await ownHandler.HandleOwnAsync(ct));
            }

            var requestedPage = page is null or < 1 ? 1 : page.Value;

            // §6.1: "Hard cap page*pageSize <= 10 000 to protect the DB; beyond that -> 422 advising
            // cursor mode." Checked before the query runs, which is the point of a cap.
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
            // Scope FIRST, before the body is read: an out-of-scope caller must not be able to tell
            // a malformed upload from an unauthorised one, and must not stream a file at all.
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

            // Invariant culture, not CurrentCulture: the host's OS/container locale (e.g. an
            // Arabic-region default) can default to a non-Gregorian calendar, silently failing
            // TryParse on the ISO "yyyy-MM-dd" string the frontend's <input type="date"> always
            // sends (HTML5 date inputs are locale-independent by spec).
            DateOnly? issueDate = DateOnly.TryParse(form["issueDate"], CultureInfo.InvariantCulture, out var issue) ? issue : null;
            DateOnly? expiryDate = DateOnly.TryParse(form["expiryDate"], CultureInfo.InvariantCulture, out var expiry) ? expiry : null;

            await using var stream = file.OpenReadStream();
            var command = new UploadDocumentCommand(
                documentTypeId, stream, file.FileName, file.ContentType, file.Length, issueDate, expiryDate);

            var result = await handler.HandleAsync(command, ct);

            return result switch
            {
                UploadDocumentResult.Success s => Results.Created($"/api/v1/documents/{s.Document.Id}", s.Document),
                UploadDocumentResult.NotFoundOrOutOfScope => Results.NotFound(),
                UploadDocumentResult.InvalidDocumentType => Results.BadRequest(new { error = "invalid_document_type" }),
                UploadDocumentResult.TooLarge => Results.BadRequest(new { error = "file_too_large" }),
                UploadDocumentResult.UnsupportedType => Results.BadRequest(new { error = "unsupported_file_type" }),
                // BRULE-020: the domain's message names what is wrong with the date rather than
                // leaving the uploader to guess which of null/past/format was rejected.
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
        // MSP-84/NFR-PERF-008: without this, ASP.NET Core's multipart form reader accepts up to
        // its own default MultipartBodyLengthLimit (128MB) before UploadDocumentHandler's 20MB
        // application-level check ever runs - the framework had already buffered/spooled the
        // whole oversized body by then. FileTypeSniffer.MaxSizeBytes is the single source of
        // truth for the 20MB figure; the +1MB headroom is for multipart boundaries and the other
        // form fields (documentTypeId/issueDate/expiryDate), not slack on the file itself.
        .WithMetadata(new RequestFormLimitsAttribute
        {
            MultipartBodyLengthLimit = FileTypeSniffer.MaxSizeBytes + 1024 * 1024,
        });

        app.MapGet("/api/v1/documents/{id:guid}/download-url", async (
            Guid id,
            IGetDocumentDownloadUrlHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);
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

        // §3 lists "POST /suppliers/{supplierCode}/documents/{documentId}/approve" among the
        // state-transition sub-resource POSTs. Reviewer-facing, so the guard is not "is this mine"
        // but "does the path name the document's real owner" - otherwise a reviewer could act on
        // supplier B's document through supplier A's URL and the audit row would name the wrong
        // supplier.
        app.MapPost("/api/v1/suppliers/{supplierCode}/documents/{id:guid}/approve", async (
            string supplierCode,
            Guid id,
            ISupplierCodeScope codeScope,
            IApproveDocumentHandler handler,
            CancellationToken ct) =>
        {
            if (!await codeScope.DocumentBelongsToSupplierAsync(supplierCode, id, ct)) return Results.NotFound();

            var result = await handler.HandleAsync(id, ct);
            return result switch
            {
                ReviewDocumentResult.Success s => Results.Ok(s.Document),
                ReviewDocumentResult.NotFoundOrForbidden => Results.NotFound(),
                ReviewDocumentResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.DocumentReview)
        .WithTags("Documents")
        .WithName("ApproveDocument");

        // §3 lists "POST /suppliers/{supplierCode}/documents/{documentId}/approve" among the
        // state-transition sub-resource POSTs. Reviewer-facing, so the guard is not "is this mine"
        // but "does the path name the document's real owner" - otherwise a reviewer could act on
        // supplier B's document through supplier A's URL and the audit row would name the wrong
        // supplier.
        app.MapPost("/api/v1/suppliers/{supplierCode}/documents/{id:guid}/reject", async (
            string supplierCode,
            Guid id,
            ISupplierCodeScope codeScope,
            RejectDocumentRequest request,
            IRejectDocumentHandler handler,
            CancellationToken ct) =>
        {
            if (!await codeScope.DocumentBelongsToSupplierAsync(supplierCode, id, ct)) return Results.NotFound();

            var result = await handler.HandleAsync(id, request.Reason, ct);
            return result switch
            {
                ReviewDocumentResult.Success s => Results.Ok(s.Document),
                ReviewDocumentResult.NotFoundOrForbidden => Results.NotFound(),
                ReviewDocumentResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.DocumentReview)
        .WithTags("Documents")
        .WithName("RejectDocument");
    }
}
