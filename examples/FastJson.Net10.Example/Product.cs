using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastJson.Example;

public sealed class Product
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public decimal Price { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Default,
    WriteIndented = false,
    PropertyNameCaseInsensitive = false,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(List<Product>))]
internal partial class AppJsonContext : JsonSerializerContext;
