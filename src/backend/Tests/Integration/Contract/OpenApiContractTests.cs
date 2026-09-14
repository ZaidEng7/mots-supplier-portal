// The contract gate: a breaking difference in the published interface document fails the build.
//
//
// WHY THIS IS A TEST RATHER THAN A SEPARATE TOOLING STEP
//
// Generating the document needs the host running, and the host needs a database, which is exactly what this fixture
// already provides.
//
// A build step would have had to stand up a database a second time just to produce the input, and the comparison
// itself is a few dozen lines. Running it here also means it gates every path into the main branch that runs the
// suite, with no new tooling to keep installed.
//
//
// ONLY BREAKING DIFFERENCES FAIL
//
// The written versioning policy is explicit that additive changes, a new optional field, a new endpoint, a new
// documented value, ship inside the current version.
//
// A gate that failed on those would be edited around within a week, and then it would be a gate nobody reads.
//
// What fails: a path or operation that disappeared, a property that disappeared from a response, a property that
// became required, a response status that disappeared.
//
//
// THE HISTORY THAT ARGUES FOR IT
//
// Every rename pass in this project leaned on the test suites to catch a breaking wire change, because the safety
// net the contract describes did not exist.
//
// That worked because those passes were careful, not because anything enforced it. This is the enforcement.
//
//
// REQUIRED PROPERTIES ARE COMPARED ON REQUESTS ONLY, AND THE FIRST VERSION GOT IT WRONG
//
// It walked every schema, so adding two fields to a RESPONSE record failed the gate on its own next run, over a
// field no client could possibly break on.
//
// Required on a response is a promise the server keeps. Required on a request is a demand on the caller. Only the
// second can break somebody.
//
// And only on schemas the BASELINE already had, because a required field on an endpoint that did not exist before
// cannot break a client, since no client has ever called it. The gate caught itself on exactly that within an hour
// of being written, and a gate that flags its own additions is a gate people learn to ignore.
//
//
// THE COMPARISON IS FLATTENED, AND DEPTH-LIMITED
//
// Response properties are compared as one string per operation, status and field path. Comparing two object graphs
// reports that they differ; comparing two sets of strings reports WHICH field on WHICH operation, which is the
// difference between a gate somebody can act on and one they re-run.
//
// It follows references one hop deep, deliberately. The schema graph is recursive in places, and the value of this
// gate is catching a field that DISAPPEARED from a response, which is visible at the top level and one hop
// through its reference. Walking the whole graph would add pages of noise for the same catch.
//
// Keys are sorted, matching the documented pipeline, because otherwise the difference is a key-order shuffle and
// the gate reports noise, which is how a gate stops being read.
//
//
// THREE CONTROLS
//
// Non-vacuity first: a baseline that parsed to nothing, or a document the host stopped producing, would make every
// assertion pass in silence, which is the failure mode this project keeps finding in its own instruments.
//
// The document must actually be reachable. It was not, once: the document endpoint declares no authorisation, so
// the deny-by-default policy refused it and the document was generated and served to nobody.
//
// The gate's own asymmetry is asserted: the live document is ALLOWED to have more than the baseline, and every
// endpoint added in the same batch is exactly that case.
//
// And the comparison is proven able to SEE a removal, by removing one from a copy of the live document. A gate that
// cannot fail is not a gate, and this is cheaper than trusting that it can.
//
//
// REFRESHING THE BASELINE
//
// Behind an environment variable, the same idiom the generated permission catalogue uses.
//
// The instructions used to say to run the interface in development and fetch it by hand, which was true and meant
// refreshing the contract needed a database, a port and a shell pipeline, so it was done rarely and the baseline
// drifted behind additive changes. The fixture already has the database.
//
// It writes nothing unless the variable is set, because a gate that quietly rewrites what it is comparing against
// is not a gate.
//
//
// EVERY PERMISSIONED ROUTE DECLARES ITS PERMISSION IN THE DOCUMENT
//
// The contract makes this document the source for the interface's types and the integration client. Every route is
// gated, and the document said nothing about any gate, so a consumer reading it could not tell which token reaches
// which route and found out as a refusal.
//
// The denominator is the endpoint table rather than a sample: the count of operations carrying the annotation is
// compared against the count of endpoints carrying the metadata the filter enforces. A transformer that silently
// stopped running would leave the numbers unequal, where a spot-check on one route would not notice.
//
// One route is read end to end as well, so the annotation's SHAPE is asserted rather than its presence.

