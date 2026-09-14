// Allow-list file-type validation: the leading bytes must match the declared type.
//
// The client's declared content type and its extension are not trusted; the bytes decide.
//
// Documents and common images only, which are the written architecture's own examples. Office formats are
// deliberately absent: admitting them without a macro-stripping and archive-bomb defence would buy a false sense
// of coverage, and it is flagged as a follow-up rather than quietly included.
//
// The size cap is the written example's own figure.

namespace MotsSupplierPortal.Infrastructure.Storage;

public static class FileTypeSniffer
{
    public static readonly IReadOnlyDictionary<string, string> AllowedExtensionToContentType = new Dictionary<string, string>
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
    };

    public const long MaxSizeBytes = 20 * 1024 * 1024; // 20 MB per docs' example cap.

    public static bool TryDetectContentType(byte[] header, out string? detectedContentType)
    {
        detectedContentType = null;

        if (header.Length >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
        {
            detectedContentType = "application/pdf"; // %PDF
            return true;
        }
        if (header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            detectedContentType = "image/png";
            return true;
        }
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            detectedContentType = "image/jpeg";
            return true;
        }
        return false;
    }
}
