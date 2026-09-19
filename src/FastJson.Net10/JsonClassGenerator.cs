using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FastJson;

/// <summary>
/// Generates compile-ready C# POCO source from representative JSON or a supported JSON Schema subset.
/// </summary>
/// <remarks>
/// Generated source must be added to a project and compiled. Runtime generation cannot extend an
/// already-compiled <c>JsonSerializerContext</c>.
/// </remarks>
public static class JsonClassGenerator
{
    /// <summary>Generates C# model and source-generation context source.</summary>
    public static string GenerateClasses(
        string jsonOrSchema,
        JsonClassInputKind inputKind = JsonClassInputKind.SampleJson,
        JsonClassGeneratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(jsonOrSchema);

        using var document = JsonDocument.Parse(jsonOrSchema, DocumentOptions);
        return Generate(document.RootElement, inputKind, options ?? new JsonClassGeneratorOptions());
    }

    /// <summary>Generates C# model and source-generation context source from UTF-8 input.</summary>
    public static string GenerateClasses(
        ReadOnlyMemory<byte> utf8JsonOrSchema,
        JsonClassInputKind inputKind = JsonClassInputKind.SampleJson,
        JsonClassGeneratorOptions? options = null)
    {
        using var document = JsonDocument.Parse(utf8JsonOrSchema, DocumentOptions);
        return Generate(document.RootElement, inputKind, options ?? new JsonClassGeneratorOptions());
    }

