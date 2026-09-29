// Turning the ERP's billing address into a portal address, or saying plainly why it cannot become one.
//
// A PORTAL ADDRESS NEEDS A GOVERNORATE, and the ERP has no such field. Its city field is what people typed, and at
// Seven Gates that is "Damascus" for nearly everybody - including suppliers whose street line says "ريف دمشق -
// جرمانا" or "حلب العرقوب". So the street is read first, and a governorate named there wins over the city: the line
// is where somebody wrote the specific place, and the city field is where the ERP's default sat. Only when the street
// names none does the city decide.
//
// NAMES ARE MATCHED AS WHOLE WORDS, never as fragments. "شارع الحمصي" is Homsi Street in Jaramana, not Homs, and a
// substring search would have filed that supplier under the wrong governorate with nothing to show it had guessed.
// Where two names match, the one written first wins, which is how "ريف دمشق - جرمانا" reads as Rif Dimashq and not
// as the "دمشق" inside it.
//
// A ROAD NAMED AFTER A CITY IS NOT THAT CITY. "اوتوستراد حمص" is the Homs highway, which starts in Damascus, and
// "طريق درعا" is a road in Damascus too; a governorate right after a word for road or highway is skipped, and the next
// match - or the city - decides. Spellings are matched as people type them, with ه for ة and "Rif Damascus" for
// ريف دمشق, because a spelling the list does not know silently falls back to the city.
//
// NOTHING IS GUESSED BEYOND THAT. A supplier in Jarmana whose street says only "جرمانا" is filed under whatever the
// city field says, because a table of every suburb would be a guess dressed as data. And an address with no street,
// no city, or outside Syria is not imported at all - the portal's governorates are Syria's, and inventing one would
// put the supplier on the wrong map - with a note saying which of those it was.
//
// The codes are the ones RegionConfiguration seeds, and SyrianGovernorateCoverageTests fails if the two lists differ.

namespace MotsSupplierPortal.Application.Integration;

using System.Text.RegularExpressions;

public sealed record MappedErpAddress(string Line1, string? Line2, string City, string RegionCode, string Country);

public sealed record ErpAddressMapping(MappedErpAddress? Address, string Note);

public static partial class ErpAddressMapper
{
    public const string SyriaCode = "SY";

    public static readonly IReadOnlyList<(string Code, string[] Names)> Governorates =
    [
        ("RDM", ["ريف دمشق", "rif dimashq", "rif damascus", "reef damascus", "rural damascus", "damascus countryside"]),
        ("DIM", ["دمشق", "damascus"]),
        ("ALP", ["حلب", "aleppo"]),
        ("LAT", ["اللاذقية", "اللاذقيه", "latakia", "lattakia"]),
        ("HOM", ["حمص", "homs"]),
        ("DAR", ["درعا", "daraa"]),
        ("DEZ", ["دير الزور", "deir ez-zor", "deir ezzor"]),
        ("HAS", ["الحسكة", "الحسكه", "hasakah", "al-hasakah", "hasakeh"]),
        ("HMA", ["حماة", "حماه", "hama"]),
        ("IDL", ["إدلب", "ادلب", "idlib"]),
        ("QUN", ["القنيطرة", "القنيطره", "quneitra"]),
        ("RAQ", ["الرقة", "الرقه", "raqqa"]),
        ("SUW", ["السويداء", "suwayda", "as-suwayda", "sweida"]),
        ("TAR", ["طرطوس", "tartus", "tartous"]),
    ];

    public static ErpAddressMapping Map(ErpSupplierAddress? address)
    {
        if (address is null)
        {
            return new ErpAddressMapping(null, "No address in the ERP; city, governorate and coordinates stay empty.");
        }

        var country = address.Country?.Trim();
        if (country is not null && !string.Equals(country, "Syria", StringComparison.OrdinalIgnoreCase))
        {
            return new ErpAddressMapping(
                null,
                $"The ERP address is in {country}; the portal records Syrian governorates only, so it was not imported.");
        }

        var line1 = Clean(address.Line1);
        var city = Clean(address.City);
        if (line1 is null || city is null)
        {
            return new ErpAddressMapping(
                null,
                "The ERP address has no " + (line1 is null ? "street" : "city") + ", so it was not imported.");
        }

        var line2 = Clean(address.Line2);
        var region = GovernorateIn(line1 + " " + line2) ?? GovernorateIn(city);
        if (region is null)
        {
            return new ErpAddressMapping(
                null,
                $"The ERP address ({line1}, {city}) names no Syrian governorate, so it was not imported.");
        }

        var cut = new List<string>();
        var mapped = new MappedErpAddress(
            ErpFieldLimits.Cut(line1, ErpFieldLimits.AddressLine, "street", cut)!,
            ErpFieldLimits.Cut(line2, ErpFieldLimits.AddressLine, "second street line", cut),
            ErpFieldLimits.Cut(city, ErpFieldLimits.City, "city", cut)!,
            region,
            SyriaCode);

        return new ErpAddressMapping(
            mapped,
            string.Join(
                " ",
                [$"Address imported from the ERP; governorate {region}. No map coordinates - the ERP has none.", .. cut]));
    }

    private static readonly HashSet<string> RoadWords =
    [
        "طريق", "اوتوستراد", "أوتوستراد", "اتوستراد", "أتوستراد", "اوتستراد", "road", "highway", "autostrad",
    ];

    public static string? GovernorateIn(string? text)
    {
        var words = Words(text);
        if (words.Count == 0) return null;

        (string Code, int At, int Length)? best = null;

        foreach (var (code, names) in Governorates)
        {
            foreach (var name in names)
            {
                var wanted = Words(name);
                var at = IndexOf(words, wanted);
                if (at < 0) continue;

                if (best is null || at < best.Value.At || (at == best.Value.At && wanted.Count > best.Value.Length))
                {
                    best = (code, at, wanted.Count);
                }
            }
        }

        return best?.Code;
    }

    private static int IndexOf(IReadOnlyList<string> words, IReadOnlyList<string> wanted)
    {
        for (var i = 0; i + wanted.Count <= words.Count; i++)
        {
            if (i > 0 && RoadWords.Contains(words[i - 1])) continue;

            var all = true;
            for (var j = 0; j < wanted.Count && all; j++)
            {
                all = words[i + j] == wanted[j];
            }

            if (all) return i;
        }

        return -1;
    }

    private static List<string> Words(string? text) =>
        text is null ? [] : [.. WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value)];

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
