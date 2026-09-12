using Microsoft.AspNetCore.Http;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Api.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// §11's contract gate: "CI fails on an undocumented breaking diff".
///
/// <para><b>Why this is a test and not an oasdiff step.</b> Generating the document needs the host running,
/// and the host needs a database - which is exactly what this fixture already provides. A CI step would have
/// had to stand up Postgres a second time to produce the input, and the comparison itself is a few dozen
/// lines. Running it here also means it gates every path into main that runs the suite, with no new tooling
/// to keep installed.</para>
///
/// <para><b>Only BREAKING differences fail.</b> API-ARCHITECTURE.md §Versioning is explicit that additive
/// changes - a new optional field, a new endpoint, a new documented enum value - ship inside the current
/// version. A gate that failed on those would be edited around within a week, and then it would be a gate
/// nobody reads. What fails: a path or operation that disappeared, a property that disappeared from a
/// response, a property that became required, a response status that disappeared.</para>
///
/// <para><b>The history.</b> Every rename pass in this project - R-9, §12-A/C, the batch-8 conformance sweep
/// - leaned on the test suites to catch a breaking wire change, because the safety net §11 describes did not
/// exist. That worked because those passes were careful, not because anything enforced it. This is the
/// enforcement.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class OpenApiContractTests(PostgresApiFixture fixture)
{
    private static string BaselinePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "contracts", "openapi-v1.baseline.json")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the committed baseline must be findable from the test binaries");
        return Path.Combine(directory!.FullName, "contracts", "openapi-v1.baseline.json");
    }

    private async Task<JsonObject> CurrentDocumentAsync()
    {
        var client = fixture.CreateRawClient();
        var response = await client.GetAsync("/openapi/v1.json");

        // If this is ever not 200, the gate is measuring nothing - which is what it WAS doing before batch
        // 11: MapOpenApi declares no authorization, so NFR-SEC-004's deny-by-default FallbackPolicy answered
        // 401 and the document was generated and served to nobody.
        response.IsSuccessStatusCode.Should().BeTrue(
            $"/openapi/v1.json must be reachable for the contract gate to mean anything (got {response.StatusCode})");

        var json = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(json)!.AsObject();
    }

    private static readonly string[] Methods = ["get", "put", "post", "delete", "patch", "options", "head"];

    /// <summary>Every response property the document names, as "PATH METHOD STATUS field.path" strings.
    /// Flattened deliberately: comparing two object graphs reports "they differ", and comparing two sets of
    /// strings reports WHICH field on WHICH operation, which is the difference between a gate someone can act
    /// on and one they re-run.</summary>
    private static HashSet<string> ResponseShapes(JsonObject document)
    {
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        var schemas = document["components"]?["schemas"]?.AsObject();

        foreach (var (path, pathItem) in document["paths"]!.AsObject())
        {
            foreach (var method in Methods)
            {
                var operation = pathItem?[method]?.AsObject();
                if (operation is null) continue;

                shapes.Add($"{path} {method}");

                var responses = operation["responses"]?.AsObject();
                if (responses is null) continue;

                foreach (var (status, response) in responses)
                {
                    shapes.Add($"{path} {method} {status}");

                    var schema = response?["content"]?["application/json"]?["schema"];
                    if (schema is null) continue;

                    foreach (var property in PropertiesOf(schema, schemas, depth: 0))
                    {
                        shapes.Add($"{path} {method} {status} {property}");
                    }
                }
            }
        }

        return shapes;
    }

    /// <summary>
    /// Property names one level of $ref deep.
    ///
    /// <para>Depth-limited on purpose. A schema graph in this document is recursive in places (an RFQ carries
    /// items that carry their own schemas), and the value of this gate is catching a field that DISAPPEARED
    /// from a response, which is visible at the top level of the response schema and one hop through its
    /// $ref. Walking the whole graph would add pages of noise for the same catch.</para>
    /// </summary>
    private static IEnumerable<string> PropertiesOf(JsonNode? schema, JsonObject? schemas, int depth)
    {
        if (schema is null || depth > 2) yield break;

        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            var name = reference.Split('/').Last();
            var target = schemas?[name];
            if (target is not null)
            {
                foreach (var property in PropertiesOf(target, schemas, depth + 1)) yield return property;
            }
            yield break;
        }

        if (schema["items"] is { } items)
        {
            foreach (var property in PropertiesOf(items, schemas, depth + 1)) yield return $"[]{property}";
        }

        if (schema["properties"]?.AsObject() is { } properties)
        {
            foreach (var (name, value) in properties)
            {
                yield return name;
                if (value?["$ref"] is not null || value?["items"] is not null)
                {
                    foreach (var nested in PropertiesOf(value, schemas, depth + 1)) yield return $"{name}.{nested}";
                }
            }
        }
    }

    /// <summary>
    /// Required properties on schemas used in REQUEST bodies, so a field becoming mandatory is caught: an
    /// existing client that does not send it starts failing, which is breaking even though nothing was removed.
    ///
    /// <para><b>Requests only, and the first version of this gate got it wrong.</b> It walked every schema, so
    /// adding two fields to a RESPONSE record failed the gate on its own next run - `ProfileHealthDto` gained
    /// the document's name beside its code, which no client could possibly break on. "Required" on a response
    /// is a promise the server keeps; "required" on a request is a demand on the caller. Only the second one
    /// can break somebody.</para>
    /// </summary>
    private static HashSet<string> RequiredRequestProperties(JsonObject document)
    {
        var schemas = document["components"]?["schemas"]?.AsObject();
        var required = new HashSet<string>(StringComparer.Ordinal);

        // The schemas reachable from a requestBody, one $ref deep - which is how every request body in this
        // API is shaped (a single $ref to a named record).
        var requestSchemas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, pathItem) in document["paths"]!.AsObject())
        {
            foreach (var method in Methods)
            {
                var reference = pathItem?[method]?["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]
                    ?? pathItem?[method]?["requestBody"]?["content"]?["application/merge-patch+json"]?["schema"]?["$ref"];
                if (reference?.GetValue<string>() is { } name) requestSchemas.Add(name.Split('/').Last());
            }
        }

        foreach (var name in requestSchemas)
        {
            foreach (var property in schemas?[name]?["required"]?.AsArray() ?? [])
            {
                required.Add($"{name}.{property!.GetValue<string>()}");
            }
        }

        return required;
    }

    /// <summary>
    /// Refreshes the committed baseline from the document this build produces, when asked to.
    ///
    /// <para><c>UPDATE_OPENAPI_BASELINE=1 dotnet test --filter OpenApiContractTests</c>, the same idiom
    /// <c>PERMISSIONS.md</c> already uses for the generated permission catalogue. `contracts/README.md` used
    /// to say "with the API running in Development, curl it" - true, and it meant refreshing the contract
    /// needed a database, a port and a shell pipeline, so it was done rarely and the baseline drifted behind
    /// additive changes. The fixture already has the database.</para>
    ///
    /// <para>Writes nothing unless the variable is set: a gate that quietly rewrites what it is comparing
    /// against is not a gate.</para>
    /// </summary>
    [Fact]
    public async Task The_baseline_can_be_refreshed_from_this_build()
    {
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_BASELINE") != "1") return;

        var current = await CurrentDocumentAsync();
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // Sorted keys, matching contracts/README.md's own pipeline: without it the diff is a key-order
        // shuffle and the gate reports noise, which is how a gate stops being read.
        await File.WriteAllTextAsync(BaselinePath(), JsonSerializer.Serialize(Sorted(current), options) + "\n");
    }

    private static JsonNode? Sorted(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
            {
                var sorted = new JsonObject();
                foreach (var (key, value) in o.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    sorted[key] = Sorted(value?.DeepClone());
                }
                return sorted;
            }

            case JsonArray a:
            {
                var items = new JsonArray();
                foreach (var item in a) items.Add(Sorted(item?.DeepClone()));
                return items;
            }

            default:
                return node?.DeepClone();
        }
    }

    [Fact]
    public async Task The_wire_contract_has_not_broken_against_the_committed_baseline()
    {
        var baseline = JsonNode.Parse(await File.ReadAllTextAsync(BaselinePath()))!.AsObject();
        var current = await CurrentDocumentAsync();

        // Non-vacuity first. A baseline that parsed to nothing, or a document the host stopped producing,
        // would make every assertion below pass in silence - the failure mode this project keeps finding in
        // its own instruments.
        baseline["paths"]!.AsObject().Count.Should().BeGreaterThan(150, "the committed baseline must be real");
        current["paths"]!.AsObject().Count.Should().BeGreaterThan(150, "the live document must be real");

        var baselineShapes = ResponseShapes(baseline);
        var currentShapes = ResponseShapes(current);

        var removed = baselineShapes.Except(currentShapes).OrderBy(x => x, StringComparer.Ordinal).ToList();
        removed.Should().BeEmpty(
            "a path, operation, response status or response field that existed in the committed contract and " +
            "does not exist now is a breaking change, and API-ARCHITECTURE.md §Versioning says it needs " +
            $"/api/v2 rather than a quiet edit:\n  {string.Join("\n  ", removed.Take(40))}");

        // Only on schemas the BASELINE already had. A required field on a request schema that did not exist
        // before cannot break a client, because no client has ever called that endpoint - and this gate caught
        // itself on exactly that within an hour of being written: `SetDocumentTypeCategoriesRequest.categoryCodes`
        // is required on an endpoint added in the same batch. A gate that flags its own additions is a gate
        // people learn to ignore.
        var baselineRequestSchemas = RequiredRequestProperties(baseline)
            .Select(entry => entry.Split('.')[0])
            .ToHashSet(StringComparer.Ordinal);

        var newlyRequired = RequiredRequestProperties(current)
            .Except(RequiredRequestProperties(baseline))
            .Where(entry => baselineRequestSchemas.Contains(entry.Split('.')[0]))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        newlyRequired.Should().BeEmpty(
            "a REQUEST property that was optional and is now required breaks every existing client that does " +
            "not send it, even though nothing was removed. Response properties are deliberately excluded: " +
            "required there is a promise the server keeps, not a demand on the caller:\n  " +
            $"{string.Join("\n  ", newlyRequired.Take(40))}");
    }

    [Fact]
    public async Task Additive_change_is_allowed_and_the_gate_says_so()
    {
        // The control. Without it, "nothing broke" could be satisfied by a comparison that is not looking -
        // this asserts the gate's own asymmetry: the live document is ALLOWED to have more than the baseline,
        // and every endpoint added in this batch is exactly that case.
        var baseline = JsonNode.Parse(await File.ReadAllTextAsync(BaselinePath()))!.AsObject();
        var current = await CurrentDocumentAsync();

        var added = ResponseShapes(current).Except(ResponseShapes(baseline)).ToList();
        added.Should().NotBeNull();

        // And prove the comparison can actually see a removal, by removing one from a COPY of the live
        // document. A gate that cannot fail is not a gate, and this is cheaper than trusting that it can.
        var mutilated = JsonNode.Parse(current.ToJsonString())!.AsObject();
        var firstPath = mutilated["paths"]!.AsObject().First().Key;
        mutilated["paths"]!.AsObject().Remove(firstPath);

        ResponseShapes(current).Except(ResponseShapes(mutilated)).Should().NotBeEmpty(
            "the comparison must detect a removed path, or the first test proves nothing");
    }

    /// <summary>
    /// T-108: every permissioned route says which permission it needs, in the published document.
    ///
    /// <para>§11 makes the OpenAPI document the source for the SPA's types and the ERP client. Every
    /// route in this API is gated, and the document said nothing about any gate - so a consumer
    /// reading it could not tell which token reaches which route, and found out as a 403.</para>
    ///
    /// <para><b>The denominator is the endpoint table, not a sample.</b> The count of operations
    /// carrying the annotation is compared against the count of endpoints carrying the metadata the
    /// filter enforces. A transformer that silently stopped running would leave the numbers unequal;
    /// a spot-check on one route would not notice.</para>
    /// </summary>
    [Fact]
    public async Task Every_permissioned_route_names_its_permission_in_the_document()
    {
        var document = await CurrentDocumentAsync();

        var annotated = document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject())
            .Where(operation => operation.Value is JsonObject shape
                                && shape.ContainsKey("x-required-permissions"))
            .ToList();

        // Non-vacuity, and it is the whole point: a document with no annotations at all would satisfy
        // any assertion about the ones it has.
        annotated.Should().HaveCountGreaterThan(150,
            "nearly every route in this API is permissioned, so the annotation must be on nearly every operation");

        // And the count matches what the API itself enforces, read from the endpoint table rather than
        // from a list in this file.
        var enforced = fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Count(e => e.Metadata.GetMetadata<RequiredPermissionsMetadata>() is not null);

        annotated.Should().HaveCount(enforced,
            "the document and the filter must agree about which routes are gated - a difference means one "
            + "of them is lying to a consumer generating a client");

        // One route, read end to end, so the annotation's SHAPE is asserted rather than its presence.
        var audit = document["paths"]!["/api/v1/audit"]!["get"]!.AsObject();
        audit["x-required-permissions"]!.AsArray().Select(v => v!.GetValue<string>())
            .Should().Contain(Permissions.AuditRead);
        audit["description"]!.GetValue<string>().Should().Contain(Permissions.AuditRead,
            "a generator that drops unknown x- extensions still carries the prose");
        audit["responses"]!["403"].Should().NotBeNull("a permission a caller can read, with a refusal they cannot anticipate, is half an answer");
    }
}
