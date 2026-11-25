namespace AI.Agent.ConsoleApp
{
    public class ToolResult : IContentItem
    {
        public ContentType Type { get; } = ContentType.ToolResult;
        public string ToolCallId { get; set; }
        public string Name { get; set; }
        public Status Status { get; set; }
        public List<string> Content { get; set; }

    }
}
