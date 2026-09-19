using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastJson.Tests;

internal sealed class Product
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public decimal Price { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNameCaseInsensitive = false,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Product))]
internal partial class TestJsonContext : JsonSerializerContext;
