// The supplier's own profile: reading it, editing it, its logo, its representatives, addresses, contacts,
// branches, bank accounts and categories, accepting the terms, and submitting for review.
//
// Everything here is the caller's own company, resolved from their token rather than from a path parameter,
// so the interface never needs to know its own reference code to drive registration. Where a code does appear
// in the path it is checked against that scope first, and an unknown code and somebody else's code give the
// same not-found answer.
//
// Reading the profile needs no particular permission beyond being signed in, because any of a supplier's
// users may look up their own company's record.
// What a caller may send is in SupplierRequests, and how a handler's answer becomes a status, including
// the refusal that carries a version back, is in SupplierResults. Both used to be in this file.
//
//
// WRITE PRECONDITIONS ON THE CHILD ROUTES
//
// Every route that edits a child of the profile, from representatives through to category links, requires the
// caller to say which version they read.
//
// They all moved the profile's version while guarding nothing, so two of a company's users editing different
// contacts both won, and the second silently overwrote something invisible.
//
// It is safe to require here because the precondition is obtainable: the profile read already issues the
// version as a tag. That check is the rule this project learned the hard way on another resource, where a
// guarded write had no read that could supply its precondition and therefore refused every caller.
//
// The bank-account reveal does not carry it, because it is a read and a read has nothing to lose to a
// concurrent write.
//
// The logo upload and accepting the terms do carry it. The logo is a field of the profile, and the version
// moved on every upload while guarding nothing, so two users uploading different logos both won and the
// second silently replaced the first. Accepting the terms records a version and a timestamp and gates
// submission, so a stale caller re-accepting an older version of the terms over a newer acceptance is the one
// lost update here with a compliance consequence rather than a cosmetic one.
//
//
// THE ONE ROUTE THAT WAS MISSING ITS RESPONSE VERSION
//
// Out of twenty-three writes on this profile, one changed the supplier and answered with no version at all,
// so every client kept asserting the version it read before that write, and the next guarded save on the same
// page was refused with nothing on screen to explain it.
//
// Found by filling in registration as a supplier: save the legal details, choose a currency, save again, and
// the currency is silently gone after a reload.
//
//
// OTHER NOTES
//
// The logo was a dead field for a while: the domain could set one and nothing called it.
//
// Bank accounts are gated on their own narrower permission, held by a supplier's administrator only, never on
// the general profile-edit permission.
//
// Accepting the terms is recorded with its version and a timestamp, and it gates submission alongside profile
// completeness and the required documents.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Concurrency;
using FluentValidation.Results;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Application.Common;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class SupplierEndpoints
{
    public static void MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/suppliers").WithTags("Suppliers");

        group.MapGet("/{referenceCode}", async (
            string referenceCode,
            IGetSupplierHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(referenceCode, ct);

            return result switch
            {
                GetSupplierResult.Found f => Results.Ok(f.Supplier),
                GetSupplierResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithETag()
        .WithName("GetSupplier");

        group.MapGet("/me", async (IGetSupplierHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleOwnAsync(ct);

            return result switch
            {
                GetSupplierResult.Found f => Results.Ok(f.Supplier),
                GetSupplierResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithETag()
        .WithName("GetOwnSupplier");

        group.MapPatch("/{supplierCode}", async (
            string supplierCode,
            UpdateProfileRequest request,
            IValidator<UpdateProfileRequest> validator,
            ISupplierCodeScope codeScope,
            IUpdateProfileHandler handler,
            CancellationToken ct) =>
        {
            if (await codeScope.ResolveOwnAsync(supplierCode, ct) is null) return Results.NotFound();

            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(new UpdateProfileCommand(request.Description, request.Website, request.SupplierGroup, request.CurrencyCode, request.PrimaryContactPhone), ct);

            return SupplierResults.MapProfileResult(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateSupplierProfile");

        group.MapPut("/me/legal-info", async (
            UpdateLegalInfoRequest request,
            IValidator<UpdateLegalInfoRequest> validator,
            IUpdateLegalInfoHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(new UpdateLegalInfoCommand(request.LegalNameAr, request.LegalNameEn, request.RegistrationNumber, request.TaxId, request.SupplierType, request.EstablishedOn), ct);

            return SupplierResults.MapProfileResult(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithFreshETag()
        .WithName("UpdateLegalInfo");

        group.MapPost("/me/logo", async (
            HttpRequest request,
            IUploadLogoHandler handler,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "expected_multipart_form" });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file_required" });

            await using var stream = file.OpenReadStream();
            var result = await handler.HandleAsync(new UploadLogoCommand(stream, file.FileName, file.Length), ct);

            return result switch
            {
                UploadLogoResult.Success s => Results.Ok(s.Supplier),
                UploadLogoResult.NotFoundOrOutOfScope => Results.NotFound(),
                UploadLogoResult.TooLarge => Results.BadRequest(new { error = "file_too_large" }),
                UploadLogoResult.UnsupportedType => Results.BadRequest(new { error = "unsupported_file_type" }),
                UploadLogoResult.ContentMismatch => Results.BadRequest(new { error = "content_type_mismatch" }),
                UploadLogoResult.NotEditable n => Results.Conflict(new { error = n.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UploadLogo")
        .DisableAntiforgery();

        group.MapGet("/me/logo/download-url", async (IGetLogoDownloadUrlHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);
            return result switch
            {
                LogoDownloadUrlResult.Success s => Results.Ok(new { url = s.Url }),
                LogoDownloadUrlResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequireAuthorization()
        .WithName("GetLogoDownloadUrl");

        group.MapPost("/me/representatives", async (
            AddRepresentativeRequest request,
            IValidator<AddRepresentativeRequest> validator,
            IManageRepresentativeHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddRepresentativeCommand(request.FullName, request.Email, request.Phone, request.Position), ct);
            return SupplierResults.MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddRepresentative");

        group.MapPut("/me/representatives/{representativeId:guid}", async (
            Guid representativeId,
            AddRepresentativeRequest request,
            IValidator<AddRepresentativeRequest> validator,
            IManageRepresentativeHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateRepresentativeCommand(representativeId, request.FullName, request.Email, request.Phone, request.Position), ct);
            return SupplierResults.MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateRepresentative");

        group.MapDelete("/me/representatives/{representativeId:guid}", async (Guid representativeId, IManageRepresentativeHandler handler, CancellationToken ct) =>
            SupplierResults.MapMutation(await handler.RemoveAsync(new RemoveRepresentativeCommand(representativeId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveRepresentative");

        group.MapPost("/me/representatives/{representativeId:guid}/set-primary", async (Guid representativeId, IManageRepresentativeHandler handler, CancellationToken ct) =>
            SupplierResults.MapMutation(await handler.SetPrimaryAsync(new SetPrimaryRepresentativeCommand(representativeId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("SetPrimaryRepresentative");

        group.MapPost("/me/addresses", async (
            AddAddressRequest request,
            IValidator<AddAddressRequest> validator,
            IManageAddressHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddAddressCommand(request.Kind, request.Line1, request.Line2, request.City, request.RegionCode, request.Country, request.PostalCode, request.Latitude, request.Longitude), ct);
            return SupplierResults.MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddAddress");

        group.MapPut("/me/addresses/{addressId:guid}", async (
            Guid addressId,
            UpdateAddressRequest request,
            IValidator<UpdateAddressRequest> validator,
            IManageAddressHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateAddressCommand(addressId, request.Kind, request.Line1, request.Line2, request.City, request.RegionCode, request.Country, request.PostalCode, request.Latitude, request.Longitude), ct);
            return SupplierResults.MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateAddress");

        group.MapDelete("/me/addresses/{addressId:guid}", async (Guid addressId, IManageAddressHandler handler, CancellationToken ct) =>
            SupplierResults.MapMutation(await handler.RemoveAsync(new RemoveAddressCommand(addressId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveAddress");

        group.MapPost("/me/contacts", async (
            AddContactRequest request,
            IValidator<AddContactRequest> validator,
            IManageContactHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddContactCommand(request.FullName, request.Email, request.Phone, request.Role), ct);
            return SupplierResults.MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddContact");

        group.MapPut("/me/contacts/{contactId:guid}", async (
            Guid contactId,
            AddContactRequest request,
            IValidator<AddContactRequest> validator,
            IManageContactHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateContactCommand(contactId, request.FullName, request.Email, request.Phone, request.Role), ct);
            return SupplierResults.MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateContact");

        group.MapDelete("/me/contacts/{contactId:guid}", async (Guid contactId, IManageContactHandler handler, CancellationToken ct) =>
            SupplierResults.MapMutation(await handler.RemoveAsync(new RemoveContactCommand(contactId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveContact");

        group.MapPost("/me/branches", async (
            AddBranchRequest request,
            IValidator<AddBranchRequest> validator,
            IManageBranchHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddBranchCommand(request.NameAr, request.NameEn, request.AddressId), ct);
            return SupplierResults.MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddBranch");

        group.MapPut("/me/branches/{branchId:guid}", async (
            Guid branchId,
            UpdateBranchRequest request,
            IValidator<UpdateBranchRequest> validator,
            IManageBranchHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateBranchCommand(branchId, request.NameAr, request.NameEn, request.AddressId, request.IsActive), ct);
            return SupplierResults.MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateBranch");

        group.MapDelete("/me/branches/{branchId:guid}", async (Guid branchId, IManageBranchHandler handler, CancellationToken ct) =>
            SupplierResults.MapMutation(await handler.RemoveAsync(new RemoveBranchCommand(branchId), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveBranch");

        group.MapPost("/me/bank-accounts", async (
            AddBankAccountRequest request,
            IValidator<AddBankAccountRequest> validator,
            IManageBankAccountHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddBankAccountCommand(request.AccountHolderName, request.BankName, request.BranchName, request.AccountNumber, request.SwiftBic, request.CurrencyCode), ct);
            return SupplierResults.MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AddBankAccount");

        group.MapPut("/me/bank-accounts/{bankAccountId:guid}", async (
            Guid bankAccountId,
            UpdateBankAccountRequest request,
            IValidator<UpdateBankAccountRequest> validator,
            IManageBankAccountHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateBankAccountCommand(bankAccountId, request.AccountHolderName, request.BankName, request.BranchName, request.AccountNumber, request.SwiftBic, request.CurrencyCode), ct);
            return SupplierResults.MapMutation(result);
        })
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UpdateBankAccount");

        group.MapDelete("/me/bank-accounts/{bankAccountId:guid}", async (Guid bankAccountId, IManageBankAccountHandler handler, CancellationToken ct) =>
            SupplierResults.MapMutation(await handler.RemoveAsync(new RemoveBankAccountCommand(bankAccountId), ct)))
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("RemoveBankAccount");

        group.MapPost("/me/bank-accounts/{bankAccountId:guid}/set-default", async (Guid bankAccountId, IManageBankAccountHandler handler, CancellationToken ct) =>
            SupplierResults.MapMutation(await handler.SetDefaultAsync(new SetDefaultBankAccountCommand(bankAccountId), ct)))
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("SetDefaultBankAccount");

        group.MapPost("/me/bank-accounts/{bankAccountId:guid}/reveal", async (Guid bankAccountId, IManageBankAccountHandler handler, CancellationToken ct) =>
        {
            var result = await handler.RevealAsync(new RevealBankAccountCommand(bankAccountId), ct);
            return result switch
            {
                RevealBankAccountResult.Success s => Results.Ok(new { accountNumber = s.AccountNumber }),
                RevealBankAccountResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierBankAccountManage)
        .WithName("RevealBankAccount");

        group.MapPost("/me/category-links", async (LinkCategoryRequest request, IManageCategoryLinkHandler handler, CancellationToken ct) =>
            SupplierResults.MapMutation(await handler.LinkAsync(new LinkCategoryCommand(request.CategoryCode), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("LinkCategory");

        group.MapDelete("/me/category-links/{categoryCode}", async (string categoryCode, IManageCategoryLinkHandler handler, CancellationToken ct) =>
            SupplierResults.MapMutation(await handler.UnlinkAsync(new UnlinkCategoryCommand(categoryCode), ct)))
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("UnlinkCategory");

        group.MapPost("/me/accept-terms", async (
            IAcceptTermsHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);

            return result switch
            {
                AcceptTermsResult.Success s => Results.Ok(s.Supplier),
                AcceptTermsResult.NotFoundOrOutOfScope => Results.NotFound(),
                AcceptTermsResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierEdit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("AcceptTerms");

        group.MapPost("/{supplierCode}/onboarding/submit", async (
            string supplierCode,
            ISupplierCodeScope codeScope,
            ISubmitApplicationHandler handler,
            CancellationToken ct) =>
        {
            if (await codeScope.ResolveOwnAsync(supplierCode, ct) is null) return Results.NotFound();

            var result = await handler.HandleAsync(ct);

            return result switch
            {
                SubmitApplicationResult.Success s => Results.Ok(s.Supplier),
                SubmitApplicationResult.NotFoundOrOutOfScope => Results.NotFound(),
                SubmitApplicationResult.Incomplete i => Results.UnprocessableEntity(new { error = "incomplete_profile", missingFields = i.MissingFields }),
                SubmitApplicationResult.InvalidState s => Results.Conflict(new { error = s.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierSubmit)
        .RequireIfMatch()
        .WithFreshETag()
        .WithName("SubmitSupplierApplication");
    }
}