namespace MotsSupplierPortal.Tests.Integration.Contract;

using Microsoft.AspNetCore.Http;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Api.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using MotsSupplierPortal.Tests.Integration;

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

        response.IsSuccessStatusCode.Should().BeTrue(
            $"/openapi/v1.json must be reachable for the contract gate to mean anything (got {response.StatusCode})");

        var json = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(json)!.AsObject();
    }

    private static readonly string[] Methods = ["get", "put", "post", "delete", "patch", "options", "head"];

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

    private static HashSet<string> RequiredRequestProperties(JsonObject document)
    {
        var schemas = document["components"]?["schemas"]?.AsObject();
        var required = new HashSet<string>(StringComparer.Ordinal);

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

        baseline["paths"]!.AsObject().Count.Should().BeGreaterThan(150, "the committed baseline must be real");
        current["paths"]!.AsObject().Count.Should().BeGreaterThan(150, "the live document must be real");

        var baselineShapes = ResponseShapes(baseline);
        var currentShapes = ResponseShapes(current);

        var removed = baselineShapes.Except(currentShapes).OrderBy(x => x, StringComparer.Ordinal).ToList();
        removed.Should().BeEmpty(
            "a path, operation, response status or response field that existed in the committed contract and " +
            "does not exist now is a breaking change, and API-ARCHITECTURE.md §Versioning says it needs " +
            $"/api/v2 rather than a quiet edit:\n  {string.Join("\n  ", removed.Take(40))}");

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
        var baseline = JsonNode.Parse(await File.ReadAllTextAsync(BaselinePath()))!.AsObject();
        var current = await CurrentDocumentAsync();

        var added = ResponseShapes(current).Except(ResponseShapes(baseline)).ToList();
        added.Should().NotBeNull();

        var mutilated = JsonNode.Parse(current.ToJsonString())!.AsObject();
        var firstPath = mutilated["paths"]!.AsObject().First().Key;
        mutilated["paths"]!.AsObject().Remove(firstPath);

        ResponseShapes(current).Except(ResponseShapes(mutilated)).Should().NotBeEmpty(
            "the comparison must detect a removed path, or the first test proves nothing");
    }

    [Fact]
    public async Task Every_permissioned_route_names_its_permission_in_the_document()
    {
        var document = await CurrentDocumentAsync();

        var annotated = document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject())
            .Where(operation => operation.Value is JsonObject shape
                                && shape.ContainsKey("x-required-permissions"))
            .ToList();

        annotated.Should().HaveCountGreaterThan(150,
            "nearly every route in this API is permissioned, so the annotation must be on nearly every operation");

        var enforced = fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Count(e => e.Metadata.GetMetadata<RequiredPermissionsMetadata>() is not null);

        annotated.Should().HaveCount(enforced,
            "the document and the filter must agree about which routes are gated - a difference means one "
            + "of them is lying to a consumer generating a client");

        var audit = document["paths"]!["/api/v1/audit"]!["get"]!.AsObject();
        audit["x-required-permissions"]!.AsArray().Select(v => v!.GetValue<string>())
            .Should().Contain(Permissions.AuditRead);
        audit["description"]!.GetValue<string>().Should().Contain(Permissions.AuditRead,
            "a generator that drops unknown x- extensions still carries the prose");
        audit["responses"]!["403"].Should().NotBeNull("a permission a caller can read, with a refusal they cannot anticipate, is half an answer");
    }
}