    private static JsonDocumentOptions DocumentOptions => new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 128
    };

    private static string Generate(
        JsonElement root,
        JsonClassInputKind inputKind,
        JsonClassGeneratorOptions options)
    {
        var normalizedNamespace = NormalizeNamespace(options.Namespace);
        var rootName = NormalizeTypeName(options.RootClassName, nameof(options.RootClassName));
        var contextName = NormalizeTypeName(options.ContextName, nameof(options.ContextName));

        var model = inputKind switch
        {
            JsonClassInputKind.SampleJson => SampleModelReader.Read(root, rootName, options),
            JsonClassInputKind.JsonSchema => new SchemaModelReader(root, options).Read(rootName),
            _ => throw new ArgumentOutOfRangeException(nameof(inputKind), inputKind, "Unknown input kind.")
        };

        return new SourceRenderer(options, normalizedNamespace, rootName, contextName).Render(model);
    }

    private static string NormalizeNamespace(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var segments = value.Split('.');
        for (var index = 0; index < segments.Length; index++)
        {
            segments[index] = NormalizeTypeName(segments[index], nameof(JsonClassGeneratorOptions.Namespace));
        }

        return string.Join('.', segments);
    }

    private static string NormalizeTypeName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = Identifier.ToPascalCase(value, "GeneratedType");
        return normalized[0] == '_' ? $"Generated{normalized}" : normalized;
    }

    private enum ModelKind
    {
        Unknown,
        Boolean,
        Int32,
        Int64,
        Decimal,
        Double,
        String,
        DateOnly,
        DateTimeOffset,
        Guid,
        JsonElement,
        Object,
        Array,
        Dictionary
    }

    private sealed class TypeModel
    {
        public ModelKind Kind { get; set; }

        public bool IsNullable { get; set; }

        public ObjectModel? Object { get; set; }

        public TypeModel? Element { get; set; }

        public static TypeModel Create(ModelKind kind, bool isNullable = false) => new()
        {
            Kind = kind,
            IsNullable = isNullable
        };

        public static TypeModel CreateObject(ObjectModel model) => new()
        {
            Kind = ModelKind.Object,
            Object = model
        };

        public static TypeModel CreateCollection(ModelKind kind, TypeModel element) => new()
        {
            Kind = kind,
            Element = element
        };
    }

    private sealed class ObjectModel(string suggestedName)
    {
        public string SuggestedName { get; } = suggestedName;

        public Dictionary<string, PropertyModel> Properties { get; } = new(StringComparer.Ordinal);
    }

    private sealed class PropertyModel(string jsonName, TypeModel type, bool isOptional)
    {
        public string JsonName { get; } = jsonName;

        public TypeModel Type { get; set; } = type;

        public bool IsOptional { get; set; } = isOptional;
    }

    private static class SampleModelReader
    {
        public static TypeModel Read(
            JsonElement root,
            string rootName,
            JsonClassGeneratorOptions options)
        {
            var model = Infer(root, rootName, options);
            if (model.Kind is not (ModelKind.Object or ModelKind.Array))
            {
                throw new ArgumentException(
                    "The sample root must be a JSON object or array because a primitive root cannot be represented by a POCO class.",
                    nameof(root));
            }

            return model;
        }

        private static TypeModel Infer(
            JsonElement element,
            string suggestedName,
            JsonClassGeneratorOptions options)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => InferObject(element, suggestedName, options),
                JsonValueKind.Array => InferArray(element, suggestedName, options),
                JsonValueKind.String => InferString(element.GetString()!, options),
                JsonValueKind.Number => InferNumber(element),
                JsonValueKind.True or JsonValueKind.False => TypeModel.Create(ModelKind.Boolean),
                JsonValueKind.Null or JsonValueKind.Undefined => TypeModel.Create(ModelKind.Unknown, true),
                _ => TypeModel.Create(ModelKind.JsonElement)
            };
        }

        private static TypeModel InferObject(
            JsonElement element,
            string suggestedName,
            JsonClassGeneratorOptions options)
        {
            var objectModel = new ObjectModel(suggestedName);
            foreach (var property in element.EnumerateObject())
            {
                var propertyTypeName = Identifier.ToPascalCase(property.Name, "NestedType");
                objectModel.Properties.Add(
                    property.Name,
                    new PropertyModel(property.Name, Infer(property.Value, propertyTypeName, options), false));
            }

            return TypeModel.CreateObject(objectModel);
        }

        private static TypeModel InferArray(
            JsonElement element,
            string suggestedName,
            JsonClassGeneratorOptions options)
        {
            var itemName = Identifier.Singularize(suggestedName);
            TypeModel? elementType = null;

            foreach (var item in element.EnumerateArray())
            {
                var current = Infer(item, itemName, options);
                elementType = elementType is null ? current : Merge(elementType, current);
            }

            return TypeModel.CreateCollection(
                ModelKind.Array,
                elementType ?? TypeModel.Create(ModelKind.JsonElement));
        }

        private static TypeModel InferString(string value, JsonClassGeneratorOptions options)
        {
            if (!options.InferFormattedStrings)
            {
                return TypeModel.Create(ModelKind.String);
            }

            if (Guid.TryParseExact(value, "D", out _))
            {
                return TypeModel.Create(ModelKind.Guid);
            }

            if (DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
            {
                return TypeModel.Create(ModelKind.DateOnly);
            }

            if (value.Contains('T', StringComparison.Ordinal)
                && DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _))
            {
                return TypeModel.Create(ModelKind.DateTimeOffset);
            }

            return TypeModel.Create(ModelKind.String);
        }

        private static TypeModel InferNumber(JsonElement element)
        {
            if (element.TryGetInt32(out _))
            {
                return TypeModel.Create(ModelKind.Int32);
            }

            if (element.TryGetInt64(out _))
            {
                return TypeModel.Create(ModelKind.Int64);
            }

            if (element.TryGetDecimal(out _))
            {
                return TypeModel.Create(ModelKind.Decimal);
            }

            return TypeModel.Create(ModelKind.Double);
        }

        public static TypeModel Merge(TypeModel left, TypeModel right)
        {
            if (left.Kind == ModelKind.Unknown)
            {
                right.IsNullable = true;
                return right;
            }

            if (right.Kind == ModelKind.Unknown)
            {
                left.IsNullable = true;
                return left;
            }

            if (left.Kind == right.Kind)
            {
                left.IsNullable |= right.IsNullable;

                if (left.Kind == ModelKind.Object)
                {
                    MergeObjects(left.Object!, right.Object!);
                }
                else if (left.Kind is ModelKind.Array or ModelKind.Dictionary)
                {
                    left.Element = Merge(left.Element!, right.Element!);
                }

                return left;
            }

            if (IsNumeric(left.Kind) && IsNumeric(right.Kind))
            {
                return TypeModel.Create(
                    PromoteNumber(left.Kind, right.Kind),
                    left.IsNullable || right.IsNullable);
            }

            if (IsFormattedString(left.Kind) && IsFormattedString(right.Kind))
            {
                return TypeModel.Create(ModelKind.String, left.IsNullable || right.IsNullable);
            }

            return TypeModel.Create(ModelKind.JsonElement, left.IsNullable || right.IsNullable);
        }

        private static void MergeObjects(ObjectModel target, ObjectModel incoming)
        {
            foreach (var targetProperty in target.Properties.Values)
            {
                if (!incoming.Properties.ContainsKey(targetProperty.JsonName))
                {
                    targetProperty.IsOptional = true;
                }
            }

            foreach (var incomingProperty in incoming.Properties.Values)
            {
                if (target.Properties.TryGetValue(incomingProperty.JsonName, out var existing))
                {
                    existing.Type = Merge(existing.Type, incomingProperty.Type);
                    existing.IsOptional |= incomingProperty.IsOptional;
                }
                else
                {
                    incomingProperty.IsOptional = true;
                    target.Properties.Add(incomingProperty.JsonName, incomingProperty);
                }
            }
        }

        private static bool IsNumeric(ModelKind kind) => kind is
            ModelKind.Int32 or ModelKind.Int64 or ModelKind.Decimal or ModelKind.Double;

        private static bool IsFormattedString(ModelKind kind) => kind is
            ModelKind.String or ModelKind.DateOnly or ModelKind.DateTimeOffset or ModelKind.Guid;

        private static ModelKind PromoteNumber(ModelKind left, ModelKind right)
        {
            if (left == ModelKind.Double || right == ModelKind.Double)
            {
                return ModelKind.Double;
            }

            if (left == ModelKind.Decimal || right == ModelKind.Decimal)
            {
                return ModelKind.Decimal;
            }

            return left == ModelKind.Int64 || right == ModelKind.Int64
                ? ModelKind.Int64
                : ModelKind.Int32;
        }
    }

    private sealed class SchemaModelReader(JsonElement schemaRoot, JsonClassGeneratorOptions options)
    {
        private readonly Dictionary<string, TypeModel> referenceCache = new(StringComparer.Ordinal);

        public TypeModel Read(string rootName)
        {
            var model = Parse(schemaRoot, rootName);
            if (model.Kind is not (ModelKind.Object or ModelKind.Array or ModelKind.Dictionary))
            {
                throw new ArgumentException(
                    "The schema root must describe an object, array, or dictionary.",
                    nameof(schemaRoot));
            }

            return model;
        }

        private TypeModel Parse(JsonElement schema, string suggestedName)
        {
            if (schema.ValueKind is JsonValueKind.True)
            {
                return TypeModel.Create(ModelKind.JsonElement);
            }

            if (schema.ValueKind is JsonValueKind.False)
            {
                throw new NotSupportedException("A JSON Schema value of false accepts no values and cannot produce a C# model.");
            }

            if (schema.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Every JSON Schema node must be an object or boolean.", nameof(schema));
            }

            if (schema.TryGetProperty("$ref", out var referenceElement))
            {
                return ParseReference(referenceElement.GetString()!, suggestedName);
            }

            if (TryParseUnion(schema, "anyOf", suggestedName, out var union)
                || TryParseUnion(schema, "oneOf", suggestedName, out union))
            {
                return union;
            }

            var (typeName, nullable) = ReadType(schema);
            var model = typeName switch
            {
                "object" => ParseObject(schema, suggestedName),
                "array" => ParseArray(schema, suggestedName),
                "string" => ParseSchemaString(schema),
                "integer" => ParseInteger(schema),
                "number" => ParseNumber(schema),
                "boolean" => TypeModel.Create(ModelKind.Boolean),
                "null" => TypeModel.Create(ModelKind.Unknown, true),
                null when schema.TryGetProperty("properties", out _) => ParseObject(schema, suggestedName),
                null => TypeModel.Create(ModelKind.JsonElement),
                _ => TypeModel.Create(ModelKind.JsonElement)
            };

            model.IsNullable |= nullable;
            return model;
        }

        private TypeModel ParseReference(string reference, string suggestedName)
        {
            if (!reference.StartsWith("#/", StringComparison.Ordinal))
            {
                throw new NotSupportedException($"Only local JSON Schema references are supported. Reference: '{reference}'.");
            }

            if (referenceCache.TryGetValue(reference, out var existing))
            {
                return existing;
            }

            var target = ResolveReference(reference);
            var referenceName = reference.Split('/').Last();
            referenceName = referenceName.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);

            if (IsObjectSchema(target))
            {
                var objectModel = new ObjectModel(Identifier.ToPascalCase(referenceName, suggestedName));
                var placeholder = TypeModel.CreateObject(objectModel);
                referenceCache.Add(reference, placeholder);
                PopulateObject(target, objectModel);
                return placeholder;
            }

            var parsed = Parse(target, Identifier.ToPascalCase(referenceName, suggestedName));
            referenceCache.Add(reference, parsed);
            return parsed;
        }

        private JsonElement ResolveReference(string reference)
        {
            var current = schemaRoot;
            foreach (var rawSegment in reference[2..].Split('/'))
            {
                var segment = rawSegment.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);

                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    throw new ArgumentException($"JSON Schema reference '{reference}' could not be resolved.");
                }
            }

            return current;
        }

        private bool TryParseUnion(
            JsonElement schema,
            string keyword,
            string suggestedName,
            out TypeModel model)
        {
            model = null!;
            if (!schema.TryGetProperty(keyword, out var variants) || variants.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            TypeModel? merged = null;
            var nullable = false;
            foreach (var variant in variants.EnumerateArray())
            {
                var parsed = Parse(variant, suggestedName);
                if (parsed.Kind == ModelKind.Unknown && parsed.IsNullable)
                {
                    nullable = true;
                    continue;
                }

                merged = merged is null ? parsed : SampleModelReader.Merge(merged, parsed);
            }

            model = merged ?? TypeModel.Create(ModelKind.JsonElement);
            model.IsNullable |= nullable;
            return true;
        }

        private static (string? TypeName, bool Nullable) ReadType(JsonElement schema)
        {
            if (!schema.TryGetProperty("type", out var type))
            {
                return (null, false);
            }

            if (type.ValueKind == JsonValueKind.String)
            {
                return (type.GetString(), type.GetString() == "null");
            }

            if (type.ValueKind != JsonValueKind.Array)
            {
                return (null, false);
            }

            string? selected = null;
            var nullable = false;
            foreach (var item in type.EnumerateArray())
            {
                var current = item.GetString();
                if (current == "null")
                {
                    nullable = true;
                }
                else if (selected is null)
                {
                    selected = current;
                }
                else if (selected != current)
                {
                    return (null, nullable);
                }
            }

            return (selected, nullable);
        }

        private TypeModel ParseObject(JsonElement schema, string suggestedName)
        {
            var objectModel = new ObjectModel(suggestedName);
            PopulateObject(schema, objectModel);

            if (objectModel.Properties.Count == 0
                && schema.TryGetProperty("additionalProperties", out var additionalProperties)
                && additionalProperties.ValueKind != JsonValueKind.False)
            {
                var valueType = additionalProperties.ValueKind == JsonValueKind.True
                    ? TypeModel.Create(ModelKind.JsonElement)
                    : Parse(additionalProperties, $"{suggestedName}Value");
                return TypeModel.CreateCollection(ModelKind.Dictionary, valueType);
            }

            return TypeModel.CreateObject(objectModel);
        }

        private void PopulateObject(JsonElement schema, ObjectModel objectModel)
        {
            var required = new HashSet<string>(StringComparer.Ordinal);
            if (schema.TryGetProperty("required", out var requiredElement)
                && requiredElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requiredElement.EnumerateArray())
                {
                    required.Add(item.GetString()!);
                }
            }

            if (!schema.TryGetProperty("properties", out var properties)
                || properties.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in properties.EnumerateObject())
            {
                var propertyTypeName = Identifier.ToPascalCase(property.Name, "NestedType");
                objectModel.Properties[property.Name] = new PropertyModel(
                    property.Name,
                    Parse(property.Value, propertyTypeName),
                    !required.Contains(property.Name));
            }
        }

        private TypeModel ParseArray(JsonElement schema, string suggestedName)
        {
            var itemType = schema.TryGetProperty("items", out var items)
                ? Parse(items, Identifier.Singularize(suggestedName))
                : TypeModel.Create(ModelKind.JsonElement);

            return TypeModel.CreateCollection(ModelKind.Array, itemType);
        }

        private TypeModel ParseSchemaString(JsonElement schema)
        {
            if (!options.InferFormattedStrings
                || !schema.TryGetProperty("format", out var formatElement))
            {
                return TypeModel.Create(ModelKind.String);
            }

            return formatElement.GetString() switch
            {
                "date" => TypeModel.Create(ModelKind.DateOnly),
                "date-time" => TypeModel.Create(ModelKind.DateTimeOffset),
                "uuid" => TypeModel.Create(ModelKind.Guid),
                _ => TypeModel.Create(ModelKind.String)
            };
        }

        private static TypeModel ParseInteger(JsonElement schema)
        {
            if (schema.TryGetProperty("format", out var format) && format.GetString() == "int32")
            {
                return TypeModel.Create(ModelKind.Int32);
            }

            return TypeModel.Create(ModelKind.Int64);
        }

        private static TypeModel ParseNumber(JsonElement schema)
        {
            if (!schema.TryGetProperty("format", out var format))
            {
                return TypeModel.Create(ModelKind.Decimal);
            }

            return format.GetString() switch
            {
                "float" or "double" => TypeModel.Create(ModelKind.Double),
                _ => TypeModel.Create(ModelKind.Decimal)
            };
        }

        private static bool IsObjectSchema(JsonElement schema)
        {
            var (typeName, _) = ReadType(schema);
            return typeName == "object" || schema.TryGetProperty("properties", out _);
        }
    }

    private sealed class SourceRenderer(
        JsonClassGeneratorOptions options,
        string generatedNamespace,
        string rootName,
        string contextName)
    {
        private readonly Dictionary<ObjectModel, string> objectNames = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> usedTypeNames = new(StringComparer.Ordinal);
        private readonly List<ObjectModel> objectOrder = [];

        public string Render(TypeModel root)
        {
            if (root.Kind == ModelKind.Object)
            {
                AssignObjectName(root.Object!, rootName);
            }
            else
            {
                usedTypeNames.Add(rootName);
            }

            var builder = new StringBuilder(2048);
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.AppendLine();
            builder.AppendLine("using System.Text.Json;");
            builder.AppendLine("using System.Text.Json.Serialization;");
            builder.AppendLine();
            builder.Append("namespace ").Append(generatedNamespace).AppendLine(";");
            builder.AppendLine();

            if (root.Kind == ModelKind.Array)
            {
                builder.Append("public sealed class ").Append(rootName).Append(" : List<")
                    .Append(GetTypeName(root.Element!, false)).AppendLine(">");
                builder.AppendLine("{");
                builder.AppendLine("}");
                builder.AppendLine();
            }
            else if (root.Kind == ModelKind.Dictionary)
            {
                builder.Append("public sealed class ").Append(rootName).Append(" : Dictionary<string, ")
                    .Append(GetTypeName(root.Element!, false)).AppendLine(">");
                builder.AppendLine("{");
                builder.AppendLine("}");
                builder.AppendLine();
            }

            for (var index = 0; index < objectOrder.Count; index++)
            {
                RenderObject(builder, objectOrder[index]);
            }

            if (options.IncludeSerializerContext)
            {
                builder.AppendLine("[JsonSourceGenerationOptions(");
                builder.AppendLine("    JsonSerializerDefaults.Web,");
                builder.AppendLine("    GenerationMode = JsonSourceGenerationMode.Default,");
                builder.AppendLine("    WriteIndented = false,");
                builder.AppendLine("    PropertyNameCaseInsensitive = false,");
                builder.AppendLine("    RespectNullableAnnotations = true,");
                builder.AppendLine("    RespectRequiredConstructorParameters = true)]");
                builder.Append("[JsonSerializable(typeof(").Append(rootName).AppendLine("))]");
                builder.Append("internal partial class ").Append(contextName)
                    .AppendLine(" : JsonSerializerContext;");
            }

            return builder.ToString();
        }

        private void RenderObject(StringBuilder builder, ObjectModel model)
        {
            builder.Append("public sealed class ").Append(objectNames[model]).AppendLine();
            builder.AppendLine("{");

            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in model.Properties.Values)
            {
                var propertyName = MakeUniquePropertyName(
                    Identifier.ToPascalCase(property.JsonName, "Value"),
                    propertyNames);
                var isNullable = property.IsOptional || property.Type.IsNullable;
                var typeName = GetTypeName(property.Type, isNullable);
                var isRequired = options.UseRequiredProperties && !isNullable;

                builder.Append("    [JsonPropertyName(")
                    .Append(ToCSharpStringLiteral(property.JsonName))
                    .AppendLine(")]");
                builder.Append("    public ");
                if (isRequired)
                {
                    builder.Append("required ");
                }

                builder.Append(typeName).Append(' ').Append(propertyName).Append(" { get; init; }");
                if (!isRequired && !isNullable && IsReferenceType(property.Type))
                {
                    builder.Append(" = ").Append(GetDefaultInitializer(property.Type)).Append(';');
                }

                builder.AppendLine();
                builder.AppendLine();
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        private string GetTypeName(TypeModel model, bool forceNullable)
        {
            var name = model.Kind switch
            {
                ModelKind.Boolean => "bool",
                ModelKind.Int32 => "int",
                ModelKind.Int64 => "long",
                ModelKind.Decimal => "decimal",
                ModelKind.Double => "double",
                ModelKind.String => "string",
                ModelKind.DateOnly => "DateOnly",
                ModelKind.DateTimeOffset => "DateTimeOffset",
                ModelKind.Guid => "Guid",
                ModelKind.Unknown or ModelKind.JsonElement => "JsonElement",
                ModelKind.Object => AssignObjectName(model.Object!, model.Object!.SuggestedName),
                ModelKind.Array => $"List<{GetTypeName(model.Element!, false)}>",
                ModelKind.Dictionary => $"Dictionary<string, {GetTypeName(model.Element!, false)}>",
                _ => throw new ArgumentOutOfRangeException(nameof(model), model.Kind, "Unknown model kind.")
            };

            return forceNullable ? $"{name}?" : name;
        }

        private string AssignObjectName(ObjectModel model, string suggestedName)
        {
            if (objectNames.TryGetValue(model, out var existing))
            {
                return existing;
            }

            var baseName = Identifier.ToPascalCase(suggestedName, "NestedType");
            var candidate = baseName;
            var suffix = 2;
            while (!usedTypeNames.Add(candidate))
            {
                candidate = $"{baseName}{suffix++}";
            }

            objectNames.Add(model, candidate);
            objectOrder.Add(model);
            return candidate;
        }

        private static string MakeUniquePropertyName(string baseName, HashSet<string> usedNames)
        {
            var candidate = baseName;
            var suffix = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = $"{baseName}{suffix++}";
            }

            return candidate;
        }

        private static bool IsReferenceType(TypeModel model) => model.Kind is
            ModelKind.String or ModelKind.Object or ModelKind.Array or ModelKind.Dictionary;

        private static string GetDefaultInitializer(TypeModel model) => model.Kind switch
        {
            ModelKind.String => "string.Empty",
            ModelKind.Array or ModelKind.Dictionary => "[]",
            ModelKind.Object => "new()",
            _ => throw new InvalidOperationException("A default initializer was requested for a value type.")
        };

        private static string ToCSharpStringLiteral(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');

            foreach (var character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(character) || character is '\u2028' or '\u2029')
                        {
                            builder.Append("\\u")
                                .Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.Append('"').ToString();
        }
    }

    private static class Identifier
    {
        public static string ToPascalCase(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var builder = new StringBuilder(value.Length + 1);
            var capitalize = true;
            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    capitalize = true;
                    continue;
                }

                if (character == '_')
                {
                    capitalize = true;
                    continue;
                }

                builder.Append(capitalize ? char.ToUpperInvariant(character) : character);
                capitalize = false;
            }

            if (builder.Length == 0)
            {
                return fallback;
            }

            if (char.IsDigit(builder[0]))
            {
                builder.Insert(0, '_');
            }

            return builder.ToString();
        }

        public static string Singularize(string value)
        {
            if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && value.Length > 3)
            {
                return $"{value[..^3]}y";
            }

            if (value.EndsWith('s') && !value.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
            {
                return value[..^1];
            }

            return $"{value}Item";
        }
    }
}
