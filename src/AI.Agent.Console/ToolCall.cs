namespace AI.Agent.ConsoleApp
{
    public class ToolCall : IContentItem
    {
        public ContentType Type { get; } = ContentType.ToolCall;
        public Dictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();
        public string Name { get; set; }
        public string ToolCallId { get; set; }
    }
}
