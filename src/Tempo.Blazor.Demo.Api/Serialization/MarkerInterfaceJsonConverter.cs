using System.Text.Json;
using System.Text.Json.Serialization;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.Api.Serialization;

/// <summary>
/// <see cref="IFieldConfig"/> and <see cref="IDatabaseViewConfig"/> are empty marker interfaces.
/// Serializing a field through the interface writes <c>{}</c> — the WASM client then receives no
/// Select/Status options at all and the table + record detail render empty chips (F-04/F-05).
/// This converter writes the RUNTIME type's payload instead (serialize-only; the demo API never
/// reads these DTOs back through the converter).
/// </summary>
public sealed class MarkerInterfaceJsonConverter<T> : JsonConverter<T> where T : class
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException($"{nameof(MarkerInterfaceJsonConverter<T>)} is serialize-only.");

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
