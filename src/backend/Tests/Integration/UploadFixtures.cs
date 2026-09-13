// The two constants a document upload needs: a seeded document type, and the smallest valid file.
//
// They used to live on one test class, which meant three other suites reached across to that class for them.
// A shared fixture is where a shared constant belongs.

namespace MotsSupplierPortal.Tests.Integration;

public static class UploadFixtures
{
    public static readonly Guid TaxCertificateDocumentTypeId = Guid.Parse("00000000-0000-0000-0000-000000000102");

    public static readonly byte[] MinimalPdfBytes =
        "%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF"u8.ToArray();
}
