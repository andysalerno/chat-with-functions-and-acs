namespace Functions;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;

internal class FunctionBuilder
{
    private readonly string _functionName;

    private readonly List<string> _requiredPropertyNames = new List<string>();

    private readonly List<Property> _parameters = new List<Property>();

    private string? _description;

    public FunctionBuilder(string functionName)
    {
        _functionName = functionName;
    }

    public enum Type
    {
        String,
        Number,
        Boolean,
        Object,
    }

    public FunctionBuilder WithDescription(string description)
    {
        _description = description;

        return this;
    }

    public FunctionBuilder WithRequiredParameter(string parameterName)
    {
        _requiredPropertyNames.Add(parameterName);

        return this;
    }

    public FunctionBuilder WithEnumParameter(string name, string description, IEnumerable<string> values, bool isRequired = false)
    {
        if (isRequired)
        {
            _requiredPropertyNames.Add(name);
        }

        _parameters.Add(new Property
        {
            Name = name,
            Type = TypeToString(Type.String),
            Description = description,
            Enum = values.ToList(),
        });

        return this;
    }

    public FunctionBuilder WithParameter(string name, Type type, string description, bool isRequired = false)
    {
        if (isRequired)
        {
            _requiredPropertyNames.Add(name);
        }

        _parameters.Add(new Property
        {
            Name = name,
            Type = TypeToString(type),
            Description = description,
        });

        return this;
    }

    public FunctionDefinition Build()
    {
        var properties = _parameters.ToDictionary(p => p.Name, p => p);

        return new FunctionDefinition(_functionName)
        {
            Description = _description,
            Parameters = BinaryData.FromObjectAsJson(new FunctionParameters
            {
                Properties = properties,
                Required = _requiredPropertyNames,
            }),
        };
    }

    private static string TypeToString(Type type)
    {
        switch (type)
        {
            case Type.String:
                return "string";
            case Type.Number:
                return "number";
            case Type.Boolean:
                return "boolean";
            case Type.Object:
                return "object";
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    internal class FunctionParameters
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "object";

        [JsonPropertyName("properties")]
        public object Properties { get; set; } = new Dictionary<string, Property>();

        [JsonPropertyName("required")]
        public List<string> Required { get; set; } = new List<string>();
    }

    internal class Property
    {
        [JsonIgnore]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("enum")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> Enum { get; init; } = new List<string>();
    }
}
