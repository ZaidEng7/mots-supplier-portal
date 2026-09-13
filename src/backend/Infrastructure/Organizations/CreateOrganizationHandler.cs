using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

namespace MotsSupplierPortal.Infrastructure.Organizations;

public sealed class CreateOrganizationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ICreateOrganizationHandler
{
    public async Task<OrganizationMutationResult> HandleAsync(CreateOrganizationCommand command, CancellationToken ct)
    {
        // T-055: the same allocator every other reference code uses, on its own connection - see
        // ReferenceCodeGenerator for why it is not inside the caller's transaction.
        var referenceCode = await ReferenceCodeGenerator.NextCodeAsync(db, "ORG", ct);

        Organization org;
        try
        {
            org = Organization.Create(referenceCode, command.LegalNameAr, command.LegalNameEn, command.OrganizationType, command.ContactEmail, command.ContactPhone);
        }
        catch (DomainException ex)
        {
            return new OrganizationMutationResult.InvalidState(ex.Message);
        }

        db.Set<Organization>().Add(org);
        await auditLogger.LogAsync("Organization", org.Id, "organization_created", scope.UserId, reason: command.LegalNameEn, ct: ct);
        await db.SaveChangesAsync(ct);
        return new OrganizationMutationResult.Success(OrganizationDtoMapper.ToDto(org));
    }
}
