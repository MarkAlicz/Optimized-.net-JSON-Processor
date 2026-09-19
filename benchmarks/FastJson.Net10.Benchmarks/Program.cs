using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FastJson;

var iterations = args.Length == 1 && int.TryParse(args[0], out var requested)
    ? requested
    : 250_000;

var product = new Product
{
    Id = 42,
    Name = "Professional Torque Wrench",
    Price = 129.95m,
    Tags = ["tools", "automotive", "calibrated"]
};

Run("Reflection string", iterations, () => JsonSerializer.Serialize(product));
Run("Source-gen string", iterations, () =>
    FastJsonSerializer.Serialize(product, BenchmarkJsonContext.Default.Product));
Run("Source-gen UTF-8", iterations, () =>
    FastJsonSerializer.SerializeUtf8(product, BenchmarkJsonContext.Default.Product));

static void Run<T>(string name, int iterations, Func<T> operation)
{
    for (var index = 0; index < 5_000; index++)
    {
        _ = operation();
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();
    var checksum = 0;

    for (var index = 0; index < iterations; index++)
    {
        checksum ^= operation()?.GetHashCode() ?? 0;
    }

    stopwatch.Stop();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    var operationsPerSecond = iterations / stopwatch.Elapsed.TotalSeconds;
    var bytesPerOperation = allocated / (double)iterations;

    Console.WriteLine(
        $"{name,-20} {operationsPerSecond,12:N0} ops/s {bytesPerOperation,10:N1} B/op  checksum={checksum}");
}

internal sealed class Product
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public decimal Price { get; init; }

    public required List<string> Tags { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(Product))]
internal partial class BenchmarkJsonContext : JsonSerializerContext;
