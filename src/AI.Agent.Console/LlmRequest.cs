namespace AI.Agent.ConsoleApp
{
    public class LlmRequest
    {
        public List<string> Instructions { get; set; } = new List<string>();
        public List<IContentItem> Contents { get; set; } = new List<IContentItem>();
        public List<BaseTool> Tools { get; set; } = new List<BaseTool>();
        public string? ToolChoice { get; set; }
        public string? ModelId { get; set; }

        public void AppendInstructions(string text)
        {
            Instructions.Add(text);
        }
    }
}
