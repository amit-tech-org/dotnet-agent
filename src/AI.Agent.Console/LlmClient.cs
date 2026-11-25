using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Schema;

namespace AI.Agent.ConsoleApp
{
    public class LlmClient
    {
        private readonly ChatClient _client;
        private readonly Action<ChatCompletionOptions>? _configure;

        public LlmClient(string model, string apiKey, Uri? endpoint = null, Action<ChatCompletionOptions>? configure = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(model);
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

            Model = model;
            _configure = configure;
            _client = endpoint is null
                ? new ChatClient(model, new ApiKeyCredential(apiKey))
                : new ChatClient(model, new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint });
        }

        public string Model { get; }

        public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            try
            {
                List<ChatMessage> messages = BuildMessages(request);
                ChatCompletionOptions options = new ChatCompletionOptions();
                _configure?.Invoke(options);

                foreach (BaseTool tool in request.Tools)
                {
                    options.Tools.Add(ToChatTool(tool));
                }

                if (!string.IsNullOrWhiteSpace(request.ToolChoice))
                {
                    options.ToolChoice = CreateToolChoice(request.ToolChoice);
                }

                ChatCompletion completion = await _client.CompleteChatAsync(messages, options, cancellationToken);
                return ParseResponse(completion);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new LlmResponse { ErrorMessage = exception.Message };
            }
        }

        public async Task<string> AskAsync(string prompt, string sysPrompt = null, CancellationToken cancellationToken = default)
        {
            LlmResponse response = await GenerateAsync(CreateAskRequest(sysPrompt, prompt), cancellationToken);
            EnsureSuccess(response);
            return ReadAssistantText(response);
        }

        public async Task<T> AskAsync<T>(string prompt, string sysPrompt = null, CancellationToken cancellationToken = default)
        {
            string schemaText = JsonSerializerOptions.Default.GetJsonSchemaAsNode(typeof(T)).ToJsonString();
            string instruction = $"{prompt}\n\nRespond ONLY with valid JSON matching this schema:\n{schemaText}";
            LlmResponse response = await GenerateAsync(CreateAskRequest(sysPrompt, instruction), cancellationToken);
            EnsureSuccess(response);
            return JsonSerializer.Deserialize<T>(StripCodeFence(ReadAssistantText(response)))
                ?? throw new InvalidOperationException("LLM returned empty JSON.");
        }

        public static List<ChatMessage> BuildMessages(LlmRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var messages = new List<ChatMessage>();

            foreach (string instruction in request.Instructions)
            {
                messages.Add(ChatMessage.CreateSystemMessage(instruction));
            }

            foreach (IContentItem item in request.Contents)
            {
                switch (item)
                {
                    case Message message:
                        messages.Add(CreateRoleMessage(message));
                        break;
                    case ToolCall toolCall:
                        AppendToolCall(messages, toolCall);
                        break;
                    case ToolResult toolResult:
                        string content = toolResult.Content is { Count: > 0 } ? toolResult.Content[0] : "";
                        messages.Add(new ToolChatMessage(toolResult.ToolCallId, content));
                        break;
                }
            }

            return messages;
        }

        private LlmRequest CreateAskRequest(string sysPrompt, string instruction)
        {
            return new LlmRequest
            {
                ModelId = Model,
                Instructions = string.IsNullOrEmpty(sysPrompt) ? [] : [sysPrompt],
                Contents =
                [
                    new Message
                    {
                        Role = Role.User,
                        Content = instruction
                    }
                ]
            };
        }

        private static ChatMessage CreateRoleMessage(Message message)
        {
            string content = message.Content ?? "";
            return message.Role switch
            {
                Role.System => ChatMessage.CreateSystemMessage(content),
                Role.User => ChatMessage.CreateUserMessage(content),
                Role.Assistant => new AssistantChatMessage(content),
                _ => throw new InvalidOperationException($"Unsupported message role '{message.Role}'.")
            };
        }

        private static void AppendToolCall(List<ChatMessage> messages, ToolCall toolCall)
        {
            ChatToolCall call = ChatToolCall.CreateFunctionToolCall(
                toolCall.ToolCallId,
                toolCall.Name,
                BinaryData.FromString(JsonSerializer.Serialize(toolCall.Arguments ?? new Dictionary<string, object>())));

            if (messages.Count > 0 && messages[^1] is AssistantChatMessage assistant)
            {
                assistant.ToolCalls.Add(call);
                return;
            }

            messages.Add(new AssistantChatMessage([call]));
        }

        private static ChatTool ToChatTool(BaseTool tool)
        {
            if (tool.ToolDefination is null)
            {
                throw new InvalidOperationException($"Tool '{tool.Name}' does not have a definition.");
            }

            using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(tool.ToolDefination));
            JsonElement function = document.RootElement.TryGetProperty("function", out JsonElement functionElement)
                ? functionElement
                : document.RootElement;
            string name = function.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString() ?? tool.Name
                : tool.Name;
            string description = function.TryGetProperty("description", out JsonElement descriptionElement)
                ? descriptionElement.GetString() ?? tool.Description
                : tool.Description;
            BinaryData? parameters = function.TryGetProperty("parameters", out JsonElement parametersElement)
                ? BinaryData.FromString(parametersElement.GetRawText())
                : null;
            bool? strict = function.TryGetProperty("strict", out JsonElement strictElement)
                && (strictElement.ValueKind == JsonValueKind.True || strictElement.ValueKind == JsonValueKind.False)
                ? strictElement.GetBoolean()
                : null;

            return ChatTool.CreateFunctionTool(name, description, parameters, strict);
        }

        private static ChatToolChoice CreateToolChoice(string toolChoice)
        {
            return toolChoice switch
            {
                "auto" => ChatToolChoice.CreateAutoChoice(),
                "none" => ChatToolChoice.CreateNoneChoice(),
                "required" => ChatToolChoice.CreateRequiredChoice(),
                _ => ChatToolChoice.CreateFunctionChoice(toolChoice)
            };
        }

        private static LlmResponse ParseResponse(ChatCompletion completion)
        {
            var contentItems = new List<IContentItem>();
            string text = string.Concat(completion.Content.Select(part => part.Text));
            if (!string.IsNullOrEmpty(text))
            {
                contentItems.Add(new Message
                {
                    Role = Role.Assistant,
                    Content = text
                });
            }

            foreach (ChatToolCall toolCall in completion.ToolCalls)
            {
                contentItems.Add(new ToolCall
                {
                    ToolCallId = toolCall.Id,
                    Name = toolCall.FunctionName,
                    Arguments = ParseArguments(toolCall.FunctionArguments)
                });
            }

            var usage = new Dictionary<string, object?>();
            if (completion.Usage is not null)
            {
                usage["input_tokens"] = completion.Usage.InputTokenCount;
                usage["output_tokens"] = completion.Usage.OutputTokenCount;
            }

            return new LlmResponse
            {
                Content = contentItems,
                UsageMetadata = usage
            };
        }

        private static Dictionary<string, object> ParseArguments(BinaryData? arguments)
        {
            if (arguments is null)
            {
                return new Dictionary<string, object>();
            }

            using JsonDocument document = JsonDocument.Parse(arguments.ToString());
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? ReadObject(document.RootElement)
                : new Dictionary<string, object>();
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

        private static string ReadAssistantText(LlmResponse response)
        {
            foreach (IContentItem item in response.Content)
            {
                if (item is Message message)
                {
                    return message.Content ?? "";
                }
            }

            return "";
        }

        private static void EnsureSuccess(LlmResponse response)
        {
            if (response.ErrorMessage is not null)
            {
                throw new InvalidOperationException($"LLM request failed: {response.ErrorMessage}");
            }
        }

        private static string StripCodeFence(string text)
        {
            string cleaned = text.Trim();
            if (!cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                return cleaned;
            }

            int newLine = cleaned.IndexOf('\n');
            cleaned = newLine >= 0 ? cleaned[(newLine + 1)..] : cleaned[3..];
            int fence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0 && cleaned.EndsWith("```", StringComparison.Ordinal))
            {
                cleaned = cleaned[..fence];
            }

            if (cleaned.StartsWith("json", StringComparison.Ordinal))
            {
                cleaned = cleaned[4..].TrimStart();
            }

            return cleaned.Trim();
        }
    }
}
