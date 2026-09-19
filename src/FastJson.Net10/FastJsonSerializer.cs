using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FastJson;

/// <summary>
/// Provides allocation-conscious JSON operations that require source-generated type metadata.
/// </summary>
public static class FastJsonSerializer
{
    private const int FileBufferSize = 16 * 1024;

    /// <summary>Serializes a value to a JSON string.</summary>
    public static string Serialize<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return JsonSerializer.Serialize(value, jsonTypeInfo);
    }

    /// <summary>Serializes a value directly to a newly allocated UTF-8 byte array.</summary>
    public static byte[] SerializeUtf8<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo);
    }

    /// <summary>Serializes a value through a caller-owned UTF-8 writer.</summary>
    /// <remarks>The caller controls when the writer is flushed.</remarks>
    public static void Serialize<T>(
        Utf8JsonWriter writer,
        T value,
        JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        JsonSerializer.Serialize(writer, value, jsonTypeInfo);
    }

    /// <summary>Serializes a value directly into an <see cref="IBufferWriter{T}"/>.</summary>
    public static void Serialize<T>(
        IBufferWriter<byte> destination,
        T value,
        JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        using var writer = new Utf8JsonWriter(destination, CreateWriterOptions(jsonTypeInfo.Options));
        JsonSerializer.Serialize(writer, value, jsonTypeInfo);
        writer.Flush();
    }

    /// <summary>Deserializes JSON from a string.</summary>
    public static T? Deserialize<T>(string json, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return JsonSerializer.Deserialize(json, jsonTypeInfo);
    }

    /// <summary>Deserializes JSON from a UTF-8 span without creating an intermediate string.</summary>
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return JsonSerializer.Deserialize(utf8Json, jsonTypeInfo);
    }

    /// <summary>Serializes a value synchronously to a stream.</summary>
    public static void Export<T>(Stream destination, T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        JsonSerializer.Serialize(destination, value, jsonTypeInfo);
    }

    /// <summary>Deserializes a value synchronously from a stream.</summary>
    public static T? Import<T>(Stream source, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return JsonSerializer.Deserialize(source, jsonTypeInfo);
    }

    /// <summary>Serializes a value asynchronously to a stream.</summary>
    public static Task ExportAsync<T>(
        Stream destination,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return JsonSerializer.SerializeAsync(destination, value, jsonTypeInfo, cancellationToken);
    }

    /// <summary>Deserializes a value asynchronously from a stream.</summary>
    public static ValueTask<T?> ImportAsync<T>(
        Stream source,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return JsonSerializer.DeserializeAsync(source, jsonTypeInfo, cancellationToken);
    }

    /// <summary>Serializes a value asynchronously to a file, replacing an existing file.</summary>
    public static async Task ExportFileAsync<T>(
        string path,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(path);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = FileBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

        await JsonSerializer.SerializeAsync(stream, value, jsonTypeInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Deserializes a value asynchronously from a file.</summary>
    public static async ValueTask<T?> ImportFileAsync<T>(
        string path,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(path);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = FileBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    private static JsonWriterOptions CreateWriterOptions(JsonSerializerOptions options) => new()
    {
        Encoder = options.Encoder,
        Indented = options.WriteIndented,
        MaxDepth = options.MaxDepth,
        NewLine = options.NewLine,
        IndentCharacter = options.IndentCharacter,
        IndentSize = options.IndentSize
    };

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
    }
}
