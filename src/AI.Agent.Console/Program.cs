using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using System.Buffers.Text;
using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;
using Tavily;

namespace AI.Agent.ConsoleApp
{
    public class Program
    {
        private static string calculator_tool_definition = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "calculator_tool_definition.json"));
        public static void Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder()
                                    .SetBasePath(Directory.GetCurrentDirectory())
                                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                                    .Build();

            // 2. Read individual values using colons for nested keys
            string? openAiApiKey = config["OPENAI_API_KEY"];
            string? huggingFaceToken = config["HF_TOKEN"];
            string? tavilyApiKey = config["TAVILY_API_KEY"];
            string? openRouterApiKey = config["OPEN_ROUTER_API_KEY"];
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", openAiApiKey);
            Environment.SetEnvironmentVariable("HF_TOKEN", huggingFaceToken);
            Environment.SetEnvironmentVariable("TAVILY_API_KEY", tavilyApiKey);
            Environment.SetEnvironmentVariable("OPEN_ROUTER_API_KEY", openRouterApiKey);

            IReadOnlyList<GaiaProblem> level1Problems = GaiaDataset.LoadAsync(
                "gaia-benchmark/GAIA",
                "2023_level1",
                "validation").GetAwaiter().GetResult();
            Console.WriteLine($"Number of Level 1 problems: {level1Problems.Count}");
            //var result = SearchWeb("Kipchoge's marathon world record").GetAwaiter().GetResult();
            //BuildMcpClient().GetAwaiter().GetResult();
            //OpenAIChat().GetAwaiter().GetResult();

            CallAgent().GetAwaiter().GetResult();
            Console.WriteLine("Hello, World!");
        }
        private static async Task CallAgent()
        {
            IList<McpClientTool> mcpClientTools = await GetMcpClientTools();
            var tools = new List<BaseTool>();
            var transportOptions = new StdioClientTransportOptions
            {
                Name = "Everything",
                Command = "npx",
                Arguments = ["-y", "tavily-mcp@latest"],
                EnvironmentVariables = new Dictionary<string, string?>()
                {
                    ["TAVILY_API_KEY"] = Environment.GetEnvironmentVariable("TAVILY_API_KEY")
                }
            };
            foreach (var item in mcpClientTools)
            {
                tools.Add(new McpTool(item));
            }
            var client = new LlmClient("openrouter/free", Environment.GetEnvironmentVariable("OPEN_ROUTER_API_KEY"), new Uri("https://openrouter.ai/api/v1"));
            var response = client.AskAsync("Who won 2025 Nobel Physics award").GetAwaiter().GetResult();
            var agent = new Agent(client, "You are a helpful assistant.", tools);
            var resp = await agent.Run("Who won 2025 Nobel Physics award");
            Console.WriteLine(resp.Output);
        }
        private static async Task<List<CreateSearchResponseResult>> SearchWeb(string query)
        {
            string apiKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY");
            using var client = new TavilyClient(apiKey);
            var response = await client.CreateSearchAsync(query: query);

            foreach (var result in response.Results)
            {
                Console.WriteLine($"{result.Title}: {result.Url}");
            }
            // Implementation for Tavily search
            return response.Results.ToList();
        }
        public static async Task<IList<McpClientTool>> GetMcpClientTools()
        {
            var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Everything",
                Command = "npx",
                Arguments = ["-y", "tavily-mcp@latest"],
                EnvironmentVariables = new Dictionary<string, string?>()
                {
                    ["TAVILY_API_KEY"] = Environment.GetEnvironmentVariable("TAVILY_API_KEY")
                }
            });

            var client = await McpClient.CreateAsync(clientTransport);
            return await client.ListToolsAsync();
        }
        public static async Task BuildMcpClient()
        {
            var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Everything",
                Command = "npx",
                Arguments = ["-y", "tavily-mcp@latest"],
                EnvironmentVariables = new Dictionary<string, string?>()
                {
                    ["TAVILY_API_KEY"] = Environment.GetEnvironmentVariable("TAVILY_API_KEY")
                }
            });

            var client = await McpClient.CreateAsync(clientTransport);
            foreach (var tool in await client.ListToolsAsync())
            {
                Console.WriteLine($"{tool.Name} ({tool.Description})");
            }
            var result = await client.CallToolAsync("tavily_search", new Dictionary<string, object?>() { ["query"] = "2025 Nobel Physics" }, cancellationToken: CancellationToken.None);

            Console.WriteLine(result.Content.OfType<TextContentBlock>().First().Text);
        }
        [Description("Calculates the result of a basic arithmetic operation.")]
        private static double CalculateNew(
            [Description("Arithmetic operation to perform")] string operation,
            [Description("First number for the calculation")] double a, 
            [Description("Second number for the calculation")] double b)
        {
            return operation switch
            {
                "add" => a + b,
                "subtract" => a - b,
                "multiply" => a * b,
                "divide" => a / b,
                _ => throw new ArgumentException("Invalid operation"),
            };
        }
        public static async Task OpenAIChat()
        {
            AIFunction calcTool = AIFunctionFactory.Create(CalculateNew);
            var client = new ChatClient(
                                model: "openrouter/free",//"google/gemma-4-31b-it:free",//"gpt-5.4-mini",
                                new ApiKeyCredential(Environment.GetEnvironmentVariable("OPEN_ROUTER_API_KEY")),
                                new OpenAIClientOptions() { Endpoint = new Uri("https://openrouter.ai/api/v1") }
                                )// Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                                .AsIChatClient();
            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                //new SystemChatMessage("You are a helpful assistant."),
                new Microsoft.Extensions.AI.ChatMessage(ChatRole.User,"Who won 2025 Nobel Physics award"),// "What is 1234 x 5678?"),
            };
            Microsoft.Extensions.AI.ChatOptions options = new ChatOptions();
            options.Tools = new List<AITool>() { calcTool };
            IList<McpClientTool> mcpClientTools = await GetMcpClientTools();
            var toolDict = new Dictionary<string, McpClientTool>();
            foreach (var item in mcpClientTools)
            {
                options.Tools.Add(item);
                toolDict.Add(item.Name, item);
            }
            bool requiresAction;

            do
            {
                requiresAction = false;
                var completion = await client.GetResponseAsync(messages, options);
                if (completion.FinishReason == Microsoft.Extensions.AI.ChatFinishReason.Stop)
                {
                    Console.WriteLine(completion.Text);
                }
                else if (completion.FinishReason == Microsoft.Extensions.AI.ChatFinishReason.ToolCalls)
                {
                    messages.AddMessages(completion);

                    var functionCalls = completion.Messages.SelectMany(m => m.Contents)
                                                           .OfType<FunctionCallContent>()
                                                           .ToList();
                    var toolResultContents = new List<AIContent>();
                    foreach (var toolCall in functionCalls)
                    {
                        var arguments = toolCall.Arguments is not null ? new AIFunctionArguments(toolCall.Arguments) : null;
                        if (toolCall.Name == "CalculateNew")
                        { 
                            var resul = await calcTool.InvokeAsync(arguments);
                            toolResultContents.Add(new FunctionResultContent(toolCall.CallId, resul));
                            //messages.Add(new ToolChatMessage(toolCall.Id, result.ToString()));
                        }
                        else if(toolDict.ContainsKey(toolCall.Name))
                        {
                            var toolResult = await toolDict[toolCall.Name].InvokeAsync(arguments);
                            toolResultContents.Add(new FunctionResultContent(toolCall.CallId, toolResult));
                        }
                    }
                    messages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Tool, toolResultContents));
                    requiresAction = true;
                }
            } while (requiresAction);
            Console.WriteLine("Test method called.");
        }
    }
}