// Reading a timestamp the ERP sent without a time zone.
//
// THE WIRE FORMAT CARRIES NO OFFSET. The ERP sends "2026-08-17 03:15:00" and means its own server's local time.
// Every other timestamp crossing this product's boundaries carries a zone, so the natural thing - hand it to the
// framework and let it parse - reads it as the LOCAL time of whatever machine the portal happens to run on, and
// a container running in UTC then records a supplier as created three hours before they were.
//
// NOTHING ABOUT THAT FAILURE WOULD BE VISIBLE. The value parses and the only symptom is a timestamp quietly wrong by a
// fixed amount - and a modified-since filter built on a drifted one would skip rows or re-send them forever. The
// import reads neither timestamp yet (see ErpOptions), so this is kept right for the day something does.
//
// SO THE ZONE IS SUPPLIED, NOT INFERRED, and an unparseable zone identifier throws here rather than falling back
// to UTC. A fallback would turn a configuration mistake into permanently skewed data, which is the same failure
// with the alarm disconnected.
//
// A VALUE THAT ALREADY CARRIES A ZONE IS HONOURED. Frappe's v2 endpoints and some fields do emit an offset, and
// re-interpreting an explicit +03:00 as if it were local time would corrupt the one case that arrives correct.
//
// A TIME SENT TO THE ERP GOES THE SAME WAY BACK. A filter on "creation" is compared with the ERP's own local
// timestamps, so Format writes the moment in the server's zone and wire format, with no offset the ERP would not
// read. Sent in UTC, "created since" would be three hours out in Damascus and miss the record it is looking for.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Globalization;

public static class ErpServerTime
{
    private const string WireFormat = "yyyy-MM-dd HH:mm:ss";

    public static DateTimeOffset Parse(string value, TimeZoneInfo serverZone)
    {
        if (!TryParse(value, serverZone, out var parsed))
        {
            throw new FormatException($"'{value}' is not a timestamp the ERP sends.");
        }

        return parsed;
    }

    public static bool TryParse(string? value, TimeZoneInfo serverZone, out DateTimeOffset parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();

        if (DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var alreadyZoned)
            && CarriesAZone(trimmed))
        {
            parsed = alreadyZoned;
            return true;
        }

        if (!DateTime.TryParseExact(
                trimmed,
                [WireFormat, "yyyy-MM-dd HH:mm:ss.FFFFFF", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local))
        {
            return false;
        }

        var offset = serverZone.GetUtcOffset(local);
        parsed = new DateTimeOffset(local, offset);
        return true;
    }

    private static bool CarriesAZone(string value) =>
        value.EndsWith('Z') || value.Contains('+') || value.LastIndexOf('-') > 7;

    public static TimeZoneInfo Zone(string identifier) => TimeZoneInfo.FindSystemTimeZoneById(identifier);

    public static string Format(DateTimeOffset value, TimeZoneInfo serverZone) =>
        TimeZoneInfo.ConvertTime(value, serverZone).ToString(WireFormat, CultureInfo.InvariantCulture);
}
