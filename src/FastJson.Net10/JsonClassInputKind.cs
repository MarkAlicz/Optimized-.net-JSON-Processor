namespace FastJson;

/// <summary>Identifies the input accepted by <see cref="JsonClassGenerator.GenerateClasses(string, JsonClassInputKind, JsonClassGeneratorOptions?)"/>.</summary>
public enum JsonClassInputKind
{
    /// <summary>Infer model shapes from a representative JSON value.</summary>
    SampleJson,

    /// <summary>Read model shapes from the supported subset of JSON Schema.</summary>
    JsonSchema
}
