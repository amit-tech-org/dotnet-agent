using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AI.Agent.ConsoleApp
{
    public class AgentResult
    {
        public string Output { get; set; }
        public ExecutionContext Context { get; set; }
        public AgentResult(string op, ExecutionContext executionContext)
        {
            Output = op;
            Context = executionContext;
        }
    }
    public class Agent(LlmClient client, string instructions = null, List<BaseTool> tools = null, int maxRetries = 10, string name = "Agent")
    {
        private readonly LlmClient _client = client;
        private readonly string _instructions = instructions ?? "";
        private readonly List<BaseTool> _tools = tools ?? new List<BaseTool>();
        private readonly int _maxRetries = maxRetries;
        private readonly string _name = name;

        public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
        {
            var request = new LlmRequest
            {
                Instructions = new List<string> { _instructions },
                Contents = new List<IContentItem>
                {
                    new Message
                    {
                        Role = Role.User,
                        Content = question
                    }
                },
                Tools = _tools
            };
            for (int attempt = 0; attempt < _maxRetries; attempt++)
            {
                try
                {
                    var response = await _client.GenerateAsync(request, cancellationToken);
                    if (response.Content.Count > 0 && response.Content[0] is Message message)
                    {
                        return message.Content ?? "";
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == _maxRetries - 1)
                    {
                        throw;
                    }
                    // Optionally log the exception or handle it as needed
                }
            }
            throw new InvalidOperationException("Failed to get a valid response from the LLM after multiple attempts.");
        }
        public async Task<AgentResult> Run(string userPrompt, ExecutionContext executionContext = null)
        {
            executionContext = executionContext ?? new ExecutionContext();
            var userEvent = new Event()
            {
                ExecutionId = executionContext.ExecutionId,
                Author = _name,
                Content = new List<IContentItem>() { new Message() { Content = userPrompt, Role = Role.User } }
            };
            executionContext.AddEvent(userEvent);
            while(executionContext.FinalResult == null && executionContext.CurrentStep < _maxRetries)
            {
                await Step(executionContext);
                var lastEvent = executionContext.Events.Last();
                if (IsFinalResponse(lastEvent))
                {
                    return new AgentResult(ExtractFinalResult(lastEvent), executionContext);
                }
            }
            return new AgentResult(string.Empty, executionContext);
        }
        private async Task Step(ExecutionContext executionContext)
        {
            var request = CreateLlmRequest(executionContext);
            var response = await Think(request);
            var evt = new Event()
            {
                ExecutionId = executionContext.ExecutionId,
                Author = this._name,
                Content = response.Content
            };
            executionContext.AddEvent(evt);
            var toolCalls = response.Content.Where(a => a.Type == ContentType.ToolCall)
                                            .Cast<ToolCall>();
            if (toolCalls.Any())
            {
                var toolRes = await Act(executionContext, toolCalls);
                executionContext.AddEvent(new Event()
                {
                    Author = _name,
                    Content = toolRes,
                    ExecutionId = executionContext.ExecutionId,
                    
                });
            }
            executionContext.IncrementStep();
        }
        private async Task<List<ToolResult>> Act(ExecutionContext context, IEnumerable<ToolCall> tools)
        {
            var toolDict = this._tools.ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);
            List<ToolResult> toolResults = new List<ToolResult>();
            foreach (var tool in tools)
            {
                if (!toolDict.TryGetValue(tool.Name, out var baseTool))
                {
                    toolResults.Add(new ToolResult()
                    {
                        Content = [$"Tool {tool.Name} not found."],
                        Status = Status.Error,
                        Name = tool.Name,
                        ToolCallId = tool.ToolCallId,
                    });
                }
                try
                {
                    var obj = await baseTool?.ExceuteAsync(context, tool.Arguments);
                    toolResults.Add(new ToolResult()
                    {
                        Content = [JsonSerializer.Serialize(obj)],
                        Name = tool.Name,
                        Status = Status.Success,
                        ToolCallId = tool.ToolCallId,
                    });
                }
                catch(Exception ex)
                {
                    toolResults.Add(new ToolResult()
                    {
                        Content = [ex.ToString()],
                        Name = tool.Name,
                        Status = Status.Error,
                        ToolCallId = tool.ToolCallId
                    });
                }
            }
            return toolResults;
        }
        private Task<LlmResponse> Think(LlmRequest request)
        {
            return _client.GenerateAsync(request);
        }
        private LlmRequest CreateLlmRequest(ExecutionContext executionContext)
        {
            var request = new LlmRequest
            {
                Instructions = string.IsNullOrEmpty(_instructions) ? new List<string>() : new List<string> { _instructions },
                Contents = executionContext.Events.SelectMany(e => e.Content).ToList(),
                Tools = _tools
            };
            return request;
        }
        private string ExtractFinalResult(Event @event)
        {
            var content = @event.Content.FirstOrDefault(a => a.Type == ContentType.Message && (a as Message)?.Role == Role.Assistant);
            if(content is not null)
            {
                return (content as Message)?.Content ?? string.Empty;
            }
            return string.Empty;
        }
        private bool IsFinalResponse(Event @event)
        {
            var hasTools = @event.Content.Any(a => a.Type == ContentType.ToolCall);
            var hasToolResult = @event.Content.Any(a => a.Type == ContentType.ToolResult);
            return !hasToolResult && !hasTools;
        }
    }
}
