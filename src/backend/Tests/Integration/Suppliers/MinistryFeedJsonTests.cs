// The JSON representation of both ministry feeds, and the one property that keeps it honest.
//
// THE ASSERTION THIS FILE EXISTS FOR IS THAT THE TWO REPRESENTATIONS AGREE. A feed served as CSV to a person
// and as JSON to a nightly job is two answers to one question, and the failure mode is not that one breaks -
// it is that they quietly stop matching, so the file somebody checks by hand and the data the dashboard loads
// describe different registries. Nothing else in this repository would notice. So the same request is made
// twice, once for each representation, and the rows are compared value by value.
//
// THE COLUMN NAMES ARE COMPARED TOO, as a set and in order. Their loader reads by name, so a JSON property
// that lost its explicit name and came back as supplierID would be a working endpoint serving a contract
// nobody asked for. The web defaults lower that first letter automatically, which is exactly the kind of
// change that arrives by accident.
//
// CSV IS STILL WHAT ANSWERS WHEN NOBODY ASKS. A browser sends */* and a person clicking a link must not
// receive JSON; only an explicit application/json switches representation. Both directions are asserted,
// because a negotiation that always returns one thing passes half the tests that matter.
//
// THE TYPES ARE ASSERTED AS TYPES, not as text. A coordinate serialised as "33.5131" is accepted by most
// loaders and plotted by none, and it is indistinguishable from the correct answer in any assertion that reads
// the value as a string.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class MinistryFeedJsonTests(PostgresApiFixture fixture)
{
    // The suite's fixture seeds nothing, and this class cannot borrow suppliers other classes happen to have
    // created: run on its own it would find an empty registry, and run after them it would pass for reasons
    // that have nothing to do with it. So it plants its own supplier, with a position, which is also what the
    // coordinate assertion needs.
    private static async Task SeedSupplierWithAPositionAsync(PostgresApiFixture fixture)
    {
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(
            fixture, $"Feed JSON Co {Guid.NewGuid():N}"[..24]);

        var address = await supplier.PostAsJsonAsync("/api/v1/suppliers/me/addresses", new
        {
            kind = "HeadOffice", line1 = "12 Al-Thawra Street", line2 = (string?)null, city = "Damascus",
            regionCode = "DAM", country = "Syria", postalCode = "0100",
            latitude = 33.5131, longitude = 36.2925,
        });
        address.EnsureSuccessStatusCode();
    }

    // EVERY row of the feed, following the cursor to the end - not the first page. These tests compare the two
    // representations, and the CSV is unpaged, so reading one page of JSON and calling it the feed compares a
    // page against a file. It passes while the suite is small and fails the moment the registry outgrows one
    // page, which is precisely the kind of order-dependent failure that arrives in continuous integration and
    // not on the machine that wrote it.
    private static async Task<List<JsonElement>> JsonFeedRowsAsync(HttpClient client, string route)
    {
        List<JsonElement> rows = [];
        string? cursor = null;
        var separator = route.Contains('?') ? "&" : "?";

        while (true)
        {
            var url = route + separator + $"limit={FeedPage.MaxLimit}"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var envelope = await JsonEnvelopeAsync(client, url);

            rows.AddRange(envelope.GetProperty("data").EnumerateArray().Select(r => r.Clone()));

            var pagination = envelope.GetProperty("pagination");
            if (!pagination.GetProperty("hasMore").GetBoolean()) return rows;

            cursor = pagination.GetProperty("nextCursor").GetString();
        }
    }

    private static async Task<JsonElement> JsonEnvelopeAsync(HttpClient client, string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<List<string[]>> CsvFeedAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        response.EnsureSuccessStatusCode();

        var text = (await response.Content.ReadAsStringAsync()).TrimStart('﻿');
        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(SplitCsvLine)];
    }

    // The feed quotes a field only when it has to, so a naive split on commas would work until a supplier's
    // address contained one - which is most of them.
    private static string[] SplitCsvLine(string line)
    {
        List<string> cells = [];
        var current = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { cells.Add(current.ToString()); current.Clear(); }
            else if (c != '\r') current.Append(c);
        }

        cells.Add(current.ToString());
        return [.. cells];
    }

    [Fact]
    public async Task The_supplier_feed_answers_json_with_the_ministrys_own_column_names()
    {
        await SeedSupplierWithAPositionAsync(fixture);
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var json = await JsonFeedRowsAsync(client, "/api/v1/feeds/suppliers");

        json.Should().NotBeEmpty("this class seeds a supplier of its own");

        var names = json[0].EnumerateObject().Select(p => p.Name).ToList();
        names.Should().Equal(MinistrySupplierFeedCsv.Columns,
            "their loader reads by name, and the web defaults would have lowered SupplierID to supplierID "
            + "without anything failing");
    }

    [Fact]
    public async Task The_supplier_feed_says_the_same_thing_in_both_representations()
    {
        await SeedSupplierWithAPositionAsync(fixture);
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var json = await JsonFeedRowsAsync(client, "/api/v1/feeds/suppliers");
        var csv = await CsvFeedAsync(client, "/api/v1/feeds/suppliers");

        csv[0].Should().Equal(MinistrySupplierFeedCsv.Columns);
        (csv.Count - 1).Should().Be(json.Count, "both representations carry every supplier");

        for (var row = 0; row < json.Count; row++)
        {
            var cells = csv[row + 1];
            var element = json[row];

            for (var column = 0; column < MinistrySupplierFeedCsv.Columns.Count; column++)
            {
                var name = MinistrySupplierFeedCsv.Columns[column];
                var fromCsv = cells[column];
                var fromJson = Rendered(element.GetProperty(name));

                // LastModified is the one value the two are meant to write differently. Their requirements ask
                // for datetimes with a time zone, which CSV cannot express and JSON can, so the JSON carries a
                // trailing Z and the CSV keeps the shape already handed over. The instant must still be the
                // same instant, which is what is compared here rather than the text.
                if (name == "LastModified")
                {
                    DateTimeOffset.Parse(fromJson, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal)
                        .Should().Be(DateTimeOffset.Parse(fromCsv, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
                            $"row {row} column {name} must name the same instant in both representations");
                    continue;
                }

                fromJson.Should().Be(fromCsv,
                    $"row {row} column {name} must read the same however it is asked for");
            }
        }
    }

    // What the CSV would have written for the value JSON carries. Only the shapes the feed actually produces
    // are handled, so a type appearing here that nobody expected fails loudly rather than being coerced.
    private static string Rendered(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => string.Empty,
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number when value.TryGetInt32(out var whole) => whole.ToString(System.Globalization.CultureInfo.InvariantCulture),
        JsonValueKind.Number => value.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"The feed produced a {value.ValueKind}, which nothing formats."),
    };

    [Fact]
    public async Task A_caller_that_asks_for_nothing_in_particular_still_gets_the_file()
    {
        await SeedSupplierWithAPositionAsync(fixture);
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        using var response = await client.GetAsync("/api/v1/feeds/suppliers");

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv",
            "a person clicking a link must not suddenly be handed JSON");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be(MinistrySupplierFeedCsv.FileName);
    }

    [Fact]
    public async Task A_browsers_accept_header_is_not_a_request_for_json()
    {
        await SeedSupplierWithAPositionAsync(fixture);
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/feeds/suppliers");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));

        using var response = await client.SendAsync(request);

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv",
            "*/* is what every browser sends, so honouring it would flip the default for every human caller");
    }

    [Fact]
    public async Task Coordinates_are_numbers_rather_than_quoted_strings()
    {
        await SeedSupplierWithAPositionAsync(fixture);
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var json = await JsonFeedRowsAsync(client, "/api/v1/feeds/suppliers");

        var placed = json
            .FirstOrDefault(row => row.GetProperty("Latitude").ValueKind is not JsonValueKind.Null);

        placed.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "this assertion needs a supplier with a position, and the suite creates several");
        placed.GetProperty("Latitude").ValueKind.Should().Be(JsonValueKind.Number);
        placed.GetProperty("Longitude").ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public async Task The_rfq_feed_answers_json_with_the_ministrys_own_column_names()
    {
        await SeedSupplierWithAPositionAsync(fixture);
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var json = await JsonFeedRowsAsync(client, "/api/v1/feeds/rfqs");

        if (json.Count == 0) return;

        var names = json[0].EnumerateObject().Select(p => p.Name).ToList();
        names.Should().Equal(MinistryRfqFeedCsv.Columns);
    }

    [Fact]
    public async Task The_rfq_feed_says_the_same_thing_in_both_representations()
    {
        await SeedSupplierWithAPositionAsync(fixture);
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var json = await JsonFeedRowsAsync(client, "/api/v1/feeds/rfqs");
        var csv = await CsvFeedAsync(client, "/api/v1/feeds/rfqs");

        csv[0].Should().Equal(MinistryRfqFeedCsv.Columns);
        (csv.Count - 1).Should().Be(json.Count);

        for (var row = 0; row < json.Count; row++)
        {
            var cells = csv[row + 1];
            var element = json[row];

            // The total is the one value the two representations are allowed to write differently: the CSV
            // pads it to two decimal places because their sheet declares decimal(14,2) and CSV has one type,
            // while JSON carries the number itself. Compared as numbers, they must still agree.
            for (var column = 0; column < MinistryRfqFeedCsv.Columns.Count; column++)
            {
                var name = MinistryRfqFeedCsv.Columns[column];
                var fromCsv = cells[column];
                var value = element.GetProperty(name);

                if (name == "QuotationTotal" && value.ValueKind is JsonValueKind.Number)
                {
                    value.GetDecimal().Should().Be(decimal.Parse(fromCsv, System.Globalization.CultureInfo.InvariantCulture));
                    continue;
                }

                Rendered(value).Should().Be(fromCsv, $"row {row} column {name}");
            }
        }
    }
}
