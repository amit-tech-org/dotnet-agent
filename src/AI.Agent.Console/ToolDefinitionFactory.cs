namespace AI.Agent.ConsoleApp
{
    public enum LlmProvider
    {
        OpenAI
    }

    public static class ToolDefinitionFactory
    {
        public static Dictionary<string, object?> Create(ToolDefinition definition, LlmProvider provider)
        {
            ArgumentNullException.ThrowIfNull(definition);

            return provider switch
            {
                LlmProvider.OpenAI => CreateOpenAI(definition),
                _ => throw new NotSupportedException($"LLM provider '{provider}' is not supported.")
            };
        }

        private static Dictionary<string, object?> CreateOpenAI(ToolDefinition definition)
        {
            FunctionDefinition function = definition.Function;
            var functionDefinition = new Dictionary<string, object?>
            {
                ["name"] = function.Name
            };

            if (function.Description is not null)
            {
                functionDefinition["description"] = function.Description;
            }

            if (function.Parameters is not null)
            {
                functionDefinition["parameters"] = ToSchemaDictionary(function.Parameters);
            }

            if (function.Strict is not null)
            {
                functionDefinition["strict"] = function.Strict;
            }

            return new Dictionary<string, object?>
            {
                ["type"] = definition.Type,
                ["function"] = functionDefinition
            };
        }

        private static Dictionary<string, object?> ToSchemaDictionary(JsonSchema schema)
        {
            var result = new Dictionary<string, object?>();

            if (schema.Type is not null)
            {
                result["type"] = schema.Type;
            }

            if (schema.Description is not null)
            {
                result["description"] = schema.Description;
            }

            if (schema.Properties is not null)
            {
                result["properties"] = schema.Properties.ToDictionary(
                    property => property.Key,
                    property => (object?)ToSchemaDictionary(property.Value));
            }

            if (schema.Items is not null)
            {
                result["items"] = ToSchemaDictionary(schema.Items);
            }

            if (schema.Required is not null)
            {
                result["required"] = schema.Required;
            }

            if (schema.Enum is not null)
            {
                result["enum"] = schema.Enum;
            }

            if (schema.AdditionalProperties is not null)
            {
                result["additionalProperties"] = schema.AdditionalProperties;
            }

            if (schema.ExtensionData is not null)
            {
                foreach (var (key, value) in schema.ExtensionData)
                {
                    if (!result.ContainsKey(key))
                    {
                        result[key] = value;
                    }
                }
            }

            return result;
        }
    }
}
