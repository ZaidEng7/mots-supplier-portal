using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotsSupplierPortal.Application.Common;

/// <summary>
/// Distinguishes "the client did not mention this field" from "the client explicitly sent null".
///
/// Without this, a PATCH request record with nullable properties cannot tell the two apart: an
/// omitted field binds to null exactly like an explicit null, so applying the whole record wipes
/// every field the caller left out. That is PUT behaviour under a PATCH verb, and it silently
/// destroyed data (found in review 2026-08-28: a PATCH carrying only unknown fields returned 200,
/// advanced the row version, and cleared `description`).
///
/// Usage: <c>Patch&lt;string?&gt; Description</c>. <see cref="IsSet"/> is false when the JSON body
/// omitted the property entirely, true when it was present - including when present as null, which
/// is a meaningful instruction to clear the field.
/// </summary>
[JsonConverter(typeof(PatchJsonConverterFactory))]
public readonly struct Patch<T>
{
    public bool IsSet { get; }
    public T? Value { get; }

    private Patch(T? value)
    {
        IsSet = true;
        Value = value;
    }

    public static Patch<T> Set(T? value) => new(value);

    /// <summary>Returns the patched value when the caller supplied one, otherwise the value the
    /// entity already holds - the merge that makes a partial update partial.</summary>
    public T? Or(T? current) => IsSet ? Value : current;
}

/// <summary>
/// Only invoked when the property is actually present in the JSON payload; an absent property
/// leaves <c>default(Patch&lt;T&gt;)</c>, whose <see cref="Patch{T}.IsSet"/> is false. HandleNull
/// is enabled so an explicit <c>null</c> still reaches the converter and records IsSet = true.
/// </summary>
public sealed class PatchJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Patch<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(PatchJsonConverter<>).MakeGenericType(valueType))!;
    }

    private sealed class PatchJsonConverter<T> : JsonConverter<Patch<T>>
    {
        public override bool HandleNull => true;

        public override Patch<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Patch<T>.Set(JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Patch<T> value, JsonSerializerOptions options)
        {
            if (!value.IsSet)
            {
                writer.WriteNullValue();
                return;
            }

            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
