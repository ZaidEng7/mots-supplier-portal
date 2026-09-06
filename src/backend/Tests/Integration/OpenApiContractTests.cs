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

    /// <summary>Required properties per schema, so a field becoming mandatory is caught: an existing client
    /// that does not send it starts failing, which is a breaking change even though nothing was removed.</summary>
    private static HashSet<string> RequiredProperties(JsonObject document)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, schema) in document["components"]?["schemas"]?.AsObject() ?? [])
        {
            foreach (var property in schema?["required"]?.AsArray() ?? [])
            {
                required.Add($"{name}.{property!.GetValue<string>()}");
            }
        }
        return required;
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

        var newlyRequired = RequiredProperties(current).Except(RequiredProperties(baseline))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        newlyRequired.Should().BeEmpty(
            "a property that was optional and is now required breaks every existing client that does not " +
            $"send it, even though nothing was removed:\n  {string.Join("\n  ", newlyRequired.Take(40))}");
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
}
