using System.Buffers;
using System.Text;
using System.Text.Json;
using FastJson;
using FastJson.Tests;

var tests = new (string Name, Func<Task> Run)[]
{
    ("UTF-8 round trip", Utf8RoundTrip),
    ("IBufferWriter serialization", BufferWriterSerialization),
    ("Stream round trip", StreamRoundTrip),
    ("Async file round trip", FileRoundTrip),
    ("Sample class generation", SampleGeneration),
    ("Schema class generation", SchemaGeneration),
    ("Input validation", InputValidation)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static Product CreateProduct() => new()
{
    Id = 42,
    Name = "Socket Set",
    Price = 79.50m
};

static Task Utf8RoundTrip()
{
    var bytes = FastJsonSerializer.SerializeUtf8(CreateProduct(), TestJsonContext.Default.Product);
    var product = FastJsonSerializer.Deserialize(bytes.AsSpan(), TestJsonContext.Default.Product)
        ?? throw new InvalidOperationException("Expected a non-null product.");
    Assert.Equal(42, product.Id);
    Assert.Equal("Socket Set", product.Name);
    Assert.Equal(79.50m, product.Price);
    return Task.CompletedTask;
}

static Task BufferWriterSerialization()
{
    var buffer = new ArrayBufferWriter<byte>();
    FastJsonSerializer.Serialize(buffer, CreateProduct(), TestJsonContext.Default.Product);

    var json = Encoding.UTF8.GetString(buffer.WrittenSpan);
    Assert.Contains("\"id\":42", json);
    Assert.Contains("\"name\":\"Socket Set\"", json);
    return Task.CompletedTask;
}

static async Task StreamRoundTrip()
{
    await using var stream = new MemoryStream();
    await FastJsonSerializer.ExportAsync(stream, CreateProduct(), TestJsonContext.Default.Product);
    stream.Position = 0;

    var product = await FastJsonSerializer.ImportAsync(stream, TestJsonContext.Default.Product)
        ?? throw new InvalidOperationException("Expected a non-null product.");
    Assert.Equal(42, product.Id);
}

static async Task FileRoundTrip()
{
    var path = Path.Combine(Path.GetTempPath(), $"fast-json-{Guid.NewGuid():N}.json");
    try
    {
        await FastJsonSerializer.ExportFileAsync(path, CreateProduct(), TestJsonContext.Default.Product);
        var product = await FastJsonSerializer.ImportFileAsync(path, TestJsonContext.Default.Product)
            ?? throw new InvalidOperationException("Expected a non-null product.");
        Assert.Equal("Socket Set", product.Name);
    }
    finally
    {
        File.Delete(path);
    }
}

static Task SampleGeneration()
{
    const string sample = """
        [
          { "id": 1, "display-name": "First", "tags": ["a"] },
          { "id": 2147483648, "tags": [] }
        ]
        """;

    var source = JsonClassGenerator.GenerateClasses(
        sample,
        options: new JsonClassGeneratorOptions
        {
            Namespace = "Generated.Catalog",
            RootClassName = "Products",
            ContextName = "CatalogJsonContext"
        });

    Assert.Contains("class Products : List<Product>", source);
    Assert.Contains("public required long Id", source);
    Assert.Contains("public string? DisplayName", source);
    Assert.Contains("[JsonPropertyName(\"display-name\")]", source);
    Assert.Contains("partial class CatalogJsonContext", source);
    return Task.CompletedTask;
}

static Task SchemaGeneration()
{
    const string schema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["id", "customer"],
          "properties": {
            "id": { "type": "string", "format": "uuid" },
            "customer": { "$ref": "#/$defs/customer" },
            "notes": { "type": ["string", "null"] }
          },
          "$defs": {
            "customer": {
              "type": "object",
              "required": ["name"],
              "properties": {
                "name": { "type": "string" },
                "birthDate": { "type": "string", "format": "date" }
              }
            }
          }
        }
        """;

    var source = JsonClassGenerator.GenerateClasses(
        schema,
        JsonClassInputKind.JsonSchema,
        new JsonClassGeneratorOptions { RootClassName = "Order" });

    Assert.Contains("public sealed class Order", source);
    Assert.Contains("public required Guid Id", source);
    Assert.Contains("public required Customer Customer", source);
    Assert.Contains("public string? Notes", source);
    Assert.Contains("public DateOnly? BirthDate", source);
    return Task.CompletedTask;
}

static Task InputValidation()
{
    Assert.Throws<ArgumentException>(() =>
        JsonClassGenerator.GenerateClasses("42"));
    Assert.Throws<JsonException>(() =>
        JsonClassGenerator.GenerateClasses("{ invalid"));
    return Task.CompletedTask;
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected generated text to contain '{expected}'.");
        }
    }

    public static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
