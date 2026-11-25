using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Xml.Linq;

namespace AI.Agent.ConsoleApp
{
    public class McpTool : BaseTool
    {
        //private readonly StdioClientTransportOptions _transportOptions;
        private readonly McpClientTool _mcpClientTool;

        public McpTool(McpClientTool mcpClientTool): base(mcpClientTool.Name, mcpClientTool.Description, GenerateToolDefinationByJsonElement(mcpClientTool.Name, mcpClientTool.Description, mcpClientTool.JsonSchema))
        {
            _mcpClientTool = mcpClientTool;
        }

        public override async Task<object?> ExceuteAsync(
            ExecutionContext executionContext,
            Dictionary<string, object?>? kwargs = null)
        {
            kwargs ??= new Dictionary<string, object?>();
            //if (_mcpClientTool == null)
            //{
            //    var transport = new StdioClientTransport(_transportOptions);
            //    await using McpClient client = await McpClient.CreateAsync(transport);
            //    CallToolResult result = await client.CallToolAsync(Name, kwargs);
            //    return ExtractTextContent(result);
            //}
            var callToolResult = await _mcpClientTool.CallAsync(kwargs);
            return ExtractTextContent(callToolResult);
        }
        private static Dictionary<string, object> GenerateToolDefinationByJsonElement(string name, string desc, JsonElement parameters)
        {
            object parameterSchema = parameters.ValueKind == JsonValueKind.Object
                ? ReadObject(parameters)
                : new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                };

            return new Dictionary<string, object>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["description"] = desc ?? "",
                    ["parameters"] = parameterSchema
                }
            };
        }

        private static Dictionary<string, object> ReadObject(JsonElement element)
        {
            var result = new Dictionary<string, object>();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                result[property.Name] = ReadValue(property.Value)!;
            }

            return result;
        }

        private static object? ReadValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    return ReadObject(element);
                case JsonValueKind.Array:
                    var items = new List<object?>();
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        items.Add(ReadValue(item));
                    }

                    return items;
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                    {
                        return intValue;
                    }

                    if (element.TryGetInt64(out long longValue))
                    {
                        return longValue;
                    }

                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }
        private static string ExtractTextContent(CallToolResult result)
        {
            IEnumerable<string> textParts = result.Content
                .OfType<TextContentBlock>()
                .Select(block => block.Text);

            return string.Join("\n", textParts);
        }

        private static Dictionary<string, object?> GenerateDefinition(
            string name,
            string? description,
            JsonSchema? parameters)
        {
            var definition = new ToolDefinition
            {
                Function = new FunctionDefinition
                {
                    Name = name,
                    Description = description ?? "",
                    Parameters = parameters ?? new JsonSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, JsonSchema>()
                    }
                }
            };

            return ToolDefinitionFactory.Create(definition, LlmProvider.OpenAI);
        }
    }
}
