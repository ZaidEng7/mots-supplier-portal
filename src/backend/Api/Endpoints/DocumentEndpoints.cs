using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record RejectDocumentRequest(string Reason);

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/suppliers/me/documents", async (
            IListSupplierDocumentsHandler handler,
            CancellationToken ct) =>
        {
            var documents = await handler.HandleOwnAsync(ct);
            return Results.Ok(documents);
        })
        .RequireAuthorization()
        .WithTags("Documents")
        .WithName("ListOwnDocuments");

        app.MapPost("/api/v1/suppliers/me/documents", async (
            HttpRequest request,
            IUploadDocumentHandler handler,
            CancellationToken ct) =>
        {
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

        app.MapPost("/api/v1/documents/{id:guid}/approve", async (
            Guid id,
            IApproveDocumentHandler handler,
            CancellationToken ct) =>
        {
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

        app.MapPost("/api/v1/documents/{id:guid}/reject", async (
            Guid id,
            RejectDocumentRequest request,
            IRejectDocumentHandler handler,
            CancellationToken ct) =>
        {
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
