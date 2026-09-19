using System.Buffers;
using System.Text;
using FastJson;
using FastJson.Example;

var product = new Product
{
    Id = Guid.NewGuid(),
    Name = "Torque Wrench",
    Price = 129.95m,
    UpdatedAt = DateTimeOffset.UtcNow
};

var utf8Json = FastJsonSerializer.SerializeUtf8(product, AppJsonContext.Default.Product);
Console.WriteLine(Encoding.UTF8.GetString(utf8Json));

var buffer = new ArrayBufferWriter<byte>();
FastJsonSerializer.Serialize(buffer, product, AppJsonContext.Default.Product);
var roundTrip = FastJsonSerializer.Deserialize(buffer.WrittenSpan, AppJsonContext.Default.Product);
Console.WriteLine($"Round trip: {roundTrip?.Name}");

var outputPath = Path.Combine(Path.GetTempPath(), "fast-json-product.json");
await FastJsonSerializer.ExportFileAsync(
    outputPath,
    product,
    AppJsonContext.Default.Product);

var imported = await FastJsonSerializer.ImportFileAsync(
    outputPath,
    AppJsonContext.Default.Product);
Console.WriteLine($"Imported: {imported?.Name}");

const string sampleJson = """
    {
      "id": "ef46699c-b93c-4d8d-945a-9083e89df657",
      "name": "Torque Wrench",
      "price": 129.95,
      "updatedAt": "2026-09-18T15:30:00Z"
    }
    """;

var generatedSource = JsonClassGenerator.GenerateClasses(
    sampleJson,
    options: new JsonClassGeneratorOptions
    {
        Namespace = "MyCompany.Catalog",
        RootClassName = "Product",
        ContextName = "CatalogJsonContext"
    });

if (args.Length == 1)
{
    await File.WriteAllTextAsync(args[0], generatedSource);
}

Console.WriteLine();
Console.WriteLine("Generated C# preview:");
Console.WriteLine(generatedSource);
