namespace FastJson;

/// <summary>Controls the C# source emitted by <see cref="JsonClassGenerator"/>.</summary>
public sealed class JsonClassGeneratorOptions
{
    /// <summary>Gets or sets the generated namespace.</summary>
    public string Namespace { get; init; } = "GeneratedModels";

    /// <summary>Gets or sets the name assigned to the root model.</summary>
    public string RootClassName { get; init; } = "Root";

    /// <summary>Gets or sets the generated <c>JsonSerializerContext</c> name.</summary>
    public string ContextName { get; init; } = "GeneratedJsonContext";

    /// <summary>Gets or sets whether a source-generation context is emitted.</summary>
    public bool IncludeSerializerContext { get; init; } = true;

    /// <summary>Gets or sets whether non-null, present properties use the C# <c>required</c> modifier.</summary>
    public bool UseRequiredProperties { get; init; } = true;

    /// <summary>Gets or sets whether ISO 8601 timestamps, dates, and GUID strings are inferred.</summary>
    public bool InferFormattedStrings { get; init; } = true;
}
