// Tells apart "the client did not mention this field" from "the client explicitly asked to clear it".
//
// Without it, a partial-update request whose fields are simply optional cannot distinguish the two: an
// omitted field arrives exactly as an explicitly cleared one does, so applying the whole request wipes
// every field the caller left out.
//
// That is whole-replacement behaviour under a partial-update verb, and it silently destroyed data. Found in
// review: a partial update carrying nothing but unrecognised fields returned success, advanced the record's
// version, and cleared the description.
//
// A field is declared as a patch of its own type. IsSet is false when the body omitted it and true when it
// was present, including when present and empty, which is a real instruction to clear.
//
// Or is the merge that makes a partial update partial: the caller's value when they supplied one, and the
// value the record already holds when they did not.
//
//
// HOW THE ABSENT CASE ACTUALLY WORKS
//
// The converter runs only when the property is present in the payload. An absent property therefore leaves
// the default value, whose IsSet is false, and no code has to notice.
//
// Null handling is switched on deliberately, so an explicitly empty value still reaches the converter and
// is recorded as set. Without that, clearing a field would be indistinguishable from omitting it, which is
// the whole problem this type exists to solve.

namespace MotsSupplierPortal.Application.Common;

using System.Text.Json;
using System.Text.Json.Serialization;

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

    public T? Or(T? current) => IsSet ? Value : current;
}

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
